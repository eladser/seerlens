using System.Text;
using System.Text.Json;

namespace Seerlens.Collector;

public record StepView(
    string SpanId,
    string? Model,
    string? Operation,
    double OffsetMs,
    double DurationMs,
    long? PromptTokens,
    long? CompletionTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens,
    long? ReasoningTokens,
    double? CostUsd,
    string? Error);

// A and B are the canonical JSON text of the value, null when that side has no such key.
public record ParamChange(string Key, string? A, string? B);

// Status: same | changed | removed | added. Changes: model, params, finish, input, output, error.
// Input and Output are only filled on the first changed row; the rest come from /api/diff/step.
public record DiffStep(
    string Status,
    string Kind,
    string Name,
    StepView? A,
    StepView? B,
    IReadOnlyList<string> Changes,
    IReadOnlyList<ParamChange> Params,
    TextDiffResult? Input = null,
    TextDiffResult? Output = null);

public record RunTotals(
    double DurationMs,
    double? CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long ReasoningTokens,
    double? CacheHitRate,
    int Steps,
    int ToolCalls,
    int Errors,
    IReadOnlyList<string> Models);

public record DiffResponse(
    TraceSummary A,
    TraceSummary B,
    RunTotals TotalsA,
    RunTotals TotalsB,
    IReadOnlyList<string> ToolsA,
    IReadOnlyList<string> ToolsB,
    bool Truncated,
    IReadOnlyList<DiffStep> Steps);

public record StepDiffResponse(TextDiffResult Input, TextDiffResult Output);

// Each side is cut at TextDiff.MaxChars; Truncated says either one was.
public record StepRawResponse(string? Input, string? Output, bool Truncated = false)
{
    public static StepRawResponse Of(string? input, string? output)
    {
        var cut = (input?.Length ?? 0) > TextDiff.MaxChars || (output?.Length ?? 0) > TextDiff.MaxChars;
        return cut
            ? new(Clip(input), Clip(output), true)
            : new(input, output);
    }

    static string? Clip(string? s) => s is { Length: > TextDiff.MaxChars } ? s[..TextDiff.MaxChars] : s;
}

public static class RunDiff
{
    public const int MaxSteps = 2000;
    const long GroupWindowMs = 10;
    const string Finish = "finish_reasons";

    public static DiffResponse Compare(TraceDetail a, TraceDetail b)
    {
        var sa = Order(a);
        var sb = Order(b);
        var truncated = sa.Count > MaxSteps || sb.Count > MaxSteps;
        var ca = sa.Count > MaxSteps ? sa.GetRange(0, MaxSteps) : sa;
        var cb = sb.Count > MaxSteps ? sb.GetRange(0, MaxSteps) : sb;

        var rows = new List<DiffStep>();
        var inlined = false;
        foreach (var (x, y) in Align(ca, cb))
        {
            var row = Row(x, y, a.Trace.StartedAt, b.Trace.StartedAt);
            if (!inlined && row.Status == "changed")
            {
                inlined = true;
                row = row with
                {
                    Input = TextDiff.Diff(x!.PromptText, y!.PromptText),
                    Output = TextDiff.Diff(x.CompletionText, y.CompletionText),
                };
            }
            rows.Add(row);
        }

        return new DiffResponse(a.Trace, b.Trace, Totals(a, sa), Totals(b, sb), Tools(ca), Tools(cb), truncated, rows);
    }

    public static bool IsStep(SpanRow s) => s.Kind is "llm" or "tool" or "mcp";

    public static string Key(SpanRow s) => s.Kind switch
    {
        "tool" => "tool:" + s.Name.ToLowerInvariant(),
        "mcp" => "mcp:" + s.Name.ToLowerInvariant(),
        _ => s.Operation == "embeddings" ? "llm:embeddings" : "llm",
    };

