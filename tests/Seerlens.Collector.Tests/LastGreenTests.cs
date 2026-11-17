using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class LastGreenTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-green-{Guid.NewGuid():N}.db");
    readonly EvalStore _store;

    public LastGreenTests() => _store = EvalStore.ForFile(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    static EvalCaseIn C(string input, double score, string? id = null) => new(input, "a", score, id);

    void Add(string id, long at, string scorer, string set, params EvalCaseIn[] cases) =>
        _store.Add(new EvalRunIn(id, set, "gpt-4o", scorer, at, cases.Average(c => c.Score), cases.ToList()));

    EvalCaseOut Case(string runId, int idx = 0) => _store.GetWithBaselines(runId)!.Cases[idx];

    [Fact]
    public void Failing_case_points_at_the_newest_earlier_green()
    {
        Add("r1", 1000, "keyword", "s", C("q", 1.0, "c1"));
        Add("r2", 2000, "keyword", "s", C("q", 0.9, "c1"));
        Add("r3", 3000, "keyword", "s", C("q", 0.2, "c1"));

        var b = Case("r3").Baseline;

        Assert.NotNull(b);
        Assert.Equal("r2", b!.RunId);
        Assert.Equal(0, b.CaseIdx);
        Assert.Equal(0.9, b.Score);
        Assert.Equal(2000, b.CreatedAt);
    }

    [Fact]
    public void Uses_the_case_score_not_the_run_mean()
    {
        // r1's mean is 0.5 but its case is green; r2's mean is 0.9 but the matching case isn't
        Add("r1", 1000, "keyword", "s", C("q", 0.8, "c1"), C("other", 0.2, "c2"));
        Add("r2", 2000, "keyword", "s", C("q", 0.7, "c1"), C("other2", 1.0, "c3"), C("o3", 1.0, "c4"));
        Add("r3", 3000, "keyword", "s", C("q", 0.1, "c1"));

        Assert.Equal("r1", Case("r3").Baseline!.RunId);
    }

    [Fact]
    public void Green_cases_get_no_baseline()
    {
        Add("r1", 1000, "keyword", "s", C("q", 1.0, "c1"));
        Add("r2", 2000, "keyword", "s", C("q", 0.8, "c1"));

        Assert.Null(Case("r2").Baseline);
    }

    [Fact]
    public void Only_same_set_and_scorer_count()
    {
        Add("judge", 1000, "llm-judge", "s", C("q", 1.0, "c1"));
        Add("other-set", 1500, "keyword", "t", C("q", 1.0, "c1"));
        Add("r2", 2000, "keyword", "s", C("q", 0.1, "c1"));

        Assert.Null(Case("r2").Baseline);
    }

    [Fact]
    public void Later_runs_are_not_baselines()
    {
        Add("r1", 1000, "keyword", "s", C("q", 0.1, "c1"));
        Add("r2", 2000, "keyword", "s", C("q", 1.0, "c1"));

        Assert.Null(Case("r1").Baseline);
    }

    [Fact]
    public void Id_with_equal_input_is_preferred_over_an_earlier_input_only_match()
    {
        Add("r1", 1000, "keyword", "s", C("dup", 1.0, "x"), C("dup", 1.0, "c2"));
        Add("r2", 2000, "keyword", "s", C("dup", 0.1, "c2"));

        Assert.Equal(1, Case("r2").Baseline!.CaseIdx);
    }

    [Fact]
    public void Renamed_case_falls_back_to_the_exact_input()
    {
        Add("r1", 1000, "keyword", "s", C("a", 0.0), C("q", 1.0, "old-id"));
        Add("r2", 2000, "keyword", "s", C("q", 0.1, "new-id"));

        Assert.Equal(1, Case("r2").Baseline!.CaseIdx);
    }

    [Fact]
    public void Same_id_with_an_edited_input_does_not_match()
    {
        Add("r1", 1000, "keyword", "s", C("old wording", 1.0, "c1"));
        Add("r2", 2000, "keyword", "s", C("new wording", 0.1, "c1"));

        Assert.Null(Case("r2").Baseline);
    }

    [Fact]
    public void A_red_match_in_a_run_does_not_hide_an_older_green()
    {
        Add("r1", 1000, "keyword", "s", C("q", 1.0, "c1"));
        Add("r2", 2000, "keyword", "s", C("q", 0.3, "c1"));
        Add("r3", 3000, "keyword", "s", C("q", 0.2, "c1"));

        Assert.Equal("r1", Case("r3").Baseline!.RunId);
    }

    [Fact]
    public void Target_model_is_ignored()
    {
        _store.Add(new EvalRunIn("r1", "s", "gpt-4o", "keyword", 1000, 1, [C("q", 1.0)]));
        _store.Add(new EvalRunIn("r2", "s", "cheap-mini", "keyword", 2000, 0.1, [C("q", 0.1)]));

        Assert.Equal("r1", Case("r2").Baseline!.RunId);
    }

    [Fact]
    public void Looks_back_at_most_50_runs()
    {
        Add("green", 1, "keyword", "s", C("q", 1.0));
        for (var i = 0; i < 50; i++)
            Add($"red{i}", 100 + i, "keyword", "s", C("q", 0.1));
        Add("now", 1000, "keyword", "s", C("q", 0.1));

        Assert.Null(Case("now").Baseline);   // 50 red runs sit between

        Add("green2", 500, "keyword", "s", C("q", 1.0));   // newer than the reds, inside the window
        Assert.Equal("green2", Case("now").Baseline!.RunId);
    }

    [Fact]
    public void Before_is_newest_first_and_excludes_the_run_itself()
    {
        Add("r1", 1000, "keyword", "s", C("q", 1));
        Add("r2", 2000, "keyword", "s", C("q", 1));
        Add("r3", 3000, "keyword", "s", C("q", 1));

        var ids = _store.Before("s", "keyword", 3000, 10).Select(r => r.Id).ToList();

        Assert.Equal(["r2", "r1"], ids);
        Assert.Single(_store.Before("s", "keyword", 3000, 1));
    }

    [Fact]
    public void Old_cases_json_without_steps_or_ids_still_reads_and_matches()
    {
        var old = """[{"input":"q","answer":"a","score":1.0}]""";
        using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            db.Open();
            var cmd = db.CreateCommand();
            cmd.CommandText = "insert into eval_runs values ('old','s','gpt-4o','keyword',1000,1.0,$c)";
            cmd.Parameters.AddWithValue("$c", old);
            cmd.ExecuteNonQuery();
        }
        Add("r2", 2000, "keyword", "s", C("q", 0.1, "c1"));

        var oldCase = Case("old");
        Assert.Null(oldCase.Steps);
        Assert.Null(oldCase.CaseId);
        Assert.Equal("old", Case("r2").Baseline!.RunId);
    }

    [Fact]
    public void Steps_survive_the_store()
    {
        var step = new EvalStep("llm", null, "gpt-4o", 1700, 12.5, 100, 20, 80, 5, null);
        _store.Add(new EvalRunIn("r1", "s", "gpt-4o", "keyword", 1000, 1,
            [new EvalCaseIn("q", "a", 1, "c1", [step])]));

        Assert.Equal(step, Assert.Single(Case("r1").Steps!));
    }
}

