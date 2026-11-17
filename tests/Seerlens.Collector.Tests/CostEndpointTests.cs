using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Seerlens.Collector.Tests;

public class CostEndpointTests : IClassFixture<CostEndpointTests.Factory>
{
    readonly Factory _factory;

    public CostEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Cost_reports_over_budget_once_spend_passes_the_cap()
    {
        var client = _factory.CreateClient();

        await client.PutAsJsonAsync("/api/budget", new { monthlyUsd = 0.0001 });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var trace = new IngestTrace("c1", "chat", now, 100, "openai", "gpt-4o", "ok",
            [new IngestSpan("s", null, "chat", "llm", now, 100, "gpt-4o", 1000, 1000, "hi", "ok", null)]);
        await client.PostAsJsonAsync("/ingest", trace);

        var report = await client.GetFromJsonAsync<CostReport>("/api/cost");

        Assert.NotNull(report);
        Assert.True(report!.OverBudget);
        Assert.True(report.Spend.MonthToDateUsd > 0);
    }

    [Fact]
    public async Task Daily_spend_covers_exactly_14_days_ending_today()
    {
        var client = _factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        // one call on each of the last 14 days, plus one just inside now - 14 days, on the 15th day back
        var times = Enumerable.Range(0, 14).Select(d => today.AddDays(-d).AddSeconds(1))
            .Append(now.AddDays(-14).AddSeconds(1));
        var i = 0;
        foreach (var t in times)
        {
            var ms = t.ToUnixTimeMilliseconds();
            await client.PostAsJsonAsync("/ingest", new IngestTrace($"day{i}", "chat", ms, 100, "openai", "gpt-4o", "ok",
                [new IngestSpan($"day{i++}s", null, "chat", "llm", ms, 100, "gpt-4o", 1000, 1000, "hi", "ok", null)]));
        }

        var report = await client.GetFromJsonAsync<CostReport>("/api/cost");

        var days = report!.Spend.Daily.Select(d => d.Date).ToList();
        Assert.Equal(14, days.Count);
        Assert.Equal(today.AddDays(-13).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), days[0]);
        Assert.Equal(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), days[^1]);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-costapi-{Guid.NewGuid():N}.db");
        readonly string _settings = Path.Combine(Path.GetTempPath(), $"seerlens-costapi-{Guid.NewGuid():N}.json");
        readonly string _evalsDir = Path.Combine(Path.GetTempPath(), $"seerlens-costapi-{Guid.NewGuid():N}");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _db,
                    ["SEERLENS_SETTINGS"] = _settings,
                    ["SEERLENS_EVALS_DIR"] = _evalsDir,
                }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
            try { File.Delete(_settings); } catch { }
            try { Directory.Delete(_evalsDir, true); } catch { }
        }
    }
}
