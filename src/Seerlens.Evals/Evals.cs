using System.Text.Json;

namespace Seerlens.Evals;

// A tool the agent eval offers the model. Result is the canned value returned when
// the model calls it, so a run is deterministic without hitting real systems.
public record AgentTool(string Name, string Description, string Result);

// One question in a golden set. Keywords drive the offline scorer; criteria is the
// rubric handed to an LLM judge. Tools and ExpectedTools drive the agent eval: the
// model gets Tools to call, and the run is scored on whether it called ExpectedTools
// in order.
public record GoldenCase(
    string Id,
    string Input,
    string[]? Keywords = null,
    string? Criteria = null,
    AgentTool[]? Tools = null,
    string[]? ExpectedTools = null,
    string[]? Rubric = null,      // criteria scored one by one by the rubric judge
    string[]? Patterns = null,    // regex patterns a good answer must match
    string? Schema = null,        // a JSON Schema the answer (as JSON) must validate against
    string? Reference = null);    // gold answer the embedding scorer compares against

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
