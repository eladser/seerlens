using System.Text.Json;

namespace Seerlens.Collector;

// Rough public list prices, USD per 1M tokens, checked November 2026.
// Good enough to spot expensive calls; not billing-grade. Prices move, so you can
// override or extend the table with a JSON file via SEERLENS_PRICING_FILE
// ({ "model": { "in": 1.0, "out": 2.0 } }). Models priced by prompt size also take
// "above" (input tokens) and "inAbove"/"outAbove": a call with more input tokens than
// "above" is priced at the second pair, a call at or under it at the first. "cachedIn" and
// "cacheWrite" price cache reads and writes (they scale with the tier); without them cached
// tokens cost the plain input price.
public record TokenUsage(long? Input, long? Output, long? CacheRead = null, long? CacheWrite = null, long? Reasoning = null);

public static class Pricing
{
    internal record Rate(double In, double Out, long? Above = null, double? InAbove = null, double? OutAbove = null,
        double? CachedIn = null, double? CacheWrite = null);

    internal static IReadOnlyDictionary<string, Rate> Rates => Table;

    static readonly Dictionary<string, Rate> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI. Long context (over 272K input tokens) is the second pair.
        ["gpt-6-astra"] = new(10.00, 50.00, 272_000, 20.00, 75.00, 1.00, 12.50),
        ["gpt-6.1-sol"] = new(2.00, 10.00, 272_000, 4.00, 15.00, 0.10, 2.50),
        ["gpt-6.1-terra"] = new(1.00, 5.00, 272_000, 2.00, 7.50, 0.05, 1.25),
        ["gpt-6-luna"] = new(0.10, 0.50, 272_000, 0.20, 0.75, 0.01, 0.125),
        ["gpt-6-sol"] = new(2.00, 10.00, 272_000, 4.00, 15.00, 0.20, 2.50),
        ["gpt-5.6-terra"] = new(2.00, 12.00, 272_000, 4.00, 18.00, 0.20, 2.50),
        ["gpt-5.6-luna"] = new(0.20, 1.20, 272_000, 0.40, 1.80, 0.02, 0.25),
        ["gpt-5.5"] = new(5.00, 30.00, 272_000, 10.00, 45.00, 0.50),
        ["gpt-5.5-pro"] = new(30.00, 180.00, 272_000, 60.00, 270.00),
        ["gpt-5.4"] = new(2.50, 15.00, 272_000, 5.00, 22.50, 0.25),
        ["gpt-5.4-mini"] = new(0.75, 4.50, CachedIn: 0.075),
        ["gpt-5.4-nano"] = new(0.20, 1.25, CachedIn: 0.02),
        ["gpt-5.2"] = new(1.75, 14.00, CachedIn: 0.175),
        ["gpt-5.1"] = new(1.25, 10.00, CachedIn: 0.125),
        ["gpt-5"] = new(1.25, 10.00, CachedIn: 0.125),
        ["gpt-5-mini"] = new(0.25, 2.00, CachedIn: 0.025),
        ["gpt-5-nano"] = new(0.05, 0.40, CachedIn: 0.005),
        ["gpt-4.1"] = new(2.00, 8.00, CachedIn: 0.50),
        ["gpt-4.1-mini"] = new(0.40, 1.60, CachedIn: 0.10),
        ["gpt-4.1-nano"] = new(0.10, 0.40),
        ["gpt-4o"] = new(2.50, 10.00, CachedIn: 1.25),
        ["gpt-4o-mini"] = new(0.15, 0.60, CachedIn: 0.075),
        ["o3"] = new(2.00, 8.00, CachedIn: 0.50),
        ["o3-pro"] = new(20.00, 80.00),
        ["o4-mini"] = new(1.10, 4.40),

        // Anthropic
        ["claude-fable-5-2"] = new(8.00, 40.00, CachedIn: 0.20, CacheWrite: 10.00),
        ["claude-fable-5-1"] = new(10.00, 50.00, CachedIn: 0.25, CacheWrite: 12.50),
        ["claude-fable-5"] = new(10.00, 50.00, CachedIn: 1.00, CacheWrite: 12.50),
        ["claude-mythos-5-2"] = new(8.00, 40.00, CachedIn: 0.20, CacheWrite: 10.00),
        ["claude-mythos-5-1"] = new(10.00, 50.00, CachedIn: 0.25, CacheWrite: 12.50),
        ["claude-mythos-5"] = new(10.00, 50.00, CachedIn: 1.00, CacheWrite: 12.50),
        ["claude-opus-5-5"] = new(4.00, 20.00, CachedIn: 0.20, CacheWrite: 5.00),
        ["claude-opus-5"] = new(5.00, 25.00, CachedIn: 0.50, CacheWrite: 6.25),
        ["claude-opus-4-8"] = new(5.00, 25.00, CachedIn: 0.50, CacheWrite: 6.25),
        ["claude-opus-4-7"] = new(5.00, 25.00, CachedIn: 0.50, CacheWrite: 6.25),
        ["claude-opus-4-6"] = new(5.00, 25.00, CachedIn: 0.50, CacheWrite: 6.25),
        ["claude-opus-4-5"] = new(5.00, 25.00, CachedIn: 0.50, CacheWrite: 6.25),
        ["claude-sonnet-5-5"] = new(2.00, 10.00, CachedIn: 0.10, CacheWrite: 2.50),
        ["claude-sonnet-5"] = new(2.00, 10.00, CachedIn: 0.20, CacheWrite: 2.50),
        ["claude-sonnet-4-6"] = new(3.00, 15.00, CachedIn: 0.30, CacheWrite: 3.75),
        ["claude-sonnet-4-5"] = new(3.00, 15.00, CachedIn: 0.30, CacheWrite: 3.75),
        ["claude-haiku-5-5"] = new(0.10, 0.50, 100_000, 0.50, 2.50, 0.01, 0.125),
        ["claude-haiku-4-5"] = new(1.00, 5.00, CachedIn: 0.10, CacheWrite: 1.25),
        ["claude-3-7-sonnet"] = new(3.00, 15.00),   // kept so older traces still price
        ["claude-3-5-haiku"] = new(0.80, 4.00),

