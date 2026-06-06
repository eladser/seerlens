using System.Text.Json;

namespace Seerlens.Collector;

// A spend ceiling you can set from the dashboard. Kept in a small JSON file next
// to the db so it survives restarts. Both fields optional: set what you care about.
public record Budget(double? MonthlyUsd = null, double? AlertPerCallUsd = null);

// Tiny JSON-file settings store. Only holds the budget today, but it's the place
// other dashboard-controlled settings would go.
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly string _path;
    readonly Lock _gate = new();

    public SettingsStore(string path) => _path = path;

    public Budget GetBudget()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new Budget();
            try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), Json)?.Budget ?? new Budget(); }
            catch { return new Budget(); }
        }
    }

    public void SetBudget(Budget budget)
    {
        lock (_gate)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new Settings(budget), Json));
        }
    }

    record Settings(Budget Budget);
}
