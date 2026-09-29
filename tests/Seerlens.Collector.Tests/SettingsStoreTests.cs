using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class SettingsStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"seerlens-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void Defaults_when_no_file()
    {
        var s = new SettingsStore(_path);
        Assert.Null(s.GetBudget().MonthlyUsd);
        Assert.Null(s.GetAlerts().WebhookUrl);
    }

    [Fact]
    public void Budget_and_alerts_persist_independently()
    {
        var s = new SettingsStore(_path);
        s.SetBudget(new Budget(50));
        s.SetAlerts(new Alerts("https://hooks.example/x", 0.1));

        var fresh = new SettingsStore(_path);
        Assert.Equal(50, fresh.GetBudget().MonthlyUsd);
        Assert.Equal("https://hooks.example/x", fresh.GetAlerts().WebhookUrl);
        Assert.Equal(0.1, fresh.GetAlerts().RegressionDrop);

        // setting one doesn't wipe the other
        fresh.SetBudget(new Budget(75));
        Assert.Equal("https://hooks.example/x", new SettingsStore(_path).GetAlerts().WebhookUrl);
    }

    [Fact]
    public void Last_run_date_survives_a_restart()
    {
        var s = new SettingsStore(_path);
        Assert.Null(s.GetLastRun("caps keyword 09:00"));

        s.SetLastRun("caps keyword 09:00", new DateOnly(2026, 6, 9));

        var fresh = new SettingsStore(_path);
        Assert.Equal(new DateOnly(2026, 6, 9), fresh.GetLastRun("caps keyword 09:00"));
        // a different schedule key isn't touched
        Assert.Null(fresh.GetLastRun("other keyword 09:00"));
    }
}
