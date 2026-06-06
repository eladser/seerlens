using Seerlens.Collector;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class EvalCommandTests
{
    [Theory]
    [InlineData(0.79, 0.80, true)]
    [InlineData(0.80, 0.80, false)]
    [InlineData(0.95, 0.80, false)]
    public void Floor_fails_only_when_under(double score, double min, bool fails)
        => Assert.Equal(fails, EvalCommand.BelowFloor(score, min));

    [Theory]
    [InlineData(0.90, 0.84, 0.05, true)]   // dropped 6 points, only 5 allowed
    [InlineData(0.90, 0.86, 0.05, false)]  // dropped 4 points, within tolerance
    [InlineData(0.90, 0.95, 0.05, false)]  // went up
    public void Regression_trips_past_tolerance(double baseline, double score, double tol, bool regressed)
        => Assert.Equal(regressed, EvalCommand.Regressed(baseline, score, tol));

    [Fact]
    public void Baseline_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"seerlens-base-{Guid.NewGuid():N}.json");
        var run = new EvalRun("r1", "support", "gpt-4o", "keyword", 1700, 0.88, []);
        try
        {
            EvalCommand.Baseline.Write(path, run);
            var read = EvalCommand.Baseline.Read(path);

            Assert.NotNull(read);
            Assert.Equal(0.88, read!.Score);
            Assert.Equal("support", read.Set);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Junit_counts_a_failure_per_case_under_the_floor()
    {
        var run = new EvalRun("r1", "support", "gpt-4o", "keyword", 1700, 0.5,
        [
            new EvalCaseResult("q good", "a", 1.0),
            new EvalCaseResult("q bad", "b", 0.0),
        ]);

        var xml = EvalCommand.JUnit(run, 0.8);

        Assert.Contains("tests=\"2\" failures=\"1\"", xml);
        Assert.Contains("<failure", xml);
    }
}
