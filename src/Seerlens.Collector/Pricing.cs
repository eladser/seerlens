using System.Text.Json;

namespace Seerlens.Collector;

// Rough public list prices, USD per 1M tokens, checked November 2026.
// Good enough to spot expensive calls; not billing-grade. Prices move, so you can
// override or extend the table with a JSON file via SEERLENS_PRICING_FILE
// ({ "model": { "in": 1.0, "out": 2.0 } }). Models priced by prompt size also take
// "above" (input tokens) and "inAbove"/"outAbove": a call with more input tokens than
// "above" is priced at the second pair, a call at or under it at the first.
public static class Pricing
{
    internal record Rate(double In, double Out, long? Above = null, double? InAbove = null, double? OutAbove = null);

    static readonly Dictionary<string, Rate> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI. Long context (over 272K input tokens) is the second pair.
        ["gpt-6-astra"] = new(10.00, 50.00, 272_000, 20.00, 75.00),
        ["gpt-6.1-sol"] = new(2.00, 10.00, 272_000, 4.00, 15.00),
        ["gpt-6.1-terra"] = new(1.00, 5.00, 272_000, 2.00, 7.50),
        ["gpt-6-luna"] = new(0.10, 0.50, 272_000, 0.20, 0.75),
        ["gpt-6-sol"] = new(2.00, 10.00, 272_000, 4.00, 15.00),
        ["gpt-5.6-terra"] = new(2.00, 12.00, 272_000, 4.00, 18.00),
        ["gpt-5.6-luna"] = new(0.20, 1.20, 272_000, 0.40, 1.80),
        ["gpt-5.5"] = new(5.00, 30.00, 272_000, 10.00, 45.00),
        ["gpt-5.5-pro"] = new(30.00, 180.00, 272_000, 60.00, 270.00),
        ["gpt-5.4"] = new(2.50, 15.00, 272_000, 5.00, 22.50),
        ["gpt-5.4-mini"] = new(0.75, 4.50),
        ["gpt-5.4-nano"] = new(0.20, 1.25),
        ["gpt-5.2"] = new(1.75, 14.00),
        ["gpt-5.1"] = new(1.25, 10.00),
        ["gpt-5"] = new(1.25, 10.00),
        ["gpt-5-mini"] = new(0.25, 2.00),
        ["gpt-5-nano"] = new(0.05, 0.40),
        ["gpt-4.1"] = new(2.00, 8.00),
        ["gpt-4.1-mini"] = new(0.40, 1.60),
        ["gpt-4.1-nano"] = new(0.10, 0.40),
        ["gpt-4o"] = new(2.50, 10.00),
        ["gpt-4o-mini"] = new(0.15, 0.60),
        ["o3"] = new(2.00, 8.00),
        ["o3-pro"] = new(20.00, 80.00),
        ["o4-mini"] = new(1.10, 4.40),

        // Anthropic
        ["claude-fable-5-2"] = new(8.00, 40.00),
        ["claude-fable-5-1"] = new(10.00, 50.00),
        ["claude-fable-5"] = new(10.00, 50.00),
        ["claude-mythos-5-2"] = new(8.00, 40.00),
        ["claude-mythos-5-1"] = new(10.00, 50.00),
        ["claude-mythos-5"] = new(10.00, 50.00),
        ["claude-opus-5-5"] = new(4.00, 20.00),
        ["claude-opus-5"] = new(5.00, 25.00),
        ["claude-opus-4-8"] = new(5.00, 25.00),
        ["claude-opus-4-7"] = new(5.00, 25.00),
        ["claude-opus-4-6"] = new(5.00, 25.00),
        ["claude-opus-4-5"] = new(5.00, 25.00),
        ["claude-sonnet-5-5"] = new(2.00, 10.00),
        ["claude-sonnet-5"] = new(2.00, 10.00),
        ["claude-sonnet-4-6"] = new(3.00, 15.00),
        ["claude-sonnet-4-5"] = new(3.00, 15.00),
        ["claude-haiku-5-5"] = new(0.10, 0.50, 100_000, 0.50, 2.50),
        ["claude-haiku-4-5"] = new(1.00, 5.00),
        ["claude-3-7-sonnet"] = new(3.00, 15.00),   // kept so older traces still price
        ["claude-3-5-haiku"] = new(0.80, 4.00),

        // Google
        ["gemini-2.5-pro"] = new(1.25, 10.00),
        ["gemini-2.5-flash"] = new(0.30, 2.50),
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

    public static double? CostFor(string? model, long? inTokens, long? outTokens)
    {
        if (model is null || !Table.TryGetValue(Normalize(model), out var rate))
            return null;

        return CostAt(rate, inTokens, outTokens);
    }

    internal static double CostAt(Rate rate, long? inTokens, long? outTokens)
    {
        // over the threshold the higher pair prices the whole call, not just the tokens past it
        var (pin, pout) = inTokens > rate.Above
            ? (rate.InAbove ?? rate.In, rate.OutAbove ?? rate.Out)
            : (rate.In, rate.Out);

        var inCost = (inTokens ?? 0) / 1_000_000.0 * pin;
        var outCost = (outTokens ?? 0) / 1_000_000.0 * pout;
        return inCost + outCost;
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
