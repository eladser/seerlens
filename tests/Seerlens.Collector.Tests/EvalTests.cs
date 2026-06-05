using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class EvalStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-eval-{Guid.NewGuid():N}.db");
    readonly EvalStore _store;

    public EvalStoreTests() => _store = EvalStore.ForFile(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    static EvalRunIn Run(string id, string set, long at, double score) => new(
        id, set, "gpt-4o", "keyword", at, score,
        [new EvalCaseIn("q1", "a1", score), new EvalCaseIn("q2", "a2", score)]);

    [Fact]
    public void Add_then_get_keeps_the_cases()
    {
        _store.Add(Run("r1", "support", 1000, 0.9));

        var detail = _store.Get("r1");

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Cases.Count);
        Assert.Equal(0.9, detail.Run.Score);
        Assert.Equal(2, detail.Run.CaseCount);
    }

    [Fact]
    public void List_is_chronological_for_the_trend()
    {
        _store.Add(Run("new", "support", 2000, 0.7));
        _store.Add(Run("old", "support", 1000, 0.9));

        var ids = _store.List("support").Select(r => r.Id).ToList();

        Assert.Equal(["old", "new"], ids);
    }

    [Fact]
    public void List_filters_by_set()
    {
        _store.Add(Run("a", "support", 1000, 0.9));
        _store.Add(Run("b", "billing", 1000, 0.8));

        Assert.Single(_store.List("support"));
        Assert.Equal(2, _store.List().Count);
    }
}

public class EvalEndpointTests : IClassFixture<EvalEndpointTests.Factory>
{
    readonly Factory _factory;

    public EvalEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Posted_run_shows_up_on_the_trend()
    {
        var client = _factory.CreateClient();
        var run = new EvalRunIn("run-1", "support", "gpt-4o-mini", "keyword", 1700, 0.78,
            [new EvalCaseIn("where is my order", "it shipped", 0.78)]);

        var post = await client.PostAsJsonAsync("/eval/runs", run);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var list = await client.GetFromJsonAsync<List<EvalRunSummary>>("/api/evals?set=support");
        Assert.Contains(list!, r => r.Id == "run-1" && r.Score == 0.78);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-evalhttp-{Guid.NewGuid():N}.db");

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
