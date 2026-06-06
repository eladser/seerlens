using System.Net.Http.Json;

namespace Seerlens.Collector;

// Posts a heads-up to a webhook when answer quality regresses or spend crosses the
// budget. Fire-and-forget: a webhook being down must never break an eval or ingest.
// The payload carries a "text" field so a Slack incoming webhook works as-is.
public sealed class Alerter(SettingsStore settings)
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    readonly Lock _gate = new();
    string? _budgetAlertedFor;   // month key we've already warned about, so we don't spam

    public Task EvalRegressed(string set, double from, double to)
    {
        var drop = from - to;
        return Post("eval_regression",
            $"Seerlens: '{set}' answer quality dropped {drop:P0} ({from:P0} -> {to:P0}).",
            new { set, from, to, drop });
    }

    public Task MaybeOverBudget(double monthToDate, double budget, string monthKey)
    {
        lock (_gate)
        {
            if (_budgetAlertedFor == monthKey) return Task.CompletedTask;
            _budgetAlertedFor = monthKey;
        }
        return Post("over_budget",
            $"Seerlens: over budget. ${monthToDate:0.00} spent this month against a ${budget:0.00} cap.",
            new { monthToDate, budget, monthKey });
    }

    async Task Post(string type, string text, object details)
    {
        var url = settings.GetAlerts().WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var resp = await Http.PostAsJsonAsync(url, new { type, text, details });
        }
        catch
        {
            // a dead webhook is not our problem to crash over
        }
    }
}
