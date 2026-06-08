// Seerlens SDK, by Elad Sertshuk. https://github.com/eladser  ko-fi.com/eladser
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

    public static void Configure(string collectorUrl)
    {
        var old = Interlocked.Exchange(ref _sink, new Exporter(collectorUrl));
        (old as IDisposable)?.Dispose();
    }

    internal static void UseSink(ITraceSink sink) => _sink = sink;

    // Group several calls (and the tool work between them) into one trace.
    public static IDisposable Begin(string name)
    {
        var trace = new TraceBuilder(name);
        _current.Value = trace;
        return new TraceScope(trace);
    }

    // Time a tool or MCP call inside the current trace. Pass kind "mcp" for a
    // Model Context Protocol call. Capture what went in and out with Complete/Fail
    // so the dashboard shows the arguments and the result.
    public static ToolSpan Tool(string name, string? arguments = null, string kind = "tool")
    {
        var trace = _current.Value;
        return new ToolSpan(trace, name, kind, arguments);
    }

    public static ToolSpan Mcp(string name, string? arguments = null) => Tool(name, arguments, "mcp");

    // Record an already-finished step. Lands in the current trace if one is open,
    // otherwise becomes a trace of its own. This is the hook integrations (like the
    // Semantic Kernel filter) use to report a call they timed themselves.
    public static void AddSpan(string name, string kind, double durationMs, string? model = null,
        long? promptTokens = null, long? completionTokens = null,
        string? input = null, string? output = null, string? error = null)
        => Record(new SpanPayload(Guid.NewGuid().ToString("N"), null, name, kind,
            TraceBuilder.Now(), durationMs, model, promptTokens, completionTokens, input, output, error));

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
            // only clear if we're still the current trace, so a nested scope can't wipe its parent
            if (_current.Value?.Id == trace.Id)
                _current.Value = null;
            Ship(trace.Build());
        }
    }

    /// <summary>
    /// A tool or MCP call in progress. Set the outcome with <see cref="Complete"/>
    /// or <see cref="Fail"/>; the span is recorded when it's disposed. If there's no
    /// active trace it's a no-op.
    /// </summary>
    public sealed class ToolSpan : IDisposable
    {
        readonly TraceBuilder? _trace;
        readonly string _name;
        readonly string _kind;
        readonly string? _arguments;
        readonly long _startedAt = TraceBuilder.Now();
        readonly long _ts = Stopwatch.GetTimestamp();
        string? _result;
        string? _error;

        internal ToolSpan(TraceBuilder? trace, string name, string kind, string? arguments)
        {
            _trace = trace;
            _name = name;
            _kind = kind;
            _arguments = arguments;
        }

        public ToolSpan Complete(string result) { _result = result; return this; }
        public ToolSpan Fail(string error) { _error = error; return this; }

        public void Dispose()
        {
            if (_trace is null) return;
            var ms = Stopwatch.GetElapsedTime(_ts).TotalMilliseconds;
            _trace.Add(new SpanPayload(Guid.NewGuid().ToString("N"), null, _name, _kind,
                _startedAt, ms, null, null, null, _arguments, _result, _error));
        }
    }
}
