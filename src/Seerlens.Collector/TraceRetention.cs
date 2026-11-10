namespace Seerlens.Collector;

// Opt-in retention: with SEERLENS_RETENTION_DAYS=N, traces older than N days (and
// their spans) are dropped at startup and then once a day. Unset or 0 keeps everything.
// Only traces and spans; eval runs and settings are left alone.
public sealed class TraceRetention(TraceStore store, IConfiguration config, ILogger<TraceRetention> log) : BackgroundService
{
    // read once so a bad value warns once, not every day
    readonly int _days = ParseDays(config["SEERLENS_RETENTION_DAYS"], log);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_days == 0) return;

        while (!ct.IsCancellationRequested)
        {
            try { Prune(DateTimeOffset.UtcNow); }
            catch (Exception e) { log.LogError(e, "trace retention pass failed"); }
            try { await Task.Delay(TimeSpan.FromDays(1), ct); } catch (OperationCanceledException) { break; }
        }
    }

    internal int Prune(DateTimeOffset now)
    {
        if (_days == 0) return 0;

        // plain ms math so a huge N can't push a DateTimeOffset out of range
        var cutoff = now.ToUnixTimeMilliseconds() - _days * 86_400_000L;
        var n = store.DeleteOlderThan(cutoff);
        log.LogInformation("retention dropped {Count} traces older than {Days} days", n, _days);
        return n;
    }

    static int ParseDays(string? raw, ILogger log)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (int.TryParse(raw, out var days) && days >= 0) return days;

        log.LogWarning("SEERLENS_RETENTION_DAYS is '{Value}', not a whole number of days; keeping all traces", raw);
        return 0;
    }
}
