using System.Diagnostics;

namespace Seerlens.Sdk;

/// <summary>
/// Entry point for the SDK. Point it at a running collector, then wrap your
/// IChatClient with <c>.UseSeerlens()</c>. Calls are recorded and shipped in the
/// background. If no collector is configured, everything is a no-op.
/// </summary>
public static class SeerlensTrace
{
    static ITraceSink? _sink;
    static readonly AsyncLocal<TraceBuilder?> _current = new();

    public static void Configure(string collectorUrl) => _sink = new Exporter(collectorUrl);

    internal static void UseSink(ITraceSink sink) => _sink = sink;

    // Group several calls (and the tool work between them) into one trace.
    public static IDisposable Begin(string name)
    {
        var trace = new TraceBuilder(name);
        _current.Value = trace;
        return new TraceScope(trace);
    }

    // Time a tool/function call inside the current trace.
    public static IDisposable Tool(string name)
    {
        var trace = _current.Value;
        if (trace is null) return NoopScope.Instance;

        var startedAt = TraceBuilder.Now();
        var ts = Stopwatch.GetTimestamp();
        return new ActionScope(error =>
        {
            var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
            trace.Add(new SpanPayload(Guid.NewGuid().ToString("N"), null, name, "tool",
                startedAt, ms, null, null, null, null, null, error));
        });
    }

    internal static TraceBuilder? Current => _current.Value;

    internal static void Record(SpanPayload span)
    {
        var trace = _current.Value;
        if (trace is not null)
        {
            trace.Add(span);
            return;
        }

        // No ambient trace, so this single call is its own trace.
        var name = span.Model is null ? "chat" : $"chat: {span.Model}";
        var one = new TracePayload(Guid.NewGuid().ToString("N"), name, span.StartedAt, span.DurationMs,
            span.Model is null ? null : Providers.For(span.Model), span.Model,
            span.Error is null ? "ok" : "error", [span]);
        Ship(one);
    }

    internal static void Ship(TracePayload trace)
    {
        try { _sink?.Ship(trace); }
        catch { } // shipping must never throw into the host
    }

    sealed class TraceScope(TraceBuilder trace) : IDisposable
    {
        public void Dispose()
        {
            _current.Value = null;
            Ship(trace.Build());
        }
    }

    sealed class ActionScope(Action<string?> onDispose) : IDisposable
    {
        public void Dispose() => onDispose(null);
    }

    sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
