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

    [Theory]
    [InlineData("claude-opus-5-5", 24.00)]           // $4 in + $20 out
    [InlineData("gpt-5.4-mini", 5.25)]               // $0.75 in + $4.50 out
    [InlineData("claude-opus-4-5-20251101", 30.00)]  // dated id, priced as claude-opus-4-5
    public void Newer_models_price_per_million(string model, double expected)
    {
        var cost = Pricing.CostFor(model, 1_000_000, 1_000_000);
        Assert.Equal(expected, cost!.Value, 4);
    }

    [Fact]
    public void A_dated_claude_4_id_still_prices()
    {
        var dated = Pricing.CostFor("claude-haiku-4-5-20251001", 1000, 1000);
        var plain = Pricing.CostFor("claude-haiku-4-5", 1000, 1000);
        Assert.NotNull(dated);
        Assert.Equal(plain, dated);
    }

    // 100k out tokens each time; the input count picks the tier for the whole call
    [Theory]
    [InlineData("claude-haiku-5-5", 100_000, 0.06)]       // up to 100k: $0.10 in, $0.50 out
    [InlineData("claude-haiku-5-5", 100_001, 0.3000005)]  // over: $0.50 in, $2.50 out
    [InlineData("gpt-6.1-sol", 272_000, 1.544)]           // <= 272K is short context: $2 in, $10 out
    [InlineData("gpt-6.1-sol", 272_001, 2.588004)]        // long context: $4 in, $15 out
    public void Tiered_models_price_by_input_tokens(string model, long inTokens, double expected)
    {
        var cost = Pricing.CostFor(model, inTokens, 100_000);
        Assert.Equal(expected, cost!.Value, 6);
    }

    [Fact]
    public void A_flat_model_keeps_one_rate_for_long_prompts()
    {
        // 300k in @ $0.75 + 100k out @ $4.50
        var cost = Pricing.CostFor("gpt-5.4-mini", 300_000, 100_000);
        Assert.Equal(0.675, cost!.Value, 6);
    }

    [Fact]
    public void Pricing_file_entries_take_optional_tier_fields()
    {
        var rates = Pricing.ParseOverrides("""
            {
              "flat-model": { "in": 1.0, "out": 2.0 },
              "tiered-model": { "in": 1.0, "out": 2.0, "above": 1000, "inAbove": 3.0, "outAbove": 4.0 }
            }
            """)!;

        Assert.Null(rates["flat-model"].Above);
        Assert.Equal(3.0, Pricing.CostAt(rates["flat-model"], new TokenUsage(1_000_000, 1_000_000)), 6);
        Assert.Equal(2.001, Pricing.CostAt(rates["tiered-model"], new TokenUsage(1000, 1_000_000)), 6);
        Assert.Equal(4.003003, Pricing.CostAt(rates["tiered-model"], new TokenUsage(1001, 1_000_000)), 6);
    }

    // the 1.6.0 formula, kept here so the sweep compares against the real old behavior
    static double OldCost(Pricing.Rate rate, long? i, long? o)
    {
        var (pin, pout) = i > rate.Above
            ? (rate.InAbove ?? rate.In, rate.OutAbove ?? rate.Out)
            : (rate.In, rate.Out);
        return (i ?? 0) / 1_000_000.0 * pin + (o ?? 0) / 1_000_000.0 * pout;
    }

    [Fact]
    public void Null_extras_cost_exactly_what_they_did_before_for_every_model()
    {
        long?[] ins = [null, 0, 1, 999, 100_000, 100_001, 200_000, 200_001, 272_000, 272_001, 1_234_567];
        long?[] outs = [null, 0, 1, 777, 100_000, 5_000_000];
        foreach (var (model, rate) in Pricing.Rates)
            foreach (var i in ins)
                foreach (var o in outs)
                {
                    Assert.Equal(OldCost(rate, i, o), Pricing.CostAt(rate, new TokenUsage(i, o)));
                    Assert.Equal(OldCost(rate, i, o), Pricing.CostFor(model, i, o));
                }
    }

    static Pricing.Rate Flat => new(2.00, 10.00, CachedIn: 0.20, CacheWrite: 2.50);

    [Fact]
    public void Inclusive_input_charges_cached_tokens_once_at_the_cache_price()
    {
        // 1M in incl. 600k read + 100k written: 300k fresh @2 + 600k @0.20 + 100k @2.50
        var c = Pricing.CostAt(Flat, new TokenUsage(1_000_000, 0, 600_000, 100_000));
        Assert.Equal(0.6 + 0.12 + 0.25, c, 9);
    }

    [Fact]
    public void Exclusive_counts_are_detected_and_input_is_not_reduced()
    {
        // read + write (700k) exceed input (200k), so input leaves cache out
        var c = Pricing.CostAt(Flat, new TokenUsage(200_000, 0, 600_000, 100_000));
        Assert.Equal(0.4 + 0.12 + 0.25, c, 9);
    }

    [Fact]
    public void Cache_read_with_no_input_count_still_prices()
    {
        var c = Pricing.CostAt(Flat, new TokenUsage(null, 0, 500_000));
        Assert.Equal(0.10, c, 9);
    }

    [Fact]
    public void Reasoning_inside_output_is_not_added()
    {
        var c = Pricing.CostAt(Flat, new TokenUsage(0, 1_000, null, null, 400));
        Assert.Equal(1_000 / 1_000_000.0 * 10.0, c, 12);
    }

    [Fact]
    public void Reasoning_larger_than_output_is_billed_on_top()
    {
        var c = Pricing.CostAt(Flat, new TokenUsage(0, 200, null, null, 800));
        Assert.Equal(1_000 / 1_000_000.0 * 10.0, c, 12);
    }

    [Fact]
    public void Missing_cache_prices_fall_back_to_the_input_price()
    {
        var plain = new Pricing.Rate(2.00, 10.00);
        var c = Pricing.CostAt(plain, new TokenUsage(1_000_000, 0, 600_000, 100_000));
        Assert.Equal(2.0, c, 9);
    }

    [Fact]
    public void The_tier_check_counts_cached_tokens_and_scales_cache_prices()
    {
        // haiku 5.5: 100k tier, cached 0.01 -> 0.05, write 0.125 -> 0.625 above it
        var under = Pricing.CostFor("claude-haiku-5-5", new TokenUsage(100_000, 0, 100_000));
        Assert.Equal(100_000 / 1e6 * 0.01, under!.Value, 9);

        var over = Pricing.CostFor("claude-haiku-5-5", new TokenUsage(100_001, 0, 100_001));
        Assert.Equal(100_001 / 1e6 * 0.05, over!.Value, 9);

        var write = Pricing.CostFor("claude-haiku-5-5", new TokenUsage(150_000, 0, 0, 150_000));
        Assert.Equal(150_000 / 1e6 * 0.625, write!.Value, 9);
    }

    [Fact]
    public void Exclusive_prompt_size_includes_cache_for_the_tier()
    {
        // 50k fresh + 80k cached = 130k prompt, over haiku's 100k line
        var c = Pricing.CostFor("claude-haiku-5-5", new TokenUsage(50_000, 0, 80_000));
        Assert.Equal(50_000 / 1e6 * 0.50 + 80_000 / 1e6 * 0.05, c!.Value, 9);
    }

    [Fact]
    public void A_tier_with_zero_input_price_does_not_divide_by_zero()
    {
        var r = new Pricing.Rate(0, 1.0, 100, 2.0, 3.0, CachedIn: 0.5);
        var c = Pricing.CostAt(r, new TokenUsage(200, 0, 100));
        Assert.True(double.IsFinite(c));
        Assert.Equal(100 / 1e6 * 2.0 + 100 / 1e6 * 0.5, c, 12);
    }

    [Fact]
    public void Pricing_file_entries_take_cache_prices()
    {
        var r = Pricing.ParseOverrides("""{ "m": { "in": 1.0, "out": 2.0, "cachedIn": 0.1, "cacheWrite": 1.25 } }""")!["m"];
        Assert.Equal(0.1, r.CachedIn);
        Assert.Equal(1.25, r.CacheWrite);
    }

    [Theory]
    [InlineData("gemini-2.5-pro", 200_000, 1.25)]
    [InlineData("gemini-2.5-pro", 200_001, 2.50)]
    public void Gemini_25_pro_has_its_long_prompt_tier(string model, long inTokens, double rate)
    {
        var c = Pricing.CostFor(model, inTokens, 0);
        Assert.Equal(inTokens / 1e6 * rate, c!.Value, 9);
    }

    [Fact]
    public void Gemini_25_pro_long_prompt_output_and_cache_rates()
    {
        Assert.Equal(15.0, Pricing.CostFor("gemini-2.5-pro", 200_001, 1_000_000)!.Value - 200_001 / 1e6 * 2.50, 9);
        Assert.Equal(200_001 / 1e6 * 0.25, Pricing.CostFor("gemini-2.5-pro", new TokenUsage(200_001, 0, 200_001))!.Value, 9);
    }

    [Theory]
    [InlineData("gemini-3.1-pro-preview", 2.00, 12.00, 0.20)]
    [InlineData("gemini-3.1-pro-preview-customtools", 2.00, 12.00, 0.20)]
    [InlineData("gemini-3.5-flash-lite", 0.30, 2.50, 0.03)]
    [InlineData("gemini-3.1-flash-lite", 0.25, 1.50, 0.025)]
    [InlineData("gemini-3-flash-preview", 0.50, 3.00, 0.05)]
    public void Gemini_3_models_price_input_output_and_cached(string model, double pin, double pout, double cached)
    {
        // 100k stays under the 200k tier line of the pro rows
        Assert.Equal((pin + pout) / 10, Pricing.CostFor(model, 100_000, 100_000)!.Value, 9);
        Assert.Equal(cached / 10, Pricing.CostFor(model, new TokenUsage(100_000, 0, 100_000))!.Value, 9);
    }

    [Fact]
    public void Gemini_31_pro_doubles_above_200k()
    {
        var c = Pricing.CostFor("gemini-3.1-pro-preview", new TokenUsage(300_000, 1_000_000, 100_000));
        Assert.Equal(200_000 / 1e6 * 4.00 + 100_000 / 1e6 * 0.40 + 18.0, c!.Value, 9);
    }

    [Theory]
    [InlineData("claude-fable-5-2", 8.0, 10.0, 0.20)]
    [InlineData("claude-fable-5", 10.0, 12.50, 1.0)]
    [InlineData("claude-opus-5-5", 4.0, 5.0, 0.20)]
    [InlineData("claude-opus-4-5", 5.0, 6.25, 0.50)]
    [InlineData("claude-sonnet-5", 2.0, 2.50, 0.20)]
    [InlineData("claude-sonnet-4-6", 3.0, 3.75, 0.30)]
    [InlineData("claude-haiku-4-5", 1.0, 1.25, 0.10)]
    public void Anthropic_rows_carry_cache_read_and_write_prices(string model, double pin, double write, double read)
    {
        var u = new TokenUsage(0, 0, 1_000_000, 1_000_000);
        Assert.Equal(read + write, Pricing.CostFor(model, u)!.Value, 9);
        Assert.Equal(pin, Pricing.CostFor(model, 1_000_000, 0)!.Value, 9);
    }
}
