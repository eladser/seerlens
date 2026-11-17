using Microsoft.Data.Sqlite;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class TraceStoreCostTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-cost-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
        try { File.Delete(_path + "-wal"); File.Delete(_path + "-shm"); } catch { }
    }

    TraceStore Store() => TraceStore.ForFile(_path);

    static IngestSpan Span(string id, string? parent, string kind, long start, string? model = "gpt-4o",
        long? pin = 1000, long? pout = 200, long? cr = null, long? cw = null, long? rs = null,
        string? op = null, string? prm = null) =>
        new(id, parent, id, kind, start, 100, model, pin, pout, null, null, null, cr, cw, rs, op, prm);

    static IngestTrace Trace(string id, long start, string? model, params IngestSpan[] spans) =>
        new(id, id, start, 100, null, model, "ok", spans);

    // today's DDL, as a 1.5.0 file has it
    void MakeOldFile()
    {
        using var db = new SqliteConnection($"Data Source={_path}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            create table traces (
                id text primary key, name text not null, started_at integer not null, duration_ms real not null,
                provider text, model text, status text not null, prompt_tokens integer, completion_tokens integer, cost_usd real);
            create table spans (
                id text primary key, trace_id text not null, parent_id text, name text not null, kind text not null,
                started_at integer not null, duration_ms real not null, model text, prompt_tokens integer,
                completion_tokens integer, cost_usd real, prompt_text text, completion_text text, error text);
            create index ix_spans_trace on spans(trace_id);
            insert into traces values ('old', 'old chat', 5000, 800, 'openai', 'gpt-4o', 'ok', 1000, 500, 0.0075);
            insert into spans values ('old-s1', 'old', null, 'chat', 'llm', 5000, 800, 'gpt-4o', 1000, 500, 0.0075, 'hi', 'yo', null);
            """;
        cmd.ExecuteNonQuery();
    }

    List<string> Columns()
    {
        using var db = new SqliteConnection($"Data Source={_path}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "select name from pragma_table_info('spans')";
        using var r = cmd.ExecuteReader();
        var cols = new List<string>();
        while (r.Read()) cols.Add(r.GetString(0));
        return cols;
    }

    [Fact]
    public void Migration_keeps_old_rows_and_adds_the_new_columns()
    {
        MakeOldFile();

        var store = Store();
        var d = store.Get("old")!;

        Assert.Equal(0.0075, d.Trace.CostUsd!.Value, 9);
        Assert.Equal(1000, d.Trace.PromptTokens);
        var s = Assert.Single(d.Spans);
        Assert.Equal(0.0075, s.CostUsd!.Value, 9);
        Assert.Equal("hi", s.PromptText);
        Assert.Null(s.CacheReadTokens);
        Assert.Null(s.CacheWriteTokens);
        Assert.Null(s.ReasoningTokens);
        Assert.Null(s.Operation);
        Assert.Null(s.Params);
        Assert.Contains("cache_read_tokens", Columns());
        Assert.Contains("params", Columns());
    }

    [Fact]
    public void Migration_twice_is_a_no_op()
    {
        MakeOldFile();
        Store();
        var once = Columns();

        var again = Store();

        Assert.Equal(once, Columns());
        Assert.Equal(once.Count, once.Distinct().Count());
        Assert.NotNull(again.Get("old"));
    }

    [Fact]
    public void A_fresh_file_has_the_new_columns_too()
    {
        Store();
        var cols = Columns();
        foreach (var c in new[] { "cache_read_tokens", "cache_write_tokens", "reasoning_tokens", "operation", "params" })
            Assert.Contains(c, cols);
    }

    [Fact]
    public void A_later_batch_to_an_old_trace_accumulates()
    {
        MakeOldFile();
        var store = Store();

        var sum = store.Add(Trace("old", 5000, "gpt-4o", Span("old-s2", "old-s1", "llm", 5100, pin: 1000, pout: 200)));

        Assert.Equal(2000, sum.PromptTokens);
        Assert.Equal(700, sum.CompletionTokens);
        Assert.Equal(0.0075 + 0.0045, sum.CostUsd!.Value, 9);
    }

    [Fact]
    public void New_span_fields_round_trip_and_cache_tokens_are_priced()
    {
        var store = Store();
        // 1000 in incl. 600 cached, 100 out: 400 @2.50 + 600 @1.25 + 100 @10
        store.Add(Trace("t", 1000, "gpt-4o",
            Span("s", null, "llm", 1000, pin: 1000, pout: 100, cr: 600, cw: 0, rs: 40, op: "chat", prm: """{"temperature":0.2}""")));

        var s = Assert.Single(store.Get("t")!.Spans);
        Assert.Equal(600, s.CacheReadTokens);
        Assert.Equal(0, s.CacheWriteTokens);
        Assert.Equal(40, s.ReasoningTokens);
        Assert.Equal("chat", s.Operation);
        Assert.Equal("""{"temperature":0.2}""", s.Params);
        Assert.Equal(0.001 + 0.00075 + 0.001, s.CostUsd!.Value, 9);
    }

    [Fact]
    public void An_agent_span_with_an_llm_child_is_not_counted_twice()
    {
        var store = Store();
        var sum = store.Add(Trace("t", 1000, "gpt-4o",
            Span("agent", null, "agent", 1000, op: "invoke_agent"),
            Span("chat", "agent", "llm", 1010)));

        Assert.Equal(1000, sum.PromptTokens);
        Assert.Equal(200, sum.CompletionTokens);
        Assert.Equal(0.0045, sum.CostUsd!.Value, 9);

        // the agent span itself is still stored and priced
        var agent = store.Get("t")!.Spans.Single(s => s.Id == "agent");
        Assert.Equal("agent", agent.Kind);
        Assert.Equal(0.0045, agent.CostUsd!.Value, 9);
    }

    [Fact]
    public void Nested_agents_count_the_llm_once()
    {
        var store = Store();
        var sum = store.Add(Trace("t", 1000, "gpt-4o",
            Span("outer", null, "agent", 1000, op: "invoke_workflow"),
            Span("inner", "outer", "agent", 1005, op: "invoke_agent"),
            Span("tool", "inner", "tool", 1008, model: null, pin: null, pout: null),
            Span("chat", "tool", "llm", 1010)));

        Assert.Equal(1000, sum.PromptTokens);
        Assert.Equal(200, sum.CompletionTokens);
        Assert.Equal(0.0045, sum.CostUsd!.Value, 9);
    }

    [Fact]
    public void A_childless_agent_span_still_counts()
    {
        var store = Store();
        var sum = store.Add(Trace("t", 1000, "gpt-4o",
            Span("remote", null, "agent", 1000, op: "invoke_agent"),
            Span("local", null, "llm", 1010, pin: 50, pout: 5)));

        Assert.Equal(1050, sum.PromptTokens);
        Assert.Equal(205, sum.CompletionTokens);
    }

    [Fact]
    public void The_agent_rule_holds_when_the_child_arrives_in_a_later_batch()
    {
        var store = Store();
        var first = store.Add(Trace("t", 1000, "gpt-4o", Span("agent", null, "agent", 1000, op: "invoke_agent")));
        Assert.Equal(1000, first.PromptTokens);

        var second = store.Add(Trace("t", 1000, "gpt-4o", Span("chat", "agent", "llm", 1010)));

        Assert.Equal(1000, second.PromptTokens);
        Assert.Equal(0.0045, second.CostUsd!.Value, 9);
    }

    [Fact]
    public void An_agent_with_only_a_tool_child_still_counts()
    {
        var store = Store();
        var sum = store.Add(Trace("t", 1000, "gpt-4o",
            Span("agent", null, "agent", 1000, op: "invoke_agent"),
            Span("tool", "agent", "tool", 1010, model: null, pin: null, pout: null)));

        Assert.Equal(1000, sum.PromptTokens);
    }

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public void Hit_rate_normalizes_exclusive_counts_and_skips_rows_without_cache_data()
    {
        var store = Store();
        // inclusive: 600 of 1000
        store.Add(Trace("a", Now, "gpt-4o", Span("a1", null, "llm", Now, pin: 1000, pout: 10, cr: 600)));
        // exclusive (cache 900 > input 100): denominator is 100 + 900
        store.Add(Trace("b", Now, "claude-opus-4-5", Span("b1", null, "llm", Now, model: "claude-opus-4-5", pin: 100, pout: 10, cr: 900)));
        // no cache data at all: left out of both sums
        store.Add(Trace("c", Now, "gpt-4o", Span("c1", null, "llm", Now, pin: 5000, pout: 10)));

        var spend = store.SpendReport(Now - 100_000, Now - 100_000);

        Assert.Equal(1500.0 / 2000.0, spend.CacheHitRate!.Value, 9);
        Assert.InRange(spend.CacheHitRate!.Value, 0, 1);
        Assert.Equal(0.6, spend.ByModel.Single(m => m.Model == "gpt-4o").CacheHitRate!.Value, 9);
        Assert.Equal(0.9, spend.ByModel.Single(m => m.Model == "claude-opus-4-5").CacheHitRate!.Value, 9);
    }

    [Fact]
    public void Hit_rate_is_null_without_cache_data_and_by_model_keys_line_up()
    {
        var store = Store();
        store.Add(Trace("a", Now, "gpt-4o", Span("a1", null, "llm", Now)));
        store.Add(Trace("n", Now, null, Span("n1", null, "llm", Now, model: null, cr: 5)));

        var spend = store.SpendReport(Now - 100_000, Now - 100_000);

        Assert.Null(spend.ByModel.Single(m => m.Model == "gpt-4o").CacheHitRate);
        Assert.NotNull(spend.ByModel.Single(m => m.Model == "unknown").CacheHitRate);
        Assert.Equal(2, spend.ByModel.Count);
    }

    [Fact]
    public void Month_to_date_hit_rate_ignores_older_traces_and_is_null_when_empty()
    {
        var store = Store();
        Assert.Null(store.SpendReport(0, 0).CacheHitRate);

        var old = Now - 10_000_000;
        store.Add(Trace("old", old, "gpt-4o", Span("o1", null, "llm", old, pin: 1000, pout: 1, cr: 1000)));
        store.Add(Trace("new", Now, "gpt-4o", Span("n1", null, "llm", Now, pin: 1000, pout: 1, cr: 100)));

        var spend = store.SpendReport(Now - 100_000, 0);

        Assert.Equal(0.1, spend.CacheHitRate!.Value, 9);
        Assert.Equal(1100.0 / 2000.0, spend.ByModel.Single(m => m.Model == "gpt-4o").CacheHitRate!.Value, 9);
    }

    [Fact]
    public void Agent_spans_stay_out_of_the_hit_rate()
    {
        var store = Store();
        store.Add(Trace("t", Now, "gpt-4o",
            Span("ag", null, "agent", Now, cr: 1000, op: "invoke_agent"),
            Span("ch", "ag", "llm", Now, pin: 1000, cr: 250)));

        Assert.Equal(0.25, store.SpendReport(Now - 100_000, 0).CacheHitRate!.Value, 9);
    }
}
