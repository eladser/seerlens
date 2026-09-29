using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class BudgetAlertTests : IClassFixture<BudgetAlertTests.Factory>
{
    readonly Factory _factory;

    public BudgetAlertTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Ingest_fires_the_budget_webhook_without_anyone_hitting_the_cost_endpoint()
    {
        using var listener = new HttpListener();
        var port = GetFreePort();
        listener.Prefixes.Add($"http://localhost:{port}/hook/");
        listener.Start();
        var got = listener.GetContextAsync();

        var client = _factory.CreateClient();
        await client.PutAsJsonAsync("/api/budget", new { monthlyUsd = 0.0001 });
        await client.PutAsJsonAsync("/api/alerts", new { webhookUrl = $"http://localhost:{port}/hook/" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var trace = new IngestTrace("over-budget", "chat", now, 100, "openai", "gpt-4o", "ok",
            [new IngestSpan("s", null, "chat", "llm", now, 100, "gpt-4o", 1000, 1000, "hi", "ok", null)]);
        var post = await client.PostAsJsonAsync("/ingest", trace);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var ctx = await got.WaitAsync(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(ctx.Request.InputStream);
        var body = JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.Equal("over_budget", body.RootElement.GetProperty("type").GetString());
        ctx.Response.Close();
    }

    static int GetFreePort()
    {
        using var s = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        s.Start();
        var port = ((IPEndPoint)s.LocalEndpoint).Port;
        s.Stop();
        return port;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-budgethook-{Guid.NewGuid():N}.db");
        readonly string _settings = Path.Combine(Path.GetTempPath(), $"seerlens-budgethook-{Guid.NewGuid():N}.json");
        readonly string _evalsDir = Path.Combine(Path.GetTempPath(), $"seerlens-budgethook-{Guid.NewGuid():N}");

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
