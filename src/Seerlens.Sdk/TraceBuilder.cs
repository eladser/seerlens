using System.Diagnostics;

namespace Seerlens.Sdk;

// Collects spans for one logical interaction. Started by Seerlens.BeginTrace.
sealed class TraceBuilder
{
    readonly long _startedAt = Now();
    readonly long _startTs = Stopwatch.GetTimestamp();
    readonly List<SpanPayload> _spans = new();
    readonly object _gate = new();

    string? _model;
    string? _provider;
    string _status = "ok";

    public TraceBuilder(string name) => Name = name;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Name { get; }

    public void Add(SpanPayload span)
    {
        lock (_gate)
        {
            _spans.Add(span);
            if (span.Kind == "llm" && span.Model is not null)
            {
                _model ??= span.Model;
                _provider ??= Providers.For(span.Model);
            }
            if (span.Error is not null) _status = "error";
        }
    }

    public TracePayload Build()
    {
        var elapsedMs = Stopwatch.GetElapsedTime(_startTs).TotalMilliseconds;
        lock (_gate)
            return new TracePayload(Id, Name, _startedAt, elapsedMs, _provider, _model, _status, new(_spans));
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

static class Providers
{
    public static string? For(string model)
    {
        var m = model.ToLowerInvariant();
        if (m.StartsWith("gpt") || m.StartsWith("o1") || m.StartsWith("o3")) return "openai";
        if (m.Contains("claude")) return "anthropic";
        if (m.Contains("gemini")) return "google";
        if (m.Contains("mistral") || m.Contains("mixtral")) return "mistral";
        if (m.Contains("llama")) return "meta";
        return null;
    }
}
