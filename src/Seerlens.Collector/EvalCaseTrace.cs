namespace Seerlens.Collector;

// Builds an in-memory trace from one stored eval case so it can be diffed like a real one.
public static class EvalCaseTrace
{
    const string ErrorPrefix = "error: ";

    public static TraceDetail? Build(EvalRunDetail run, int idx)
    {
        if (idx < 0 || idx >= run.Cases.Count) return null;
        var c = run.Cases[idx];
        var r = run.Run;
        var steps = c.Steps is { Count: > 0 } ? c.Steps : null;

        var spans = new List<SpanRow>();
        if (steps is null)
        {
            spans.Add(new SpanRow("s0", null, "chat", "llm", r.CreatedAt, 0, r.Target, null, null,
                null, "user: " + c.Input, c.Answer, null));
        }
        else
        {
            var lastLlm = -1;
            for (var i = 0; i < steps.Count; i++)
                if (Kind(steps[i]) == "llm") lastLlm = i;
            var firstLlm = steps.ToList().FindIndex(s => Kind(s) == "llm");

            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var kind = Kind(s);
                var model = s.Model ?? (kind == "llm" ? r.Target : null);
                var usage = new TokenUsage(s.InputTokens, s.OutputTokens, s.CacheReadTokens, null, s.ReasoningTokens);
                string? prompt = null, completion = null, error = null;
                if (kind == "llm")
                {
                    if (i == firstLlm) prompt = "user: " + c.Input;
                    if (i == lastLlm)
                    {
                        completion = c.Answer;
                        if (IsError(c)) error = c.Answer;
                    }
                }
                else prompt = s.Args;

                spans.Add(new SpanRow($"s{i}", null, s.Name ?? (kind == "llm" ? "chat" : kind), kind, s.StartedAt,
                    s.DurationMs, model, s.InputTokens, s.OutputTokens,
                    kind == "llm" ? Pricing.CostFor(model, usage) : null,
                    prompt, completion, error, s.CacheReadTokens, null, s.ReasoningTokens));
            }
        }

        var start = spans[0].StartedAt;
        var end = spans.Max(s => s.StartedAt + s.DurationMs);
        var llm = spans.Where(s => s.Kind == "llm").ToList();
        var name = $"{r.Set}: {c.CaseId ?? Clip(c.Input, 60)}";
        var summary = new TraceSummary(
            $"eval:{r.Id}:{idx}", name, start, Math.Max(0, end - start), null,
            llm.FirstOrDefault()?.Model ?? r.Target,
            IsError(c) ? "error" : "ok",
            llm.Any(s => s.PromptTokens is not null) ? llm.Sum(s => s.PromptTokens ?? 0) : null,
            llm.Any(s => s.CompletionTokens is not null) ? llm.Sum(s => s.CompletionTokens ?? 0) : null,
            spans.Any(s => s.CostUsd is not null) ? spans.Sum(s => s.CostUsd ?? 0) : null);
        return new TraceDetail(summary, spans);
    }

    static string Kind(Seerlens.Evals.EvalStep s) => s.Kind is "tool" or "mcp" ? s.Kind : "llm";

    static bool IsError(EvalCaseIn c) => c.Answer.StartsWith(ErrorPrefix, StringComparison.Ordinal);

    static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
}
