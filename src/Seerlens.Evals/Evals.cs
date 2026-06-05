using System.Text.Json;

namespace Seerlens.Evals;

// One question in a golden set. Keywords drive the offline scorer; criteria is the
// rubric handed to an LLM judge when one is configured.
public record GoldenCase(string Id, string Input, string[]? Keywords = null, string? Criteria = null);

public record GoldenSet(string Name, IReadOnlyList<GoldenCase> Cases)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GoldenSet Load(string path) =>
        JsonSerializer.Deserialize<GoldenSet>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"empty or invalid golden set: {path}");
}

// What gets reported to the collector after a run. Score is the mean of the case scores.
public record EvalRun(
    string Id,
    string Set,
    string Target,
    string Scorer,
    long CreatedAt,
    double Score,
    IReadOnlyList<EvalCaseResult> Cases);

public record EvalCaseResult(string Input, string Answer, double Score);
