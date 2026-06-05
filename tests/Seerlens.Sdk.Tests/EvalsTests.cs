using Seerlens.Evals;

namespace Seerlens.Sdk.Tests;

public class EvalsTests
{
    [Fact]
    public async Task Keyword_scorer_is_the_fraction_of_keywords_present()
    {
        var s = new KeywordScorer();
        var c = new GoldenCase("o", "where is my order", ["shipped", "thursday"]);

        Assert.Equal(1.0, await s.Score(c, "It shipped and arrives Thursday"));
        Assert.Equal(0.5, await s.Score(c, "It shipped yesterday"), 3);
        Assert.Equal(0.0, await s.Score(c, "I have no idea"));
    }

    [Fact]
    public async Task Runner_scores_each_case_and_averages_them()
    {
        var target = new FakeChatClient("Your order shipped and arrives Thursday", "gpt-4o", 10, 8);
        var set = new GoldenSet("support",
        [
            new GoldenCase("o", "where is my order", ["shipped", "thursday"]),
            new GoldenCase("r", "what is the refund window", ["refund"]),
        ]);

        var run = await new EvalRunner(target, new KeywordScorer()).Run(set, "gpt-4o");

        Assert.Equal("support", run.Set);
        Assert.Equal("gpt-4o", run.Target);
        Assert.Equal(2, run.Cases.Count);
        // first case matches both keywords (1.0), second matches none (0.0) -> mean 0.5
        Assert.Equal(0.5, run.Score, 3);
    }
}
