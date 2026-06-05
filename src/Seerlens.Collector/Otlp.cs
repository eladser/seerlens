namespace Seerlens.Collector;

// Minimal view of an OTLP/HTTP JSON trace export. We only read the fields we need
// to pull GenAI spans out; everything else in the payload is ignored.
public record OtlpRequest(List<OtlpResourceSpans>? ResourceSpans);
public record OtlpResourceSpans(List<OtlpScopeSpans>? ScopeSpans);
public record OtlpScopeSpans(List<OtlpSpan>? Spans);

public record OtlpSpan(
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    string? Name,
    string? StartTimeUnixNano,
    string? EndTimeUnixNano,
    List<OtlpAttribute>? Attributes,
    OtlpStatus? Status);

public record OtlpAttribute(string Key, OtlpValue? Value);
public record OtlpValue(string? StringValue, string? IntValue, double? DoubleValue, bool? BoolValue);
public record OtlpStatus(int Code, string? Message);

// Maps OTLP spans that follow the OpenTelemetry GenAI conventions into our own
// trace model. This is what lets any instrumented app (Python, JS, ...) show up
// without the Seerlens SDK.
public static class Otlp
{
    record Mapped(IngestSpan Span, string? System);

    public static List<IngestTrace> ToTraces(OtlpRequest req)
    {
        var raw = (req.ResourceSpans ?? [])
            .SelectMany(r => r.ScopeSpans ?? [])
            .SelectMany(s => s.Spans ?? [])
            .Where(s => s.TraceId is not null && s.StartTimeUnixNano is not null);

        var traces = new List<IngestTrace>();
        foreach (var group in raw.GroupBy(s => s.TraceId!))
        {
            var items = group.Select(Map).OrderBy(m => m.Span.StartedAt).ToList();
            if (items.Count == 0) continue;

            var spans = items.Select(m => m.Span).ToList();
            var root = group.FirstOrDefault(s => string.IsNullOrEmpty(s.ParentSpanId));
            var llm = items.FirstOrDefault(m => m.Span.Kind == "llm");
            var start = spans.Min(s => s.StartedAt);
            var end = spans.Max(s => s.StartedAt + (long)s.DurationMs);

            traces.Add(new IngestTrace(
                group.Key,
                root?.Name ?? spans[0].Name,
                start,
                end - start,
                llm?.System ?? Provider(llm?.Span.Model),
                llm?.Span.Model,
                spans.Any(s => s.Error is not null) ? "error" : "ok",
                spans));
        }
        return traces;
    }

    static Mapped Map(OtlpSpan span)
    {
        // last value wins; an exporter sending a key twice shouldn't crash ingest
        var attr = new Dictionary<string, OtlpValue?>(StringComparer.Ordinal);
        foreach (var a in span.Attributes ?? [])
            attr[a.Key] = a.Value;

        var model = Str(attr, "gen_ai.response.model") ?? Str(attr, "gen_ai.request.model");
        var inTokens = Long(attr, "gen_ai.usage.input_tokens") ?? Long(attr, "gen_ai.usage.prompt_tokens");
        var outTokens = Long(attr, "gen_ai.usage.output_tokens") ?? Long(attr, "gen_ai.usage.completion_tokens");

        var startMs = Nanos(span.StartTimeUnixNano);
        var endMs = Nanos(span.EndTimeUnixNano);

        var s = new IngestSpan(
            span.SpanId ?? Guid.NewGuid().ToString("N"),
            string.IsNullOrEmpty(span.ParentSpanId) ? null : span.ParentSpanId,
            span.Name ?? "span",
            Kind(attr, model),
            startMs,
            Math.Max(0, endMs - startMs),
            model,
            inTokens,
            outTokens,
            Str(attr, "gen_ai.prompt"),
            Str(attr, "gen_ai.completion"),
            span.Status?.Code == 2 ? span.Status.Message ?? "error" : null);

        return new Mapped(s, Str(attr, "gen_ai.system"));
    }

    static string Kind(Dictionary<string, OtlpValue?> attr, string? model)
    {
        if (Str(attr, "gen_ai.operation.name") == "execute_tool" || attr.ContainsKey("gen_ai.tool.name"))
            return "tool";
        if (model is not null || attr.Keys.Any(k => k.StartsWith("gen_ai.", StringComparison.Ordinal)))
            return "llm";
        return "other";
    }

    static string? Provider(string? model)
    {
        if (model is null) return null;
        var m = model.ToLowerInvariant();
        if (m.StartsWith("gpt") || m.StartsWith("o1") || m.StartsWith("o3")) return "openai";
        if (m.Contains("claude")) return "anthropic";
        if (m.Contains("gemini")) return "google";
        return null;
    }

    static string? Str(Dictionary<string, OtlpValue?> attr, string key) =>
        attr.TryGetValue(key, out var v) ? v?.StringValue : null;

    static long? Long(Dictionary<string, OtlpValue?> attr, string key)
    {
        if (!attr.TryGetValue(key, out var v) || v is null) return null;
        if (v.IntValue is not null && long.TryParse(v.IntValue, out var n)) return n;
        if (v.DoubleValue is { } d) return (long)d;
        return null;
    }

    static long Nanos(string? value) =>
        long.TryParse(value, out var n) ? n / 1_000_000 : 0;
}
