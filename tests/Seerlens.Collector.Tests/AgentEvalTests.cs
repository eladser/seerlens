using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Seerlens.Collector.Tests;

public class AgentEvalTests : IClassFixture<AgentEvalTests.Factory>
{
    readonly Factory _factory;

    public AgentEvalTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Scores_a_traces_tool_sequence_against_expected()
    {
        var client = _factory.CreateClient();
        var trace = new IngestTrace("agent-1", "research agent", 1000, 400, "openai", "gpt-4o", "ok",
        [
            new IngestSpan("s1", null, "search_docs", "mcp", 1000, 120, null, null, null, "{}", "hits", null),
            new IngestSpan("s2", null, "read_file", "mcp", 1130, 90, null, null, null, "{}", "text", null),
        ]);
        await client.PostAsJsonAsync("/ingest", trace);

        var resp = await client.PostAsJsonAsync("/eval/tools",
            new { traceId = "agent-1", expected = new[] { "search_docs", "read_file" } });
        var result = await resp.Content.ReadFromJsonAsync<ToolScoreDto>();

        Assert.NotNull(result);
        Assert.Equal(1.0, result!.Score);
        Assert.True(result.OrderOk);
    }

    record ToolScoreDto(double Score, bool OrderOk, string[] Missing);

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-agent-{Guid.NewGuid():N}.db");

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
