using Microsoft.Data.Sqlite;

namespace Seerlens.Collector;

public sealed class TraceStore
{
    readonly string _conn;

    public TraceStore(string connectionString)
    {
        _conn = connectionString;
        Init();
    }

    public static TraceStore ForFile(string path) =>
        new($"Data Source={path}");

    void Init()
    {
        using var db = Open();
        Exec(db, "pragma journal_mode=wal;");
        Exec(db, """
            create table if not exists traces (
                id text primary key,
                name text not null,
                started_at integer not null,
                duration_ms real not null,
                provider text,
                model text,
                status text not null,
                prompt_tokens integer,
                completion_tokens integer,
                cost_usd real
            );
            create table if not exists spans (
                id text primary key,
                trace_id text not null,
                parent_id text,
                name text not null,
                kind text not null,
                started_at integer not null,
                duration_ms real not null,
                model text,
                prompt_tokens integer,
                completion_tokens integer,
                cost_usd real,
                prompt_text text,
                completion_text text,
                error text
            );
            create index if not exists ix_spans_trace on spans(trace_id);
            """);
        Migrate(db);
    }

    static readonly (string Name, string Type)[] SpanColumns =
    [
        ("cache_read_tokens", "integer"),
        ("cache_write_tokens", "integer"),
        ("reasoning_tokens", "integer"),
        ("operation", "text"),
        ("params", "text"),
    ];

