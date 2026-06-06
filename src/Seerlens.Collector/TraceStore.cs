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
    }

    // Stores the trace and its spans, pricing each llm span. Returns the list summary.
    public TraceSummary Add(IngestTrace t)
    {
        long? promptTokens = null, completionTokens = null;
        double? cost = null;

        var src = t.Spans ?? []; // a body that omits "spans" shouldn't NRE
        var spans = new List<SpanRow>(src.Count);
        foreach (var s in src)
        {
            var spanCost = s.Kind == "llm"
                ? Pricing.CostFor(s.Model ?? t.Model, s.PromptTokens, s.CompletionTokens)
                : null;

            if (s.PromptTokens is { } p) promptTokens = (promptTokens ?? 0) + p;
            if (s.CompletionTokens is { } c) completionTokens = (completionTokens ?? 0) + c;
            if (spanCost is { } sc) cost = (cost ?? 0) + sc;

            spans.Add(new SpanRow(s.Id, s.ParentId, s.Name, s.Kind, s.StartedAt, s.DurationMs,
                s.Model, s.PromptTokens, s.CompletionTokens, spanCost, s.PromptText, s.CompletionText, s.Error));
        }

        var summary = new TraceSummary(t.Id, t.Name, t.StartedAt, t.DurationMs, t.Provider,
            t.Model, t.Status, promptTokens, completionTokens, cost);

        using var db = Open();
        using var tx = db.BeginTransaction(); // rolls back if a span insert throws before Commit

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                insert or replace into traces
                (id, name, started_at, duration_ms, provider, model, status, prompt_tokens, completion_tokens, cost_usd)
                values ($id, $name, $started, $dur, $provider, $model, $status, $pt, $ct, $cost);
                """;
            Bind(cmd, "$id", summary.Id);
            Bind(cmd, "$name", summary.Name);
            Bind(cmd, "$started", summary.StartedAt);
            Bind(cmd, "$dur", summary.DurationMs);
            Bind(cmd, "$provider", summary.Provider);
            Bind(cmd, "$model", summary.Model);
            Bind(cmd, "$status", summary.Status);
            Bind(cmd, "$pt", summary.PromptTokens);
            Bind(cmd, "$ct", summary.CompletionTokens);
            Bind(cmd, "$cost", summary.CostUsd);
            cmd.ExecuteNonQuery();
        }

        foreach (var s in spans)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                insert or replace into spans
                (id, trace_id, parent_id, name, kind, started_at, duration_ms, model,
                 prompt_tokens, completion_tokens, cost_usd, prompt_text, completion_text, error)
                values ($id, $trace, $parent, $name, $kind, $started, $dur, $model,
                 $pt, $ct, $cost, $prompt, $completion, $error);
                """;
            Bind(cmd, "$id", s.Id);
            Bind(cmd, "$trace", summary.Id);
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
            cmd.ExecuteNonQuery();
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
                       prompt_tokens, completion_tokens, cost_usd, prompt_text, completion_text, error
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
                    r.IsDBNull(12) ? null : r.GetString(12)));
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

    static void Run(SqliteConnection db, string sql, string id)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, "$id", id);
        cmd.ExecuteNonQuery();
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
                byModel.Add(new ModelSpend(r.GetString(0), r.GetDouble(1), r.GetInt32(2), r.GetInt64(3)));
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

        return new Spend(mtd, total, byModel, daily);
    }

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
