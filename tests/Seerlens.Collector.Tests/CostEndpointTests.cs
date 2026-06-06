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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-costapi-{Guid.NewGuid():N}.db");
        readonly string _settings = Path.Combine(Path.GetTempPath(), $"seerlens-costapi-{Guid.NewGuid():N}.json");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _db,
                    ["SEERLENS_SETTINGS"] = _settings,
                }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
            try { File.Delete(_settings); } catch { }
        }
    }
}
