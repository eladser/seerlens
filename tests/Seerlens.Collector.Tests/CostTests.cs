using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class CostTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-cost-{Guid.NewGuid():N}.db");
    readonly TraceStore _store;

    public CostTests() => _store = TraceStore.ForFile(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    static IngestTrace Trace(string id, string model, long at, long inTok, long outTok) => new(
        id, "chat", at, 100, "openai", model, "ok",
        [new IngestSpan($"{id}-s", null, "chat", "llm", at, 100, model, inTok, outTok, "hi", "there", null)]);

    [Fact]
    public void Spend_rolls_up_month_total_and_by_model()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _store.Add(Trace("a", "gpt-4o", now, 1000, 1000));
        _store.Add(Trace("b", "gpt-4o-mini", now, 1000, 1000));

        var monthStart = now - 1000; // both land inside the window
        var spend = _store.SpendReport(monthStart, monthStart);

        Assert.True(spend.TotalUsd > 0);
        Assert.Equal(spend.TotalUsd, spend.MonthToDateUsd, 6);
        Assert.Equal(2, spend.ByModel.Count);
        Assert.Contains(spend.ByModel, m => m.Model == "gpt-4o");
    }

    [Fact]
    public void Spend_before_the_month_is_excluded_from_month_to_date()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _store.Add(Trace("old", "gpt-4o", now - (long)TimeSpan.FromDays(40).TotalMilliseconds, 1000, 1000));

        var monthStart = now - (long)TimeSpan.FromDays(5).TotalMilliseconds;
        var spend = _store.SpendReport(monthStart, monthStart);

        Assert.True(spend.TotalUsd > 0);
        Assert.Equal(0, spend.MonthToDateUsd, 6);
    }
}
