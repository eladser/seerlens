using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class GoldenSetsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"seerlens-sets-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Save_then_reload_from_disk()
    {
        var sets = new GoldenSets(_dir);
        sets.Save(new GoldenSet("support", [new GoldenCase("c1", "refund policy?", ["30", "days"])]));

        var fresh = new GoldenSets(_dir);
        var loaded = fresh.Get("support");

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Cases);
        Assert.Equal("refund policy?", loaded.Cases[0].Input);
    }

    [Fact]
    public void A_sneaky_name_cannot_escape_the_evals_dir()
    {
        var sets = new GoldenSets(_dir);
        sets.Save(new GoldenSet("../../evil", [new GoldenCase("c1", "q")]));

        // nothing got written outside the dir
        Assert.False(File.Exists(Path.Combine(_dir, "..", "..", "evil.json")));
        Assert.True(Directory.GetFiles(_dir, "*.json").Length == 1);
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        var sets = new GoldenSets(_dir);
        sets.Save(new GoldenSet("temp", [new GoldenCase("c1", "q")]));

        Assert.True(sets.Delete("temp"));
        Assert.Null(new GoldenSets(_dir).Get("temp"));
    }
}

public class SetEndpointTests : IClassFixture<SetEndpointTests.Factory>
{
    readonly Factory _factory;

    public SetEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Put_get_append_delete_round_trip()
    {
        var client = _factory.CreateClient();
        var body = new GoldenSet("billing", [new GoldenCase("c1", "when am I charged?", ["monthly"])]);

        var put = await client.PutAsJsonAsync("/api/sets/billing", body);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var got = await client.GetFromJsonAsync<GoldenSet>("/api/sets/billing");
        Assert.Single(got!.Cases);

        await client.PostAsJsonAsync("/api/sets/billing/cases",
            new GoldenCase("", "what about refunds?", ["30"]));
        var grown = await client.GetFromJsonAsync<GoldenSet>("/api/sets/billing");
        Assert.Equal(2, grown!.Cases.Count);

        var del = await client.DeleteAsync("/api/sets/billing");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), $"seerlens-setapi-{Guid.NewGuid():N}");
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-setapi-{Guid.NewGuid():N}.db");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _db,
                    ["SEERLENS_EVALS_DIR"] = _dir,
                }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_dir, true); } catch { }
            try { File.Delete(_db); } catch { }
        }
    }
}
