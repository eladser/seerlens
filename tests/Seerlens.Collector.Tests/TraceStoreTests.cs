using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class TraceStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-test-{Guid.NewGuid():N}.db");
    readonly TraceStore _store;

    public TraceStoreTests() => _store = TraceStore.ForFile(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    static IngestTrace Sample(string id, long startedAt, string model = "gpt-4o") => new(
        id, $"chat: {model}", startedAt, 820, "openai", model, "ok",
        [
            new IngestSpan("s1", null, "chat", "llm", startedAt, 820, model, 1000, 500,
                "hi there", "hello back", null),
            new IngestSpan("s2", "s1", "lookupOrder", "tool", startedAt + 100, 40, null, null, null,
                null, null, null),
        ]);

    [Fact]
    public void Add_then_get_returns_spans_and_priced_cost()
    {
        _store.Add(Sample("t1", 1000));

        var detail = _store.Get("t1");

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Spans.Count);
        Assert.Equal(1000, detail.Trace.PromptTokens);
        Assert.Equal(500, detail.Trace.CompletionTokens);
        // 1000/1M*2.50 + 500/1M*10 = 0.0025 + 0.005
        Assert.Equal(0.0075, detail.Trace.CostUsd!.Value, 6);
    }

    [Fact]
    public void Tool_spans_are_not_priced()
    {
        _store.Add(Sample("t1", 1000));
        var tool = _store.Get("t1")!.Spans.Single(s => s.Kind == "tool");
        Assert.Null(tool.CostUsd);
    }

    [Fact]
    public void List_is_newest_first()
    {
        _store.Add(Sample("old", 1000));
        _store.Add(Sample("new", 2000));

        var ids = _store.List().Select(t => t.Id).ToList();

        Assert.Equal(["new", "old"], ids);
    }

    [Fact]
    public void Get_missing_trace_returns_null()
    {
        Assert.Null(_store.Get("nope"));
    }

    [Fact]
    public void Stats_aggregates_traces()
    {
        _store.Add(Sample("t1", 1000));
        _store.Add(Sample("t2", 2000));

        var stats = _store.Stats();

        Assert.Equal(2, stats.Traces);
        Assert.Equal(0.015, stats.TotalCostUsd, 6);
        Assert.Equal(820, stats.AvgDurationMs, 1);
    }
}
