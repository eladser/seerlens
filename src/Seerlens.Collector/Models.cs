namespace Seerlens.Collector;

// What an SDK posts to /ingest. One trace = one AI interaction.
public record IngestTrace(
    string Id,
    string Name,
    long StartedAt,            // unix ms
    double DurationMs,
    string? Provider,
    string? Model,
    string Status,             // "ok" | "error"
    IReadOnlyList<IngestSpan> Spans);

public record IngestSpan(
    string Id,
    string? ParentId,
    string Name,
    string Kind,               // "llm" | "tool" | "other"
    long StartedAt,
    double DurationMs,
    string? Model,
    long? PromptTokens,
    long? CompletionTokens,
    string? PromptText,
    string? CompletionText,
    string? Error);

// Row shown in the trace list. No big text fields so the list stays cheap.
public record TraceSummary(
    string Id,
    string Name,
    long StartedAt,
    double DurationMs,
    string? Provider,
    string? Model,
    string Status,
    long? PromptTokens,
    long? CompletionTokens,
    double? CostUsd);

public record TraceDetail(TraceSummary Trace, IReadOnlyList<SpanRow> Spans);

public record SpanRow(
    string Id,
    string? ParentId,
    string Name,
    string Kind,
    long StartedAt,
    double DurationMs,
    string? Model,
    long? PromptTokens,
    long? CompletionTokens,
    double? CostUsd,
    string? PromptText,
    string? CompletionText,
    string? Error);

public record Stats(int Traces, double TotalCostUsd, double AvgDurationMs);
