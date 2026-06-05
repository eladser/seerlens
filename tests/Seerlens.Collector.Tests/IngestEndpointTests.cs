using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class IngestEndpointTests : IClassFixture<IngestEndpointTests.Factory>
{
    readonly Factory _factory;

    public IngestEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Posted_trace_shows_up_in_the_list()
    {
        var client = _factory.CreateClient();
        var trace = new IngestTrace(
            "t-http", "chat: gpt-4o-mini", 1700, 312, "openai", "gpt-4o-mini", "ok",
            [new IngestSpan("s1", null, "chat", "llm", 1700, 312, "gpt-4o-mini", 200, 80, "q", "a", null)]);

        var post = await client.PostAsJsonAsync("/ingest", trace);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var list = await client.GetFromJsonAsync<List<TraceSummary>>("/api/traces");
        Assert.Contains(list!, t => t.Id == "t-http");
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-http-{Guid.NewGuid():N}.db");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?> { ["SEERLENS_DB"] = _db }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
        }
    }
}
