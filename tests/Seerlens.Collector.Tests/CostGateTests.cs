using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

[Collection("console")]
public class CostGateTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-gate-{Guid.NewGuid():N}.json");

    public void Dispose() { try { File.Delete(_path); } catch { } }

    // gpt-4o is $2.50 in / $10 out per 1M, so 1M in + 100k out = $3.50
    static EvalRun RunWithTokens(long input, long output) => new("r", "support", "gpt-4o", "keyword", 1700, 1.0,
    [
        new EvalCaseResult("q", "a", 1.0, "c1",
            [new EvalStep("llm", null, "gpt-4o", 1, 5, input, output, null, null, null)]),
    ]);

    static (int Code, string Out) Gate(EvalRun run, params string[] extra)
    {
        var o = EvalCommand.Options.Parse(["support", .. extra]);
        Assert.Null(o.Error);
        var sw = new StringWriter();
        var old = Console.Out;
        Console.SetOut(sw);
        try { return (EvalCommand.Gate(run, o), sw.ToString()); }
        finally { Console.SetOut(old); }
    }

    void SaveBaseline(string json) => File.WriteAllText(_path, json);

    [Fact]
    public void Cost_within_the_allowed_increase_passes()
    {
        SaveBaseline("""{"set":"support","scorer":"keyword","score":1.0,"savedAt":1,"costUsd":3.0}""");

        var (code, text) = Gate(RunWithTokens(1_000_000, 100_000), "--baseline", _path, "--max-cost-increase", "0.25");

        Assert.Equal(0, code);   // 3.50 <= 3.0 * 1.25
        Assert.Contains("cost  $3.5000 vs $3.0000", text);
        Assert.DoesNotContain("COST", text);
    }

    [Fact]
    public void Cost_over_the_allowed_increase_fails()
    {
        SaveBaseline("""{"set":"support","scorer":"keyword","score":1.0,"savedAt":1,"costUsd":2.0}""");

        var (code, text) = Gate(RunWithTokens(1_000_000, 100_000), "--baseline", _path, "--max-cost-increase", "0.25");

        Assert.Equal(1, code);   // 3.50 > 2.5
        Assert.Contains("COST", text);
    }

    [Fact]
    public void Baseline_without_cost_passes_with_a_note()
    {
        SaveBaseline("""{"set":"support","scorer":"keyword","score":1.0,"savedAt":1}""");

        var (code, text) = Gate(RunWithTokens(1_000_000, 100_000), "--baseline", _path, "--max-cost-increase", "0.1");

        Assert.Equal(0, code);
        Assert.Contains("baseline has no cost", text);
    }

    [Fact]
    public void Without_the_flag_cost_is_printed_but_never_fails()
    {
        SaveBaseline("""{"set":"support","scorer":"keyword","score":1.0,"savedAt":1,"costUsd":0.01}""");

        var (code, text) = Gate(RunWithTokens(1_000_000, 100_000), "--baseline", _path);

        Assert.Equal(0, code);
        Assert.Contains("$3.5000 vs $0.0100", text);
    }

    [Fact]
    public void Save_baseline_writes_the_step_cost()
    {
        EvalCommand.Baseline.Write(_path, RunWithTokens(1_000_000, 100_000));

        Assert.Equal(3.5, EvalCommand.Baseline.Read(_path)!.CostUsd!.Value, 6);
        Assert.Contains("\"costUsd\"", File.ReadAllText(_path));
    }

    [Fact]
    public void Run_without_priced_steps_saves_no_cost()
    {
        EvalCommand.Baseline.Write(_path, new EvalRun("r", "s", "gpt-4o", "keyword", 1, 1.0, [new EvalCaseResult("q", "a", 1.0)]));

        Assert.Null(EvalCommand.Baseline.Read(_path)!.CostUsd);
    }

    [Fact]
    public void Old_baseline_file_still_reads()
    {
        SaveBaseline("""{"set":"support","scorer":"keyword","score":0.9,"savedAt":5}""");

        var b = EvalCommand.Baseline.Read(_path);

        Assert.Equal(0.9, b!.Score);
        Assert.Null(b.CostUsd);
    }

    [Fact]
    public void Flag_needs_a_baseline_and_a_non_negative_number()
    {
        Assert.NotNull(EvalCommand.Options.Parse(["s", "--max-cost-increase", "0.2"]).Error);
        Assert.NotNull(EvalCommand.Options.Parse(["s", "--baseline", "b", "--max-cost-increase", "-1"]).Error);
        Assert.Null(EvalCommand.Options.Parse(["s", "--baseline", "b", "--max-cost-increase", "0"]).Error);
    }
}