    // Adds span columns that older files don't have. begin immediate takes the write lock up
    // front, so two collectors opening one file can't both see a column missing and add it twice.
    static void Migrate(SqliteConnection db)
    {
        Exec(db, "begin immediate;");
        try
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "select name from pragma_table_info('spans');";
                using var r = cmd.ExecuteReader();
                while (r.Read()) have.Add(r.GetString(0));
            }
            foreach (var (name, type) in SpanColumns)
                if (!have.Contains(name))
                    Exec(db, $"alter table spans add column {name} {type};");
            Exec(db, "commit;");
        }
        catch
        {
            try { Exec(db, "rollback;"); } catch { }
            throw;
        }
    }

    // Stores the trace and its spans, pricing each llm span. BatchSpanProcessor can split
    // one trace across several OTLP exports, so trace-level totals are recomputed from every
    // span on file for the id, not just the ones in this call - otherwise the last batch in
    // wins and earlier tokens/cost/duration just vanish.
    public TraceSummary Add(IngestTrace t)
    {
        var src = t.Spans ?? []; // a body that omits "spans" shouldn't NRE
        var spans = new List<SpanRow>(src.Count);
        foreach (var s in src)
        {
            // agent spans are priced too, but trace totals skip the ones that have llm children
            var spanCost = s.Kind is "llm" or "agent"
                ? Pricing.CostFor(s.Model ?? t.Model,
                    new TokenUsage(s.PromptTokens, s.CompletionTokens, s.CacheReadTokens, s.CacheWriteTokens, s.ReasoningTokens))
                : null;

            spans.Add(new SpanRow(s.Id, s.ParentId, s.Name, s.Kind, s.StartedAt, s.DurationMs,
                s.Model, s.PromptTokens, s.CompletionTokens, spanCost, s.PromptText, s.CompletionText, s.Error,
                s.CacheReadTokens, s.CacheWriteTokens, s.ReasoningTokens, s.Operation, s.Params));
        }

        using var db = Open();
        using var tx = db.BeginTransaction(); // rolls back if a span insert throws before Commit

        // name/status aren't touched on conflict, so the first batch (which usually carries
        // the root span) keeps naming the trace; provider/model fill in from later batches
        // if the first one didn't have them yet.
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                insert into traces
                (id, name, started_at, duration_ms, provider, model, status, prompt_tokens, completion_tokens, cost_usd)
                values ($id, $name, $started, $dur, $provider, $model, $status, null, null, null)
                on conflict(id) do update set
                    started_at = min(traces.started_at, excluded.started_at),
                    provider = coalesce(traces.provider, excluded.provider),
                    model = coalesce(traces.model, excluded.model);
                """;
            Bind(cmd, "$id", t.Id);
            Bind(cmd, "$name", t.Name);
            Bind(cmd, "$started", t.StartedAt);
            Bind(cmd, "$dur", t.DurationMs);
            Bind(cmd, "$provider", t.Provider);
            Bind(cmd, "$model", t.Model);
            Bind(cmd, "$status", t.Status);
            cmd.ExecuteNonQuery();
        }

        foreach (var s in spans)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                insert or replace into spans
                (id, trace_id, parent_id, name, kind, started_at, duration_ms, model,
                 prompt_tokens, completion_tokens, cost_usd, prompt_text, completion_text, error,
                 cache_read_tokens, cache_write_tokens, reasoning_tokens, operation, params)
                values ($id, $trace, $parent, $name, $kind, $started, $dur, $model,
                 $pt, $ct, $cost, $prompt, $completion, $error,
                 $cr, $cw, $rs, $op, $params);
                """;
            Bind(cmd, "$id", s.Id);
            Bind(cmd, "$trace", t.Id);
            Bind(cmd, "$parent", s.ParentId);
            Bind(cmd, "$name", s.Name);
            Bind(cmd, "$kind", s.Kind);
            Bind(cmd, "$started", s.StartedAt);
            Bind(cmd, "$dur", s.DurationMs);
            Bind(cmd, "$model", s.Model);
            Bind(cmd, "$pt", s.PromptTokens);
            Bind(cmd, "$ct", s.CompletionTokens);
            Bind(cmd, "$cost", s.CostUsd);
            Bind(cmd, "$prompt", s.PromptText);
            Bind(cmd, "$completion", s.CompletionText);
            Bind(cmd, "$error", s.Error);
            Bind(cmd, "$cr", s.CacheReadTokens);
            Bind(cmd, "$cw", s.CacheWriteTokens);
            Bind(cmd, "$rs", s.ReasoningTokens);
            Bind(cmd, "$op", s.Operation);
            Bind(cmd, "$params", s.Params);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = db.CreateCommand())
        {
            // An agent span's usage is the total of the model calls under it, so it's left out of
            // the token and cost sums when any llm span sits below it (up walks the ancestors of
            // llm spans). Duration and errors still look at every span.
            cmd.CommandText = """
                with recursive up(pid) as (
                    select parent_id from spans where trace_id = $id and kind = 'llm' and parent_id is not null
                    union
                    select s.parent_id from spans s join up on s.id = up.pid where s.trace_id = $id and s.parent_id is not null
                ),
                counted as (
                    select * from spans
                    where trace_id = $id and not (kind = 'agent' and id in (select pid from up))
                )
                update traces set
                    duration_ms = coalesce((select max(started_at + duration_ms) - min(started_at) from spans where trace_id = $id), duration_ms),
                    prompt_tokens = (select sum(prompt_tokens) from counted),
                    completion_tokens = (select sum(completion_tokens) from counted),
                    cost_usd = (select sum(cost_usd) from counted),
                    status = case when exists(select 1 from spans where trace_id = $id and error is not null) then 'error' else status end
                where id = $id;
                """;
            Bind(cmd, "$id", t.Id);
            cmd.ExecuteNonQuery();
        }

        TraceSummary summary;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select id, name, started_at, duration_ms, provider, model, status,
                       prompt_tokens, completion_tokens, cost_usd
                from traces where id = $id;
                """;
            Bind(cmd, "$id", t.Id);
            using var r = cmd.ExecuteReader();
            r.Read();
            summary = ReadSummary(r);
        }

        tx.Commit();
        return summary;
    }

    public List<TraceSummary> List(int limit = 200)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            select id, name, started_at, duration_ms, provider, model, status,
                   prompt_tokens, completion_tokens, cost_usd
            from traces order by started_at desc limit $limit;
            """;
        Bind(cmd, "$limit", limit);

        var rows = new List<TraceSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(ReadSummary(r));
        return rows;
    }

    public TraceDetail? Get(string id)
    {
        using var db = Open();

        TraceSummary? trace = null;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select id, name, started_at, duration_ms, provider, model, status,
                       prompt_tokens, completion_tokens, cost_usd
                from traces where id = $id;
                """;
            Bind(cmd, "$id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read()) trace = ReadSummary(r);
        }
        if (trace is null) return null;

        var spans = new List<SpanRow>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select id, parent_id, name, kind, started_at, duration_ms, model,
                       prompt_tokens, completion_tokens, cost_usd, prompt_text, completion_text, error,
                       cache_read_tokens, cache_write_tokens, reasoning_tokens, operation, params
                from spans where trace_id = $id order by started_at;
                """;
            Bind(cmd, "$id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                spans.Add(new SpanRow(
                    r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetInt64(4), r.GetDouble(5), r.IsDBNull(6) ? null : r.GetString(6),
                    Long(r, 7), Long(r, 8), Dbl(r, 9),
                    r.IsDBNull(10) ? null : r.GetString(10),
                    r.IsDBNull(11) ? null : r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetString(12),
                    Long(r, 13), Long(r, 14), Long(r, 15),
                    r.IsDBNull(16) ? null : r.GetString(16),
                    r.IsDBNull(17) ? null : r.GetString(17)));
            }
        }

        return new TraceDetail(trace, spans);
    }

    public void Delete(string id)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Run(db, "delete from spans where trace_id = $id", id);
        Run(db, "delete from traces where id = $id", id);
        tx.Commit();
    }

    public void Clear()
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Exec(db, "delete from spans;");
        Exec(db, "delete from traces;");
        tx.Commit();
    }

    // Drops traces that started before the cutoff (unix ms), spans first, and returns
    // how many traces went.
    // Deletes in chunks, one transaction each, so a big backlog doesn't hold the write lock for long.
    public int DeleteOlderThan(long cutoffMs, int chunk = 5000)
    {
        using var db = Open();
        var total = 0;
        while (true)
        {
            using var tx = db.BeginTransaction();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    delete from spans where trace_id in
                        (select id from traces where started_at < $cutoff order by started_at limit $chunk);
                    """;
                Bind(cmd, "$cutoff", cutoffMs);
                Bind(cmd, "$chunk", chunk);
                cmd.ExecuteNonQuery();
            }

            int n;
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    delete from traces where id in
                        (select id from traces where started_at < $cutoff order by started_at limit $chunk);
                    """;
                Bind(cmd, "$cutoff", cutoffMs);
                Bind(cmd, "$chunk", chunk);
                n = cmd.ExecuteNonQuery();
            }
            tx.Commit();
            total += n;
            if (n < chunk) return total;
        }
    }

    static void Run(SqliteConnection db, string sql, string id)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, "$id", id);
        cmd.ExecuteNonQuery();
    }

    // A cheap month-to-date total, for checking the budget right after an ingest
    // without paying for the full SpendReport breakdown.
    public double MonthToDateCost(long monthStartMs)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "select coalesce(sum(cost_usd), 0) from traces where started_at >= $month;";
        Bind(cmd, "$month", monthStartMs);
        return Convert.ToDouble(cmd.ExecuteScalar()); // sum() with no rows comes back as an integer 0
    }

    public Stats Stats()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "select count(*), coalesce(sum(cost_usd), 0), coalesce(avg(duration_ms), 0) from traces;";
        using var r = cmd.ExecuteReader();
        r.Read();
        return new Stats(r.GetInt32(0), r.GetDouble(1), r.GetDouble(2));
    }

    // Spend rollups for the cost view. monthStart bounds month-to-date; daily
    // covers the window from sinceMs to now, bucketed by day.
    public Spend SpendReport(long monthStartMs, long sinceMs)
    {
        using var db = Open();

        double mtd, total;
        double? mtdHit;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select coalesce(sum(case when started_at >= $month then cost_usd end), 0),
                       coalesce(sum(cost_usd), 0)
                from traces;
                """;
            Bind(cmd, "$month", monthStartMs);
            using var r = cmd.ExecuteReader();
            r.Read();
            mtd = r.GetDouble(0);
            total = r.GetDouble(1);
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"""
                select {HitRateSql} from spans s join traces t on t.id = s.trace_id
                where t.started_at >= $month and s.kind = 'llm' and s.cache_read_tokens is not null;
                """;
            Bind(cmd, "$month", monthStartMs);
            var v = cmd.ExecuteScalar();
            mtdHit = v is double d ? d : null;
        }

        // same grouping key as the by-model query below, so the rows line up
        var hitByModel = new Dictionary<string, double>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"""
                select coalesce(t.model, 'unknown'), {HitRateSql}
                from spans s join traces t on t.id = s.trace_id
                where s.kind = 'llm' and s.cache_read_tokens is not null
                group by coalesce(t.model, 'unknown');
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(1)) hitByModel[r.GetString(0)] = r.GetDouble(1);
        }

        var byModel = new List<ModelSpend>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select coalesce(model, 'unknown'), coalesce(sum(cost_usd), 0), count(*),
                       coalesce(sum(coalesce(prompt_tokens,0) + coalesce(completion_tokens,0)), 0)
                from traces group by model order by sum(cost_usd) desc;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                byModel.Add(new ModelSpend(r.GetString(0), r.GetDouble(1), r.GetInt32(2), r.GetInt64(3),
                    hitByModel.TryGetValue(r.GetString(0), out var hit) ? hit : null));
        }

        var daily = new List<DaySpend>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                select date(started_at / 1000, 'unixepoch') as day, coalesce(sum(cost_usd), 0)
                from traces where started_at >= $since
                group by day order by day;
                """;
            Bind(cmd, "$since", sinceMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                daily.Add(new DaySpend(r.GetString(0), r.GetDouble(1)));
        }

        return new Spend(mtd, total, byModel, daily, mtdHit);
    }

    // Cache reads over the prompt size, using the same inclusive/exclusive rule as Pricing:
    // when cache reads + writes exceed input, input left the cache out, so it's added back.
    // That keeps the rate at or under 1. The nullif makes an all-zero denominator null.
    const string HitRateSql = """
        sum(s.cache_read_tokens) * 1.0 / nullif(sum(case
            when coalesce(s.cache_read_tokens, 0) + coalesce(s.cache_write_tokens, 0) > coalesce(s.prompt_tokens, 0)
                then coalesce(s.prompt_tokens, 0) + coalesce(s.cache_read_tokens, 0) + coalesce(s.cache_write_tokens, 0)
            else coalesce(s.prompt_tokens, 0) end), 0)
        """;

    static TraceSummary ReadSummary(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetDouble(3),
        r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
        r.GetString(6), Long(r, 7), Long(r, 8), Dbl(r, 9));

    static long? Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    static double? Dbl(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    SqliteConnection Open()
    {
        var db = new SqliteConnection(_conn);
        db.Open();
        return db;
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static void Bind(SqliteCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
