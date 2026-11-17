using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Seerlens.Collector;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class RetentionStartupTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-ret-{Guid.NewGuid():N}.db");
    readonly TraceStore _store;

    public RetentionStartupTests() => _store = TraceStore.ForFile(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    static IngestTrace T(string id, long at) => new(id, "chat", at, 10, "openai", "gpt-4o", "ok",
    [
        new IngestSpan($"{id}-1", null, "chat", "llm", at, 10, "gpt-4o", 10, 5, "q", "a", null),
        new IngestSpan($"{id}-2", $"{id}-1", "t", "tool", at + 1, 1, null, null, null, null, null, null),
    ]);

    // blocks the first info log (written after the prune) until released, so a prune run inside StartAsync would hang it
    sealed class GatedLog(ManualResetEventSlim gate) : Microsoft.Extensions.Logging.ILogger<TraceRetention>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel l) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel l, Microsoft.Extensions.Logging.EventId id, TState s, Exception? e, Func<TState, Exception?, string> f)
        {
            if (l == Microsoft.Extensions.Logging.LogLevel.Information) gate.Wait(TimeSpan.FromSeconds(20));
        }
    }

    long Count(string table)
    {
        using var db = new SqliteConnection($"Data Source={_path}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"select count(*) from {table};";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task Execute_hands_back_before_the_first_prune_runs()
    {
        var now = DateTimeOffset.UtcNow;
        _store.Add(T("old", now.AddDays(-40).ToUnixTimeMilliseconds()));
        _store.Add(T("new", now.ToUnixTimeMilliseconds()));
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SEERLENS_RETENTION_DAYS"] = "30" }).Build();
        var gate = new ManualResetEventSlim();
        using var svc = new TraceRetention(_store, cfg, new GatedLog(gate));

        // .NET 10 no longer runs the sync part of ExecuteAsync on the startup path itself, so call it directly
        using var cts = new CancellationTokenSource();
        var exec = typeof(TraceRetention).GetMethod("ExecuteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var start = Task.Run(() => { exec.Invoke(svc, [cts.Token]); });
        var first = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(start, first);

        gate.Set();
        var sw = Stopwatch.StartNew();
        while (_store.List().Count > 1 && sw.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(50);
        cts.Cancel();

        Assert.Equal(["new"], _store.List().Select(t => t.Id).ToList());
    }

    [Fact]
    public void Chunked_delete_removes_everything_older_and_nothing_newer()
    {
        for (var i = 0; i < 10; i++) _store.Add(T($"old{i}", 1000 + i));
        for (var i = 0; i < 4; i++) _store.Add(T($"new{i}", 9000 + i));

        var n = _store.DeleteOlderThan(5000, chunk: 3);

        Assert.Equal(10, n);
        Assert.Equal(4, Count("traces"));
        Assert.Equal(8, Count("spans"));
        Assert.All(_store.List(), t => Assert.StartsWith("new", t.Id));
    }

    [Fact]
    public void Chunk_boundary_that_divides_evenly_still_ends()
    {
        for (var i = 0; i < 6; i++) _store.Add(T($"old{i}", 1000 + i));
        Assert.Equal(6, _store.DeleteOlderThan(5000, chunk: 3));
        Assert.Equal(0, Count("traces"));
    }
}

public class RunDiffTotalsTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-tot-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void Childless_agent_span_counts_in_the_diff_header_like_in_the_trace_list()
    {
        var store = TraceStore.ForFile(_path);
        var sum = store.Add(new IngestTrace("t", "agent", 1000, 100, "openai", "gpt-4o", "ok",
        [
            new IngestSpan("ag", null, "invoke_agent", "agent", 1000, 100, "gpt-4o", 1000, 500, null, null, null),
            new IngestSpan("tool", "ag", "search", "tool", 1010, 10, null, null, null, null, null, null),
        ]));
        var detail = store.Get("t")!;

        var t = RunDiff.Compare(detail, detail).TotalsA;

        Assert.Equal(1000, sum.PromptTokens);
        Assert.Equal(sum.PromptTokens, t.InputTokens);
        Assert.Equal(sum.CompletionTokens, t.OutputTokens);
        Assert.NotNull(sum.CostUsd);
        Assert.Equal(sum.CostUsd!.Value, t.CostUsd!.Value, 9);
    }

    [Fact]
    public void Agent_with_a_nested_llm_below_it_is_left_out()
    {
        var store = TraceStore.ForFile(_path);
        var sum = store.Add(new IngestTrace("t", "agent", 1000, 100, "openai", "gpt-4o", "ok",
        [
            new IngestSpan("ag", null, "invoke_agent", "agent", 1000, 100, "gpt-4o", 5000, 5000, null, null, null),
            new IngestSpan("mid", "ag", "x", "other", 1001, 90, null, null, null, null, null, null),
            new IngestSpan("llm", "mid", "chat", "llm", 1002, 50, "gpt-4o", 100, 50, null, null, null),
        ]));

        var t = RunDiff.Compare(store.Get("t")!, store.Get("t")!).TotalsA;

        Assert.Equal(100, t.InputTokens);
        Assert.Equal(sum.PromptTokens, t.InputTokens);
        Assert.Equal(sum.CostUsd!.Value, t.CostUsd!.Value, 9);
    }
}

public class TextBoundsTests : IClassFixture<DiffEndpointTests.SeededFactory>
{
    readonly HttpClient _c;
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public TextBoundsTests(DiffEndpointTests.SeededFactory f) => _c = f.CreateClient();

    [Fact]
    public void Lines_over_the_word_limit_diff_as_plain_mod_rows()
    {
        var a = "head " + new string('x', 6000) + " tail";
        var b = "head " + new string('y', 6000) + " tail";

        var row = Assert.Single(TextDiff.Diff(a, b).Rows);

        Assert.Equal("mod", row.T);
        Assert.Equal(a, row.A);
        Assert.Equal(b, row.B);
        Assert.Null(row.AWords);
        Assert.Null(row.BWords);
    }

    [Fact]
    public void Lines_under_the_limit_keep_word_pieces()
    {
        var row = Assert.Single(TextDiff.Diff("one two three", "one 2 three").Rows);
        Assert.NotNull(row.AWords);
    }

    [Fact]
    public void Raw_response_is_cut_at_max_chars_and_flagged()
    {
        var big = new string('z', TextDiff.MaxChars + 10);

        var r = StepRawResponse.Of(big, "short");

        Assert.True(r.Truncated);
        Assert.Equal(TextDiff.MaxChars, r.Input!.Length);
        Assert.Equal("short", r.Output);
        Assert.False(StepRawResponse.Of("a", null).Truncated);
    }

    [Fact]
    public async Task Step_endpoint_truncates_a_huge_single_side()
    {
        var big = new string('q', TextDiff.MaxChars + 500);
        await _c.PostAsJsonAsync("/ingest", new IngestTrace("big-1", "x", 1000, 10, "openai", "gpt-4o", "ok",
            [new IngestSpan("bs", null, "chat", "llm", 1000, 10, "gpt-4o", 1, 1, big, "ok", null)]));
        await _c.PostAsJsonAsync("/ingest", new IngestTrace("big-2", "x", 2000, 10, "openai", "gpt-4o", "ok",
            [new IngestSpan("bs2", null, "chat", "llm", 2000, 10, "gpt-4o", 1, 1, "p", "ok", null)]));

        var res = await _c.GetFromJsonAsync<StepRawResponse>("/api/diff/step?a=big-1&b=big-2&sa=bs", Web);

        Assert.True(res!.Truncated);
        Assert.Equal(TextDiff.MaxChars, res.Input!.Length);
    }
}

public class BaselineLoadTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-bl-{Guid.NewGuid():N}.db");
    readonly EvalStore _store;

    public BaselineLoadTests() => _store = EvalStore.ForFile(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    static EvalCaseIn Big(string input, double score, string id, int kb) => new(input, "a", score, id,
        [new EvalStep("tool", "search", null, 0, 1, null, null, null, null, new string('x', kb * 1024))]);

    [Fact]
    public void Baselines_are_the_same_with_big_step_payloads_on_earlier_runs()
    {
        for (var i = 0; i < 20; i++)
            _store.Add(new EvalRunIn($"r{i}", "s", "gpt-4o", "keyword", 1000 + i, 0.5,
                [Big("q", i == 3 ? 1.0 : 0.2, "c1", 512), Big("other", 0.9, "c2", 512)]));
        _store.Add(new EvalRunIn("now", "s", "gpt-4o", "keyword", 5000, 0.1, [Big("q", 0.1, "c1", 1), Big("other", 0.9, "c2", 1)]));

        var view = _store.GetWithBaselines("now")!;

        var b = view.Cases[0].Baseline!;
        Assert.Equal("r3", b.RunId);
        Assert.Equal(0, b.CaseIdx);
        Assert.Equal(1.0, b.Score);
        Assert.Equal(1003, b.CreatedAt);
        Assert.Null(view.Cases[1].Baseline);
    }

    [Fact]
    public void Earlier_runs_come_back_without_their_steps()
    {
        _store.Add(new EvalRunIn("r1", "s", "gpt-4o", "keyword", 1000, 1, [Big("q", 1.0, "c1", 256)]));

        var runs = _store.Before("s", "keyword", 2000, 10);

        var run = Assert.Single(runs);
        var c = Assert.Single(run.Cases);
        Assert.Equal(("q", "c1", 1.0, 0), (c.Input, c.CaseId, c.Score, c.Idx));
        Assert.DoesNotContain(typeof(EvalStore.GreenCase).GetProperties(), p => p.Name == "Steps");
    }

    [Fact]
    public void Run_with_no_green_case_still_uses_up_a_window_slot()
    {
        for (var i = 0; i < 3; i++)
            _store.Add(new EvalRunIn($"red{i}", "s", "gpt-4o", "keyword", 100 + i, 0.1, [Big("q", 0.1, "c", 1)]));

        var runs = _store.Before("s", "keyword", 1000, 2);

        Assert.Equal(["red2", "red1"], runs.Select(r => r.Id).ToList());
        Assert.All(runs, r => Assert.Empty(r.Cases));
    }
}

public class SmallParseTests
{
    [Theory]
    [InlineData("eval:r1:3", true, "r1", 3)]
    [InlineData("eval:r1:0", true, "r1", 0)]
    [InlineData("eval:r1:+3", false, "", 0)]
    [InlineData("eval:r1: 3", false, "", 0)]
    [InlineData("eval:r1:03", false, "", 0)]
    [InlineData("eval:r1:00", false, "", 0)]
    [InlineData("eval:r1:-1", false, "", 0)]
    [InlineData("eval:r1:3 ", false, "", 0)]
    public void Eval_ref_index_is_plain_digits(string r, bool ok, string run, int idx)
    {
        Assert.Equal(ok, RunRefs.TryParseEval(r, out var id, out var i));
        if (!ok) return;
        Assert.Equal(run, id);
        Assert.Equal(idx, i);
    }

    [Theory]
    [InlineData("eval:r1:+3")]
    [InlineData("eval:r1:03")]
    public void Odd_index_spellings_are_malformed_not_a_trace_id(string r) => Assert.True(RunRefs.IsMalformed(r));

    [Theory]
    [InlineData("--max-cost-increase", "NaN")]
    [InlineData("--max-cost-increase", "Infinity")]
    [InlineData("--max-cost-increase", "-Infinity")]
    [InlineData("--min", "NaN")]
    [InlineData("--tolerance", "Infinity")]
    public void Non_finite_numbers_are_a_usage_error(string flag, string value)
    {
        var o = EvalCommand.Options.Parse(["set", "--baseline", "b", flag, value]);
        Assert.NotNull(o.Error);
        Assert.Contains("number", o.Error);
    }

    [Fact]
    public void Finite_numbers_still_parse()
    {
        var o = EvalCommand.Options.Parse(["set", "--baseline", "b", "--max-cost-increase", "0.25"]);
        Assert.Null(o.Error);
        Assert.Equal(0.25, o.MaxCostIncrease);
    }

    [Fact]
    public void Answer_only_eval_case_has_no_cost()
    {
        var run = new EvalRunDetail(new EvalRunSummary("r", "s", "gpt-4o", "keyword", 1000, 1, 1),
            [new EvalCaseIn("q", "a", 1.0)]);

        var d = EvalCaseTrace.Build(run, 0)!;

        Assert.Null(d.Trace.CostUsd);
        Assert.Null(Assert.Single(d.Spans).CostUsd);
    }
}

public class ClientFixTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-ai-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    static OtlpRequest AiSdkTrace() => JsonSerializer.Deserialize<OtlpRequest>("""
    {"resourceSpans":[{"scopeSpans":[{"spans":[
      {"traceId":"0102030405060708090a0b0c0d0e0f10","spanId":"0000000000000001","name":"invoke_agent support",
       "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000001000000000",
       "attributes":[{"key":"gen_ai.operation.name","value":{"stringValue":"invoke_agent"}},
                     {"key":"gen_ai.usage.input_tokens","value":{"intValue":"1000"}},
                     {"key":"gen_ai.usage.output_tokens","value":{"intValue":"200"}}]},
      {"traceId":"0102030405060708090a0b0c0d0e0f10","spanId":"0000000000000002","parentSpanId":"0000000000000001","name":"step 1",
       "startTimeUnixNano":"1700000000100000000","endTimeUnixNano":"1700000000900000000",
       "attributes":[{"key":"gen_ai.operation.name","value":{"stringValue":"agent_step"}},
                     {"key":"gen_ai.usage.input_tokens","value":{"intValue":"1000"}},
                     {"key":"gen_ai.usage.output_tokens","value":{"intValue":"200"}}]},
      {"traceId":"0102030405060708090a0b0c0d0e0f10","spanId":"0000000000000003","parentSpanId":"0000000000000002","name":"chat gpt-4o",
       "startTimeUnixNano":"1700000000200000000","endTimeUnixNano":"1700000000800000000",
       "attributes":[{"key":"gen_ai.operation.name","value":{"stringValue":"chat"}},
                     {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o"}},
                     {"key":"gen_ai.usage.input_tokens","value":{"intValue":"1000"}},
                     {"key":"gen_ai.usage.output_tokens","value":{"intValue":"200"}}]}
    ]}]}]}
    """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void Ai_sdk_step_span_is_a_container_not_an_llm_call()
    {
        var t = Assert.Single(Otlp.ToTraces(AiSdkTrace()));

        Assert.Equal(["agent", "agent", "llm"], t.Spans.Select(s => s.Kind).ToArray());

        var store = TraceStore.ForFile(_path);
        var sum = store.Add(t);
        Assert.Equal(1000, sum.PromptTokens);   // the step and the agent are left out, not counted three times
        var tot = RunDiff.Compare(store.Get(t.Id)!, store.Get(t.Id)!).TotalsA;
        Assert.Equal(1, tot.Steps);
        Assert.Equal(1000, tot.InputTokens);
        Assert.Equal(["gpt-4o"], tot.Models);
    }

    [Fact]
    public void Structured_system_and_user_messages_render_each_role_once()
    {
        var msgs = JsonSerializer.Serialize(new object[]
        {
            new { role = "system", parts = new[] { new { type = "text", content = "be brief" } } },
            new { role = "user", parts = new[] { new { type = "text", content = "hi" } } },
        });
        var req = JsonSerializer.Deserialize<OtlpRequest>(
            "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"traceId\":\"0102030405060708090a0b0c0d0e0f10\",\"spanId\":\"0000000000000009\",\"name\":\"chat\"," +
            "\"startTimeUnixNano\":\"1700000000000000000\",\"endTimeUnixNano\":\"1700000001000000000\",\"attributes\":[" +
            "{\"key\":\"gen_ai.request.model\",\"value\":{\"stringValue\":\"gpt-4o\"}}," +
            "{\"key\":\"gen_ai.input.messages\",\"value\":{\"stringValue\":" + JsonSerializer.Serialize(msgs) + "}}]}]}]}]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var s = Assert.Single(Assert.Single(Otlp.ToTraces(req)).Spans);

        Assert.Equal("system: be brief\nuser: hi", s.PromptText);
    }

    [Fact]
    public void Cli_percentages_use_one_format()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            foreach (var c in new[] { "en-US", "fr-FR", "" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(c);
                Assert.Equal("100%", EvalCommand.Pct(1.0));
                Assert.Equal("80%", EvalCommand.Pct(0.8));
                Assert.Equal("0%", EvalCommand.Pct(0));
                Assert.Equal("5%", EvalCommand.Pct(0.05));
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
