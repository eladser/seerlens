namespace Seerlens.Collector;

// Rough public list prices, USD per 1M tokens, as of mid-2026.
// Good enough to spot expensive calls; not billing-grade.
public static class Pricing
{
    record Rate(double In, double Out);

    static readonly Dictionary<string, Rate> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4o"] = new(2.50, 10.00),
        ["gpt-4o-mini"] = new(0.15, 0.60),
        ["gpt-4.1"] = new(2.00, 8.00),
        ["gpt-4.1-mini"] = new(0.40, 1.60),
        ["o3-mini"] = new(1.10, 4.40),
        ["claude-3-5-sonnet"] = new(3.00, 15.00),
        ["claude-3-5-haiku"] = new(0.80, 4.00),
        ["gemini-1.5-pro"] = new(1.25, 5.00),
        ["gemini-1.5-flash"] = new(0.075, 0.30),
    };

    public static double? CostFor(string? model, long? inTokens, long? outTokens)
    {
        if (model is null || !Table.TryGetValue(Normalize(model), out var rate))
            return null;

        var inCost = (inTokens ?? 0) / 1_000_000.0 * rate.In;
        var outCost = (outTokens ?? 0) / 1_000_000.0 * rate.Out;
        return inCost + outCost;
    }

    // Strip a trailing date stamp, e.g. "gpt-4o-2024-08-06" -> "gpt-4o".
    static string Normalize(string model)
    {
        var i = model.IndexOf("-20", StringComparison.Ordinal);
        return i > 0 ? model[..i] : model;
    }
}
