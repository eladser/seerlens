namespace Seerlens.Sdk;

// Turns a finished trace into an OTLP/HTTP JSON export that follows the
// OpenTelemetry GenAI conventions, the same wire format the Python and JS SDKs
// use. A synthetic root span carries the trace name and parents the steps, so the
// dashboard keeps the name and shows the run as a tree.
static class OtlpExport
{
    public static object Build(TracePayload trace)
    {
        var root = new SpanOut(
            trace.Id, trace.Id, null, trace.Name,
            Nanos(trace.StartedAt), Nanos(trace.StartedAt, trace.DurationMs),
            [], 0);

        var spans = new List<object> { root.ToJson() };
        foreach (var s in trace.Spans)
            spans.Add(MapSpan(trace, s));

        return new
        {
            resourceSpans = new[]
            {
                new { scopeSpans = new[] { new { spans } } },
            },
        };
    }

    static object MapSpan(TracePayload trace, SpanPayload s)
    {
        var attrs = new List<object>();

        switch (s.Kind)
        {
            case "tool":
                Add(attrs, "gen_ai.operation.name", "execute_tool");
                Add(attrs, "gen_ai.tool.name", s.Name);
                Add(attrs, "gen_ai.tool.call.arguments", s.PromptText);
                Add(attrs, "gen_ai.tool.message", s.CompletionText);
                break;
            case "mcp":
                Add(attrs, "mcp.tool.name", s.Name);
                Add(attrs, "mcp.request.params", s.PromptText);
                Add(attrs, "mcp.response.result", s.CompletionText);
                break;
            default: // llm
                Add(attrs, "gen_ai.system", trace.Provider);
                Add(attrs, "gen_ai.request.model", s.Model);
                AddInt(attrs, "gen_ai.usage.input_tokens", s.PromptTokens);
                AddInt(attrs, "gen_ai.usage.output_tokens", s.CompletionTokens);
                Add(attrs, "gen_ai.prompt", s.PromptText);
                Add(attrs, "gen_ai.completion", s.CompletionText);
                break;
        }

        return new SpanOut(
            trace.Id, s.Id, s.ParentId ?? trace.Id, s.Name,
            Nanos(s.StartedAt), Nanos(s.StartedAt, s.DurationMs),
            attrs, s.Error is null ? 0 : 2, s.Error).ToJson();
    }

    static void Add(List<object> attrs, string key, string? value)
    {
        if (value is not null) attrs.Add(new { key, value = new { stringValue = value } });
    }

    static void AddInt(List<object> attrs, string key, long? value)
    {
        if (value is not null) attrs.Add(new { key, value = new { intValue = value.Value.ToString() } });
    }

    static string Nanos(long startedMs, double extraMs = 0) =>
        ((startedMs + (long)extraMs) * 1_000_000L).ToString();

    readonly record struct SpanOut(
        string TraceId, string SpanId, string? ParentSpanId, string Name,
        string Start, string End, List<object> Attributes, int StatusCode, string? StatusMessage = null)
    {
        public object ToJson() => new
        {
            traceId = TraceId,
            spanId = SpanId,
            parentSpanId = ParentSpanId,
            name = Name,
            startTimeUnixNano = Start,
            endTimeUnixNano = End,
            attributes = Attributes,
            status = StatusCode == 2 ? new { code = 2, message = StatusMessage ?? "error" } : null,
        };
    }
}
