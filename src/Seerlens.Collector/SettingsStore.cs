using System.Text.Json;

namespace Seerlens.Collector;

// A spend ceiling you can set from the dashboard. Kept in a small JSON file next
// to the db so it survives restarts. Both fields optional: set what you care about.
public record Budget(double? MonthlyUsd = null, double? AlertPerCallUsd = null);

// Where to send a heads-up when an eval regresses or spend crosses the budget.
// A Slack incoming webhook works directly; the payload includes a "text" field.
public record Alerts(string? WebhookUrl = null, double RegressionDrop = 0.05);

// Tiny JSON-file settings store for the things you control from the dashboard.
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly string _path;
    readonly Lock _gate = new();

    public SettingsStore(string path) => _path = path;

    public Budget GetBudget() { lock (_gate) return Read().Budget ?? new Budget(); }
    public Alerts GetAlerts() { lock (_gate) return Read().Alerts ?? new Alerts(); }

    // Read-modify-write under one lock so saving budget can't clobber alerts.
    public void SetBudget(Budget budget) { lock (_gate) Write(Read() with { Budget = budget }); }
    public void SetAlerts(Alerts alerts) { lock (_gate) Write(Read() with { Alerts = alerts }); }

    Settings Read()
    {
        if (!File.Exists(_path)) return Empty;
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), Json) ?? Empty; }
        catch { return Empty; }
    }

    void Write(Settings settings) => File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json));

    static Settings Empty => new(new Budget(), new Alerts());

    record Settings(Budget? Budget, Alerts? Alerts);
}
