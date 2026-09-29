using System.Text.Json.Nodes;

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
    OtlpStatus? Status,
    List<OtlpSpanEvent>? Events);

// A span event, e.g. gen_ai.client.inference.operation.details, carrying the same
// kind of attributes as the span itself.
public record OtlpSpanEvent(string? Name, List<OtlpAttribute>? Attributes);

public record OtlpAttribute(string Key, OtlpValue? Value);

// AnyValue from the OTLP wire format. ArrayValue/KvlistValue let gen_ai.input.messages
// and gen_ai.output.messages carry structured message arrays instead of a flat string.
public record OtlpValue(
    string? StringValue,
    string? IntValue,
    double? DoubleValue,
    bool? BoolValue,
    OtlpArrayValue? ArrayValue,
    OtlpKvlistValue? KvlistValue);

public record OtlpArrayValue(List<OtlpValue>? Values);
public record OtlpKvlistValue(List<OtlpAttribute>? Values);
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

    const string InferenceDetailsEvent = "gen_ai.client.inference.operation.details";

    static Mapped Map(OtlpSpan span)
    {
        // last value wins; an exporter sending a key twice shouldn't crash ingest
        var attr = new Dictionary<string, OtlpValue?>(StringComparer.Ordinal);
        foreach (var a in span.Attributes ?? [])
            attr[a.Key] = a.Value;

        // Some instrumentation keeps the message content off the span itself and puts it
        // on a gen_ai.client.inference.operation.details event instead; fall back to that
        // without letting it override anything the span already has.
        foreach (var ev in span.Events ?? [])
        {
            if (ev.Name != InferenceDetailsEvent) continue;
            foreach (var a in ev.Attributes ?? [])
                attr.TryAdd(a.Key, a.Value);
        }

        var model = Str(attr, "gen_ai.response.model") ?? Str(attr, "gen_ai.request.model");
        var inTokens = Long(attr, "gen_ai.usage.input_tokens") ?? Long(attr, "gen_ai.usage.prompt_tokens");
        var outTokens = Long(attr, "gen_ai.usage.output_tokens") ?? Long(attr, "gen_ai.usage.completion_tokens");

        var startMs = Nanos(span.StartTimeUnixNano);
        var endMs = Nanos(span.EndTimeUnixNano);
        var kind = Kind(attr, model);

        // For tool and MCP spans the interesting text is the call arguments and the
        // result, so map those into the prompt/completion slots the UI already shows.
        var toolName = Str(attr, "gen_ai.tool.name") ?? Str(attr, "mcp.tool.name") ?? Str(attr, "mcp.method.name");
        var prompt = kind is "tool" or "mcp"
            ? Str(attr, "gen_ai.tool.call.arguments") ?? Str(attr, "mcp.request.params")
            : RenderMessages(attr, "gen_ai.input.messages") ?? Str(attr, "gen_ai.prompt");
        var completion = kind is "tool" or "mcp"
            ? Str(attr, "gen_ai.tool.message") ?? Str(attr, "mcp.response.result")
            : RenderMessages(attr, "gen_ai.output.messages") ?? Str(attr, "gen_ai.completion");

        var s = new IngestSpan(
            span.SpanId ?? Guid.NewGuid().ToString("N"),
            string.IsNullOrEmpty(span.ParentSpanId) ? null : span.ParentSpanId,
            toolName ?? span.Name ?? "span",
            kind,
            startMs,
            Math.Max(0, endMs - startMs),
            model,
            inTokens,
            outTokens,
            prompt,
            completion,
            span.Status?.Code == 2 ? span.Status.Message ?? "error" : null);

        return new Mapped(s, Str(attr, "gen_ai.provider.name") ?? Str(attr, "gen_ai.system"));
    }

    static string Kind(Dictionary<string, OtlpValue?> attr, string? model)
    {
        // MCP tool calls get their own kind so the agent view can call them out.
        if (attr.Keys.Any(k => k.StartsWith("mcp.", StringComparison.Ordinal))
            || Str(attr, "rpc.system") == "mcp"
            || Str(attr, "gen_ai.tool.type") == "mcp")
            return "mcp";
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
        if (m.StartsWith("gpt") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4")) return "openai";
        if (m.Contains("claude")) return "anthropic";
        if (m.Contains("gemini")) return "google";
        if (m.Contains("grok")) return "xai";
        if (m.Contains("deepseek")) return "deepseek";
        if (m.Contains("llama")) return "meta";
        if (m.Contains("mistral") || m.Contains("mixtral")) return "mistral";
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

    // gen_ai.input.messages / gen_ai.output.messages carry a chat-history array (see the
    // GenAI semconv). Exporters put that either as structured arrayValue/kvlistValue
    // attributes, or as a plain JSON-encoded string - support both and flatten it into
    // the same readable transcript text the old gen_ai.prompt/completion strings gave us.
    static string? RenderMessages(Dictionary<string, OtlpValue?> attr, string key)
    {
        if (!attr.TryGetValue(key, out var v) || v is null) return null;

        var node = ToNode(v);
        if (node is JsonValue str && str.TryGetValue<string>(out var raw))
        {
            try { node = JsonNode.Parse(raw); }
            catch { return raw; }
        }
        if (node is not JsonArray messages) return node?.ToJsonString();

        var lines = new List<string>();
        foreach (var msg in messages)
        {
            var role = msg?["role"]?.GetValue<string>() ?? "?";
            if (msg?["parts"] is not JsonArray parts)
            {
                lines.Add($"{role}: {msg?.ToJsonString()}");
                continue;
            }
            foreach (var part in parts)
            {
                var type = part?["type"]?.GetValue<string>();
                var text = type switch
                {
                    "text" => part?["content"]?.GetValue<string>(),
                    "tool_call" => $"call {part?["name"]?.GetValue<string>()}({part?["arguments"]?.ToJsonString()})",
                    "tool_call_response" => $"-> {part?["response"]?.ToJsonString()}",
                    _ => part?.ToJsonString(),
                };
                lines.Add($"{role}: {text}");
            }
        }
        return string.Join('\n', lines);
    }

    static JsonNode? ToNode(OtlpValue? v)
    {
        if (v is null) return null;
        if (v.StringValue is not null) return JsonValue.Create(v.StringValue);
        if (v.IntValue is not null) return long.TryParse(v.IntValue, out var n) ? JsonValue.Create(n) : null;
        if (v.DoubleValue is { } d) return JsonValue.Create(d);
        if (v.BoolValue is { } b) return JsonValue.Create(b);
        if (v.ArrayValue?.Values is { } items)
        {
            var arr = new JsonArray();
            foreach (var item in items) arr.Add(ToNode(item));
            return arr;
        }
        if (v.KvlistValue?.Values is { } fields)
        {
            var obj = new JsonObject();
            foreach (var f in fields) obj[f.Key] = ToNode(f.Value);
            return obj;
        }
        return null;
    }
}
