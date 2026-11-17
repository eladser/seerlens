namespace Seerlens.Sdk;

// Wire format posted to the collector's /ingest endpoint.
record TracePayload(
    string Id,
    string Name,
    long StartedAt,
    double DurationMs,
    string? Provider,
    string? Model,
    string Status,
    List<SpanPayload> Spans);

record SpanPayload(
    string Id,
    string? ParentId,
    string Name,
    string Kind,
    long StartedAt,
    double DurationMs,
    string? Model,
    long? PromptTokens,
    long? CompletionTokens,
    string? PromptText,
    string? CompletionText,
    string? Error,
    long? CacheReadTokens = null,
    long? ReasoningTokens = null,
    IReadOnlyList<ChatTurn>? Input = null);

// One input message with its role kept apart, so the OTLP export can send them as a messages array.
record ChatTurn(string Role, string Text);