public class EvalBaselineEndpointTests : IClassFixture<EvalEndpointTests.Factory>
{
    readonly EvalEndpointTests.Factory _factory;
    public EvalBaselineEndpointTests(EvalEndpointTests.Factory f) => _factory = f;

    [Fact]
    public async Task Api_returns_steps_and_baseline_per_case()
    {
        var http = _factory.CreateClient();
        var step = new EvalStep("tool", "search", null, 1700, 0, null, null, null, null, """{"q":"x"}""");
        await http.PostAsJsonAsync("/eval/runs", new EvalRunIn("g1", "api-set", "m", "keyword", 1000, 1,
            [new EvalCaseIn("q", "a", 1.0, "c1")]));
        await http.PostAsJsonAsync("/eval/runs", new EvalRunIn("g2", "api-set", "m", "keyword", 2000, 0.2,
            [new EvalCaseIn("q", "bad", 0.2, "c1", [step])]));

        var json = await http.GetFromJsonAsync<JsonElement>("/api/evals/g2");
        var c = json.GetProperty("cases")[0];

        Assert.Equal("c1", c.GetProperty("caseId").GetString());
        Assert.Equal("search", c.GetProperty("steps")[0].GetProperty("name").GetString());
        Assert.Equal("""{"q":"x"}""", c.GetProperty("steps")[0].GetProperty("args").GetString());
        var b = c.GetProperty("baseline");
        Assert.Equal("g1", b.GetProperty("runId").GetString());
        Assert.Equal(0, b.GetProperty("caseIdx").GetInt32());
        Assert.Equal(1.0, b.GetProperty("score").GetDouble());
        Assert.Equal(1000, b.GetProperty("createdAt").GetInt64());
        var first = await http.GetFromJsonAsync<JsonElement>("/api/evals/g1");
        Assert.Equal(JsonValueKind.Null, first.GetProperty("cases")[0].GetProperty("baseline").ValueKind);
    }
}
