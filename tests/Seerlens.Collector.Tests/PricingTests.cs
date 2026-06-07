using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class PricingTests
{
    [Fact]
    public void Computes_cost_from_token_counts()
    {
        // 1M in @ $2.50 + 1M out @ $10 = $12.50
        var cost = Pricing.CostFor("gpt-4o", 1_000_000, 1_000_000);
        Assert.Equal(12.50, cost!.Value, 4);
    }

    [Fact]
    public void Unknown_model_has_no_price()
    {
        Assert.Null(Pricing.CostFor("some-local-llama", 1000, 1000));
    }

    [Fact]
    public void Strips_date_suffix_from_model_name()
    {
        var dated = Pricing.CostFor("gpt-4o-2024-08-06", 1000, 1000);
        var plain = Pricing.CostFor("gpt-4o", 1000, 1000);
        Assert.Equal(plain, dated);
    }

    [Theory]
    [InlineData("gpt-5.5")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("gemini-2.5-pro")]
    [InlineData("grok-4.3")]
    public void Current_models_are_priced(string model)
    {
        var cost = Pricing.CostFor(model, 1000, 1000);
        Assert.NotNull(cost);
        Assert.True(cost > 0);
    }

    [Fact]
    public void A_dated_claude_4_id_still_prices()
    {
        var dated = Pricing.CostFor("claude-haiku-4-5-20251001", 1000, 1000);
        var plain = Pricing.CostFor("claude-haiku-4-5", 1000, 1000);
        Assert.NotNull(dated);
        Assert.Equal(plain, dated);
    }
}
