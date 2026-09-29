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
    public void Trace_with_null_spans_does_not_crash()
    {
        var summary = _store.Add(new IngestTrace("t1", "chat", 1000, 50, "openai", "gpt-4o", "ok", null!));

        Assert.Equal("t1", summary.Id);
        Assert.NotNull(_store.Get("t1"));
    }

    [Fact]
    public void A_trace_split_across_batches_accumulates_instead_of_overwriting()
    {
        // BatchSpanProcessor can flush a trace's spans across two separate OTLP exports.
        var batch1 = new IngestTrace("split", "chat: gpt-4o", 1000, 400, "openai", "gpt-4o", "ok",
            [new IngestSpan("s1", null, "chat", "llm", 1000, 400, "gpt-4o", 1000, 500, "hi", "hello", null)]);
        var batch2 = new IngestTrace("split", "chat: gpt-4o", 1000, 600, "openai", "gpt-4o", "ok",
            [new IngestSpan("s2", "s1", "lookupOrder", "tool", 1400, 600, null, null, null, null, null, null)]);

        _store.Add(batch1);
        var summary = _store.Add(batch2);

        Assert.Equal(1000, summary.PromptTokens);
        Assert.Equal(500, summary.CompletionTokens);
        // 1000/1M*2.50 + 500/1M*10 = 0.0075, unchanged since the tool span isn't priced
        Assert.Equal(0.0075, summary.CostUsd!.Value, 6);
        Assert.Equal(2, _store.Get("split")!.Spans.Count);
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