        // Google
        ["gemini-2.5-pro"] = new(1.25, 10.00, 200_000, 2.50, 15.00, 0.125),
        ["gemini-2.5-flash"] = new(0.30, 2.50, CachedIn: 0.03),

        // Gemini 3.x, standard tier, text/image/video input. Source: https://aistudio.google.com/docs/pricing
        // (ai.google.dev/gemini-api/docs/pricing redirects there), read 2026-11-17. Output includes thinking.
        // Left out: gemini-3.8-flash and gemini-3.6-flash, whose price doubles on 2027-01-01.
        ["gemini-3.1-pro-preview"] = new(2.00, 12.00, 200_000, 4.00, 18.00, 0.20),
        ["gemini-3.1-pro-preview-customtools"] = new(2.00, 12.00, 200_000, 4.00, 18.00, 0.20),
        ["gemini-3.5-flash-lite"] = new(0.30, 2.50, CachedIn: 0.03),
        ["gemini-3.1-flash-lite"] = new(0.25, 1.50, CachedIn: 0.025),
        ["gemini-3-flash-preview"] = new(0.50, 3.00, CachedIn: 0.05),
        ["gemini-2.0-flash"] = new(0.10, 0.40),

        // xAI
        ["grok-4.3"] = new(1.25, 2.50),
        ["grok-4"] = new(3.00, 15.00),
        ["grok-4.1-fast"] = new(0.20, 0.50),

        // DeepSeek, Meta (Groq)
        ["deepseek-chat"] = new(0.28, 0.42),
        ["llama-3.3-70b-versatile"] = new(0.59, 0.79),
        ["llama-3.1-8b-instant"] = new(0.05, 0.08),
    };

    public static bool HasOverride { get; private set; }

    static Pricing() => LoadOverrides();

    public static double? CostFor(string? model, long? inTokens, long? outTokens) =>
        CostFor(model, new TokenUsage(inTokens, outTokens));

    public static double? CostFor(string? model, TokenUsage u)
    {
        if (model is null || !Table.TryGetValue(Normalize(model), out var rate))
            return null;

        return CostAt(rate, u);
    }

    // Costs are stored at ingest. Counts: semconv says input includes cache reads/writes
    // and output includes reasoning, but Anthropic's raw usage leaves cache out of input.
    // Cache bigger than input can only be the exclusive kind.
    internal static double CostAt(Rate rate, TokenUsage u)
    {
        long inT = u.Input ?? 0, outT = u.Output ?? 0, cr = u.CacheRead ?? 0, cw = u.CacheWrite ?? 0, r = u.Reasoning ?? 0;
        var exclusive = cr + cw > inT;
        var prompt = exclusive ? inT + cr + cw : inT;
        var fresh = exclusive ? inT : inT - cr - cw;

        // over the threshold the higher pair prices the whole call, not just the tokens past it
        var above = prompt > rate.Above;
        var (pin, pout) = above
            ? (rate.InAbove ?? rate.In, rate.OutAbove ?? rate.Out)
            : (rate.In, rate.Out);
        var k = above && rate.In > 0 && rate.InAbove is { } ia ? ia / rate.In : 1;

        var inCost = fresh / 1_000_000.0 * pin;
        inCost += cr / 1_000_000.0 * (rate.CachedIn is { } c ? c * k : pin);
        inCost += cw / 1_000_000.0 * (rate.CacheWrite is { } w ? w * k : pin);

        // reasoning bigger than output means output left it out
        var billedOut = r > outT ? outT + r : outT;
        return inCost + billedOut / 1_000_000.0 * pout;
    }

    internal static Dictionary<string, Rate>? ParseOverrides(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, Rate>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    static void LoadOverrides()
    {
        var path = Environment.GetEnvironmentVariable("SEERLENS_PRICING_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            var extra = ParseOverrides(File.ReadAllText(path));
            if (extra is null) return;
            foreach (var (model, rate) in extra)
                Table[model] = rate;
            HasOverride = true;
        }
        catch
        {
            // a bad pricing file shouldn't take the collector down; just keep the defaults
        }
    }

    // Strip a trailing date stamp, e.g. "gpt-4o-2024-08-06" -> "gpt-4o".
    static string Normalize(string model)
    {
        var i = model.IndexOf("-20", StringComparison.Ordinal);
        return i > 0 ? model[..i] : model;
    }
}
