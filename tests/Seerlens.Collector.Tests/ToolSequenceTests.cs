using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class ToolSequenceTests
{
    [Fact]
    public void Exact_match_scores_full()
    {
        var r = ToolSequence.Score(["search", "read", "summarize"], ["search", "read", "summarize"]);
        Assert.Equal(1.0, r.Score);
        Assert.True(r.OrderOk);
        Assert.Empty(r.Missing);
    }

    [Fact]
    public void Missing_tool_lowers_score_and_is_listed()
    {
        var r = ToolSequence.Score(["search", "read", "summarize"], ["search", "summarize"]);
        Assert.Equal(2.0 / 3, r.Score, 4);
        Assert.False(r.OrderOk);
        Assert.Contains("read", r.Missing);
    }

    [Fact]
    public void Out_of_order_is_not_full_credit()
    {
        // both expected tools were called, but reversed, so the in-order match is only 1
        var r = ToolSequence.Score(["search", "read"], ["read", "search"]);
        Assert.Equal(0.5, r.Score);
        Assert.False(r.OrderOk);
        Assert.Empty(r.Missing); // present, just out of order
    }

    [Fact]
    public void Extra_actual_tools_dont_hurt_when_expected_are_in_order()
    {
        var r = ToolSequence.Score(["search", "read"], ["search", "noise", "read"]);
        Assert.Equal(1.0, r.Score);
        Assert.True(r.OrderOk);
    }
}
