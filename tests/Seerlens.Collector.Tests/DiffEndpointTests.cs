using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class DiffEndpointTests : IClassFixture<DiffEndpointTests.SeededFactory>
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    readonly HttpClient _c;

    public DiffEndpointTests(SeededFactory f) => _c = f.CreateClient();

    public sealed class SeededFactory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-diff-{Guid.NewGuid():N}.db");
        readonly string _evalsDir = Path.Combine(Path.GetTempPath(), $"seerlens-diff-{Guid.NewGuid():N}");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _db,
                    ["SEERLENS_EVALS_DIR"] = _evalsDir,
                }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
            try { Directory.Delete(_evalsDir, true); } catch { }
        }

        public SeededFactory()
        {
            var c = CreateClient();
            var v1 = new IngestTrace("d-1", "agent", 1000, 500, "openai", "gpt-4o", "ok",
            [
                new IngestSpan("a1", null, "chat", "llm", 1000, 200, "gpt-4o", 100, 50, "system: be kind\nuser: hi", "hello", null),
                new IngestSpan("a2", null, "search", "tool", 1200, 50, null, null, null, "{\"q\":1}", "ok", null),
                new IngestSpan("a3", null, "chat", "llm", 1300, 200, "gpt-4o", 150, 60, "tool said ok", "done", null),
            ]);
            var v2 = new IngestTrace("d-2", "agent", 9000, 300, "openai", "gpt-4o", "ok",
            [
                new IngestSpan("b1", null, "chat", "llm", 9000, 200, "gpt-4o", 100, 50, "system: be terse\nuser: hi", "hello", null),
                new IngestSpan("b3", null, "chat", "llm", 9300, 100, "gpt-4o", 150, 60, "tool said ok", "done", null),
            ]);
            c.PostAsJsonAsync("/ingest", v1).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            c.PostAsJsonAsync("/ingest", v2).GetAwaiter().GetResult().EnsureSuccessStatusCode();

            EvalStep[] st = [new EvalStep("llm", null, "gpt-4o", 10, 5, 100, 20, null, null, null)];
            foreach (var (id, ans) in new[] { ("er-1", "good"), ("er-2", "bad") })
                c.PostAsJsonAsync("/eval/runs",
                    new EvalRunIn(id, "s", "gpt-4o", "keyword", 100, 0.5, [new EvalCaseIn("q", ans, 0.5, "c1", st)]))
                    .GetAwaiter().GetResult().EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Diff_returns_alignment_with_the_first_changed_row_inlined()
    {
        var res = await _c.GetAsync("/api/diff?a=d-1&b=d-2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var d = await res.Content.ReadFromJsonAsync<DiffResponse>(Web);

        Assert.Equal(["changed", "removed", "same"], d!.Steps.Select(s => s.Status));
        Assert.Equal(["search"], d.ToolsA);
        Assert.NotNull(d.Steps[0].Input);
        Assert.Null(d.Steps[1].Input);
        Assert.Contains(d.Steps[0].Input!.Rows, r => r.T is "mod" or "del");
        Assert.Equal(3, d.TotalsA.Steps);
    }

    [Fact]
    public async Task Json_shape_is_camel_case()
    {
        var json = await _c.GetStringAsync("/api/diff?a=d-1&b=d-2");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var k in new[] { "a", "b", "totalsA", "totalsB", "toolsA", "toolsB", "truncated", "steps" })
            Assert.True(root.TryGetProperty(k, out _), k);
        var step = root.GetProperty("steps")[0];
        foreach (var k in new[] { "status", "kind", "name", "a", "b", "changes", "params", "input", "output" })
            Assert.True(step.TryGetProperty(k, out _), k);
        Assert.True(step.GetProperty("a").TryGetProperty("spanId", out _));
    }

    [Fact]
    public async Task Payload_only_inlines_one_text_diff()
    {
        var d = await _c.GetFromJsonAsync<DiffResponse>("/api/diff?a=d-1&b=d-2", Web);
        Assert.Equal(1, d!.Steps.Count(s => s.Input is not null));
    }

    [Fact]
    public async Task Diff_accepts_eval_refs()
    {
        var res = await _c.GetAsync("/api/diff?a=eval:er-1:0&b=eval:er-2:0");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var d = await res.Content.ReadFromJsonAsync<DiffResponse>(Web);
        Assert.Equal("changed", Assert.Single(d!.Steps).Status);
    }

    [Theory]
    [InlineData("/api/diff")]
    [InlineData("/api/diff?a=d-1")]
    [InlineData("/api/diff?a=d-1&b=d-1")]
    [InlineData("/api/diff?a=d-1&b=eval:oops")]
    [InlineData("/api/diff?a=eval:er-1:x&b=d-1")]
    [InlineData("/api/diff/step?a=d-1&b=d-2")]
    [InlineData("/api/diff/step?sa=a1")]
    public async Task Bad_requests_are_400(string url)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/diff?a=nope&b=d-2", "a")]
    [InlineData("/api/diff?a=d-1&b=nope", "b")]
    [InlineData("/api/diff?a=d-1&b=eval:er-1:9", "b")]
    [InlineData("/api/diff?a=eval:gone:0&b=d-1", "a")]
    public async Task Unknown_refs_are_404_and_say_which_side(string url, string side)
    {
        var res = await _c.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(side, doc.RootElement.GetProperty("missing").GetString());
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Step_endpoint_diffs_a_pair_on_demand()
    {
        var res = await _c.GetFromJsonAsync<StepDiffResponse>("/api/diff/step?a=d-1&b=d-2&sa=a1&sb=b1", Web);
        Assert.Contains(res!.Input.Rows, r => r.T != "eq");
        Assert.All(res.Output.Rows, r => Assert.Equal("eq", r.T));
    }

    [Fact]
    public async Task Step_endpoint_returns_raw_text_for_one_side()
    {
        var onlyA = await _c.GetFromJsonAsync<StepRawResponse>("/api/diff/step?a=d-1&b=d-2&sa=a2", Web);
        Assert.Equal("{\"q\":1}", onlyA!.Input);
        Assert.Equal("ok", onlyA.Output);

        var onlyB = await _c.GetFromJsonAsync<StepRawResponse>("/api/diff/step?a=d-1&b=d-2&sb=b3", Web);
        Assert.Equal("done", onlyB!.Output);
    }

    [Theory]
    [InlineData("/api/diff/step?a=d-1&b=d-2&sa=zzz&sb=b1", "sa")]
    [InlineData("/api/diff/step?a=d-1&b=d-2&sa=a1&sb=zzz", "sb")]
    [InlineData("/api/diff/step?a=nope&b=d-2&sa=a1", "a")]
    public async Task Step_endpoint_404s_name_the_missing_part(string url, string missing)
    {
        var res = await _c.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(missing, doc.RootElement.GetProperty("missing").GetString());
    }
}
