using Seerlens.Collector;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class RunRefsTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-refs-{Guid.NewGuid():N}.db");
    readonly TraceStore _traces;
    readonly EvalStore _evals;

    public RunRefsTests()
    {
        _traces = TraceStore.ForFile(_path);
        _evals = EvalStore.ForFile(_path);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    static EvalRunIn Run(string id, params EvalCaseIn[] cases) => new(id, "support", "gpt-4o", "keyword", 5000, 0.9, cases.ToList());

    [Fact]
    public void Trace_ids_go_to_the_trace_store()
    {
        _traces.Add(new IngestTrace("t1", "chat", 1000, 100, "openai", "gpt-4o", "ok",
            [new IngestSpan("s1", null, "chat", "llm", 1000, 100, "gpt-4o", 10, 5, "q", "a", null)]));

        Assert.Equal("t1", RunRefs.Resolve("t1", _traces, _evals)!.Trace.Id);
        Assert.Null(RunRefs.Resolve("nope", _traces, _evals));
    }

    [Fact]
    public void Eval_ref_builds_from_steps()
    {
        var steps = new[]
        {
            new EvalStep("llm", "chat", "gpt-4o", 1000, 300, 1000, 200, 400, 50, null),
            new EvalStep("tool", "lookup", null, 1300, 20, null, null, null, null, """{"id":1}"""),
            new EvalStep("llm", "chat", null, 1320, 200, 1500, 100, null, null, null),
        };
        _evals.Add(Run("r1", new EvalCaseIn("what is the status of my order?", "shipped", 1.0, "order-1", steps)));

        var d = RunRefs.Resolve("eval:r1:0", _traces, _evals)!;

        Assert.Equal("eval:r1:0", d.Trace.Id);
        Assert.Equal("support: order-1", d.Trace.Name);
        Assert.Equal(1000, d.Trace.StartedAt);
        Assert.Equal("gpt-4o", d.Trace.Model);
        Assert.Equal("ok", d.Trace.Status);
        Assert.Equal(["s0", "s1", "s2"], d.Spans.Select(s => s.Id));
        Assert.Equal("user: what is the status of my order?", d.Spans[0].PromptText);
        Assert.Null(d.Spans[2].PromptText);
        Assert.Equal("shipped", d.Spans[2].CompletionText);
        Assert.Equal("""{"id":1}""", d.Spans[1].PromptText);
        Assert.Equal("tool", d.Spans[1].Kind);
        Assert.Equal(400, d.Spans[0].CacheReadTokens);
        Assert.Equal(50, d.Spans[0].ReasoningTokens);
        // a step with no model is priced at the run target
        Assert.Equal("gpt-4o", d.Spans[2].Model);
        Assert.Equal(Pricing.CostFor("gpt-4o", new TokenUsage(1500, 100)), d.Spans[2].CostUsd);
        Assert.Equal(1500 + 1000, d.Trace.PromptTokens);
    }

    [Fact]
    public void Case_without_steps_is_answer_only()
    {
        _evals.Add(Run("old", new EvalCaseIn("a rather long question " + new string('x', 100), "the answer", 0.5)));

        var d = RunRefs.Resolve("eval:old:0", _traces, _evals)!;

        var s = Assert.Single(d.Spans);
        Assert.Equal("llm", s.Kind);
        Assert.StartsWith("user: a rather", s.PromptText);
        Assert.Equal("the answer", s.CompletionText);
        Assert.Equal(5000, d.Trace.StartedAt);
        Assert.Equal("gpt-4o", d.Trace.Model);
        Assert.Equal(60, d.Trace.Name.Length - "support: ".Length);
    }

    [Fact]
    public void Error_answers_mark_the_trace_as_error()
    {
        _evals.Add(Run("r", new EvalCaseIn("q", "error: provider down", 0.0, "c1",
            [new EvalStep("llm", null, null, 10, 5, null, null, null, null, null)])));

        var d = RunRefs.Resolve("eval:r:0", _traces, _evals)!;

        Assert.Equal("error", d.Trace.Status);
        Assert.Equal("error: provider down", d.Spans[0].Error);
    }

    [Fact]
    public void Old_and_new_runs_diff_with_old_as_answer_only()
    {
        _evals.Add(Run("old", new EvalCaseIn("q", "old answer", 0.9)));
        _evals.Add(Run("new", new EvalCaseIn("q", "new answer", 0.2, "c1",
            [new EvalStep("llm", null, "gpt-4o", 10, 5, 100, 20, null, null, null),
             new EvalStep("tool", "lookup", null, 20, 5, null, null, null, null, "{}")])));

        var d = RunDiff.Compare(RunRefs.Resolve("eval:old:0", _traces, _evals)!, RunRefs.Resolve("eval:new:0", _traces, _evals)!);

        Assert.Equal(["changed", "added"], d.Steps.Select(s => s.Status));
        Assert.Contains("output", d.Steps[0].Changes);
    }

    [Fact]
    public void Bad_refs()
    {
        _evals.Add(Run("r", new EvalCaseIn("q", "a", 1)));

        Assert.Null(RunRefs.Resolve("eval:r:5", _traces, _evals));
        Assert.Null(RunRefs.Resolve("eval:missing:0", _traces, _evals));
        Assert.True(RunRefs.IsMalformed("eval:r"));
        Assert.True(RunRefs.IsMalformed("eval:r:x"));
        Assert.True(RunRefs.IsMalformed("eval::0"));
        Assert.True(RunRefs.IsMalformed("eval:r:-1"));
        Assert.False(RunRefs.IsMalformed("eval:r:5"));
        Assert.False(RunRefs.IsMalformed("some-trace-id"));
    }
}