    // startedAt order, then parallel fan-out groups anchored at their first step.
    public static List<SpanRow> Order(TraceDetail d)
    {
        if (d.InOrder) return d.Spans.Where(IsStep).ToList();
        var sorted = d.Spans.Where(IsStep).OrderBy(s => s.StartedAt).ToList();
        var used = new bool[sorted.Count];
        var res = new List<SpanRow>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var anchor = sorted[i];
            var group = new List<SpanRow> { anchor };
            for (var j = i + 1; j < sorted.Count && sorted[j].StartedAt - anchor.StartedAt <= GroupWindowMs; j++)
            {
                if (used[j] || sorted[j].ParentId != anchor.ParentId) continue;
                used[j] = true;
                group.Add(sorted[j]);
            }
            if (group.Count > 1)
                group = group.OrderBy(Key, StringComparer.Ordinal).ThenBy(s => s.PromptText ?? "", StringComparer.Ordinal).ToList();
            res.AddRange(group);
        }
        return res;
    }

    static List<(SpanRow? A, SpanRow? B)> Align(List<SpanRow> a, List<SpanRow> b)
    {
        int n = a.Count, m = b.Count, w = n + m + 1, cols = m + 1;
        var ka = a.Select(Key).ToArray();
        var kb = b.Select(Key).ToArray();
        var ia = a.Select(s => s.PromptText ?? "").ToArray();
        var ib = b.Select(s => s.PromptText ?? "").ToArray();
        var ha = ia.Select(s => s.GetHashCode()).ToArray();
        var hb = ib.Select(s => s.GetHashCode()).ToArray();

        int Gain(int i, int j) => w + (ha[i] == hb[j] && string.Equals(ia[i], ib[j], StringComparison.Ordinal) ? 1 : 0);

        var f = new int[(n + 1) * cols];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
            {
                var best = Math.Max(f[(i + 1) * cols + j], f[i * cols + j + 1]);
                if (ka[i] == kb[j])
                    best = Math.Max(best, f[(i + 1) * cols + j + 1] + Gain(i, j));
                f[i * cols + j] = best;
            }

        var res = new List<(SpanRow?, SpanRow?)>(Math.Max(n, m));
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && ka[x] == kb[y] && f[x * cols + y] == f[(x + 1) * cols + y + 1] + Gain(x, y))
                res.Add((a[x++], b[y++]));
            else if (x < n && f[x * cols + y] == f[(x + 1) * cols + y])
                res.Add((a[x++], null));
            else
                res.Add((null, b[y++]));
        }
        return res;
    }

    static DiffStep Row(SpanRow? x, SpanRow? y, long startA, long startB)
    {
        if (x is null) return new DiffStep("added", y!.Kind, y.Name, null, View(y, startB), [], []);
        if (y is null) return new DiffStep("removed", x.Kind, x.Name, View(x, startA), null, [], []);

        var changes = new List<string>();
        var pars = new List<ParamChange>();
        if (!string.Equals(x.Model ?? "", y.Model ?? "", StringComparison.OrdinalIgnoreCase)) changes.Add("model");

        var pa = ParseParams(x.Params);
        var pb = ParseParams(y.Params);
        var paramsChanged = false;
        var finishChanged = false;
        foreach (var k in pa.Keys.Union(pb.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            pa.TryGetValue(k, out var va);
            pb.TryGetValue(k, out var vb);
            if (va == vb) continue;
            pars.Add(new ParamChange(k, va, vb));
            if (k == Finish) finishChanged = true;
            else paramsChanged = true;
        }
        if (paramsChanged) changes.Add("params");
        if (finishChanged) changes.Add("finish");

        if (!string.Equals(x.PromptText ?? "", y.PromptText ?? "", StringComparison.Ordinal)) changes.Add("input");
        if (!string.Equals(x.CompletionText ?? "", y.CompletionText ?? "", StringComparison.Ordinal)) changes.Add("output");
        if (!string.Equals(x.Error, y.Error, StringComparison.Ordinal)) changes.Add("error");

        var name = changes.Contains("model") ? WithoutModel(x) : x.Name;
        return new DiffStep(changes.Count == 0 ? "same" : "changed", x.Kind, name, View(x, startA), View(y, startB), changes, pars);
    }

    // Semconv names a call "{operation} {model}" (the .NET SDK "chat: {model}"), so after a
    // model swap A's name would repeat A's model next to the "a → b" change.
    static string WithoutModel(SpanRow s)
    {
        if (s.Model is { Length: > 0 } m)
        {
            var i = s.Name.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                var rest = (s.Name[..i] + s.Name[(i + m.Length)..]).Trim(' ', ':');
                return rest.Length > 0 ? rest : s.Operation ?? s.Kind;
            }
        }
        // the name has the request model, the span the response model (gpt-4o vs gpt-4o-2024-08-06)
        if (s.Operation is { Length: > 0 } op && s.Name.StartsWith(op + " ", StringComparison.OrdinalIgnoreCase))
            return op;
        return s.Name;
    }

    static StepView View(SpanRow s, long start) => new(
        s.Id, s.Model, s.Operation, s.StartedAt - start, s.DurationMs, s.PromptTokens, s.CompletionTokens,
        s.CacheReadTokens, s.CacheWriteTokens, s.ReasoningTokens, s.CostUsd, s.Error);

    static List<string> Tools(List<SpanRow> steps) =>
        steps.Where(s => s.Kind is "tool" or "mcp").Select(s => s.Name).ToList();

    // Same rule as TraceStore.Add: an agent span's usage is the sum of the calls under it,
    // so it only counts when no llm span sits below it.
    static List<SpanRow> LoneAgents(TraceDetail d)
    {
        var byId = new Dictionary<string, SpanRow>();
        foreach (var s in d.Spans) byId.TryAdd(s.Id, s);
        var above = new HashSet<string>();
        foreach (var s in d.Spans.Where(s => s.Kind == "llm"))
        {
            var p = s.ParentId;
            while (p is not null && above.Add(p) && byId.TryGetValue(p, out var up)) p = up.ParentId;
        }
        return d.Spans.Where(s => s.Kind == "agent" && !above.Contains(s.Id)).ToList();
    }

    static RunTotals Totals(TraceDetail d, List<SpanRow> steps)
    {
        var llm = steps.Where(s => s.Kind == "llm").ToList();
        var used = llm.Concat(LoneAgents(d)).ToList();
        var costs = steps.Concat(used.Where(s => s.Kind == "agent"));
        double? cost = costs.Any(s => s.CostUsd is not null) ? costs.Sum(s => s.CostUsd ?? 0) : null;

        double hit = 0, denom = 0;
        foreach (var s in used.Where(s => s.CacheReadTokens is not null))
        {
            long i = s.PromptTokens ?? 0, cr = s.CacheReadTokens ?? 0, cw = s.CacheWriteTokens ?? 0;
            hit += cr;
            denom += cr + cw > i ? i + cr + cw : i;
        }

        return new RunTotals(
            d.Trace.DurationMs, cost,
            used.Sum(s => s.PromptTokens ?? 0), used.Sum(s => s.CompletionTokens ?? 0),
            used.Sum(s => s.CacheReadTokens ?? 0), used.Sum(s => s.CacheWriteTokens ?? 0), used.Sum(s => s.ReasoningTokens ?? 0),
            denom > 0 ? hit / denom : null,
            steps.Count, steps.Count(s => s.Kind is "tool" or "mcp"), steps.Count(s => s.Error is not null),
            llm.Select(s => s.Model).Where(m => !string.IsNullOrEmpty(m)).Select(m => m!).Distinct().ToList());
    }

    static Dictionary<string, string> ParseParams(string? json)
    {
        var res = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return res;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return res;
            foreach (var p in doc.RootElement.EnumerateObject())
                res[p.Name] = Canonical(p.Value);
        }
        catch (JsonException) { }
        return res;
    }

    static string Canonical(JsonElement e)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
            WriteCanonical(w, e);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static void WriteCanonical(Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    w.WritePropertyName(p.Name);
                    WriteCanonical(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var v in e.EnumerateArray()) WriteCanonical(w, v);
                w.WriteEndArray();
                break;
            case JsonValueKind.Number when e.TryGetDecimal(out var d):
                // 0.70 and 0.7 are the same value
                w.WriteNumberValue(d / 1.0000000000000000000000000000m);
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }
}
