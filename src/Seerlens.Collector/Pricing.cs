using System.Text.Json;

namespace Seerlens.Collector;

// Rough public list prices, USD per 1M tokens, as of mid-2026.
// Good enough to spot expensive calls; not billing-grade. Prices move, so you can
// override or extend the table with a JSON file via SEERLENS_PRICING_FILE
// ({ "model": { "in": 1.0, "out": 2.0 } }).
public static class Pricing
{
    record Rate(double In, double Out);

    static readonly Dictionary<string, Rate> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI
        ["gpt-5.5"] = new(5.00, 30.00),
        ["gpt-5.4"] = new(2.50, 15.00),
        ["gpt-4.1"] = new(2.00, 8.00),
        ["gpt-4.1-mini"] = new(0.40, 1.60),
        ["gpt-4.1-nano"] = new(0.10, 0.40),
        ["gpt-4o"] = new(2.50, 10.00),
        ["gpt-4o-mini"] = new(0.15, 0.60),
        ["o3"] = new(2.00, 8.00),
        ["o4-mini"] = new(1.10, 4.40),

        // Anthropic
        ["claude-opus-4-8"] = new(5.00, 25.00),
        ["claude-opus-4-7"] = new(5.00, 25.00),
        ["claude-sonnet-4-6"] = new(3.00, 15.00),
        ["claude-sonnet-4-5"] = new(3.00, 15.00),
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

        var inCost = (inTokens ?? 0) / 1_000_000.0 * rate.In;
        var outCost = (outTokens ?? 0) / 1_000_000.0 * rate.Out;
        return inCost + outCost;
    }

    static void LoadOverrides()
    {
        var path = Environment.GetEnvironmentVariable("SEERLENS_PRICING_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            var extra = JsonSerializer.Deserialize<Dictionary<string, Rate>>(
                File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
