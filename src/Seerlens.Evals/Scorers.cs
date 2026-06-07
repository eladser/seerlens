using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;
using Microsoft.Extensions.AI;

namespace Seerlens.Evals;

// Scores an answer against a golden case, 0 (bad) to 1 (good).
public interface IScorer
{
    string Name { get; }
    Task<double> Score(GoldenCase c, string answer);
}

// Offline scorer: what fraction of the expected keywords show up in the answer.
// No API key needed, good enough to catch an answer that lost the important bits.
public sealed class KeywordScorer : IScorer
{
    public string Name => "keyword";

    public Task<double> Score(GoldenCase c, string answer)
    {
        var keywords = c.Keywords ?? [];
        if (keywords.Length == 0) return Task.FromResult(0.0);

        var hits = keywords.Count(k => answer.Contains(k, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult((double)hits / keywords.Length);
    }
}

// Asks a model to judge how well the answer meets the case's criteria. Use this when
// you have a provider configured and want real faithfulness/relevancy scoring.
public sealed class LlmJudgeScorer(IChatClient judge) : IScorer
{
    public string Name => "llm-judge";

    public async Task<double> Score(GoldenCase c, string answer)
    {
        var rubric = c.Criteria ?? "the answer is correct, relevant, and complete";
        var prompt =
            $"""
            Question: {c.Input}
            Answer: {answer}

            Rate from 0.0 to 1.0 how well the answer meets this: {rubric}
            Reply with only the number.
            """;

        var resp = await judge.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)]);
        var text = string.Concat(resp.Messages.Select(m => m.Text)).Trim();
        return Parse(text);
    }

    static double Parse(string text)
    {
        // pull the first number out of the reply
        var token = text.Split([' ', '\n', '\r', '\t', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 0, 1)
            : 0;
    }
}

// Offline scorer: the fraction of the case's regex patterns that match the answer.
// Patterns run case-insensitive with a timeout so a bad one can't hang the run.
public sealed class RegexScorer : IScorer
{
    public string Name => "regex";

    public Task<double> Score(GoldenCase c, string answer)
    {
        var patterns = c.Patterns ?? [];
        if (patterns.Length == 0) return Task.FromResult(0.0);

        var hits = patterns.Count(p => Matches(p, answer));
        return Task.FromResult((double)hits / patterns.Length);
    }

    static bool Matches(string pattern, string text)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
        catch { return false; } // a bad or runaway pattern counts as a miss, never throws
    }
}

// Offline scorer for structured output: 1 if the answer parses as JSON and validates
// against the case's JSON Schema, else 0. Fails closed on anything that isn't valid.
public sealed class JsonSchemaScorer : IScorer
{
    public string Name => "json-schema";

    public Task<double> Score(GoldenCase c, string answer)
    {
        if (string.IsNullOrWhiteSpace(c.Schema)) return Task.FromResult(0.0);
        try
        {
            // schemas are author-written; a pathological `pattern` keyword could be
            // slow to evaluate, but the input here is trusted (your own golden set).
            var schema = JsonSchema.FromText(c.Schema);
            using var doc = JsonDocument.Parse(ExtractJson(answer));
            return Task.FromResult(schema.Evaluate(doc.RootElement).IsValid ? 1.0 : 0.0);
        }
        catch
        {
            return Task.FromResult(0.0);
        }
    }

    // Models often wrap JSON in prose or a code fence; pull out the object or array.
    static string ExtractJson(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
            s = s.Trim();
        }
        var start = s.IndexOfAny(['{', '[']);
        if (start < 0) return s;
        // close with the matching bracket type, not whichever closer happens to be last
        var end = s.LastIndexOf(s[start] == '{' ? '}' : ']');
        return end > start ? s[start..(end + 1)] : s;
    }
}

// Asks the judge to score each criterion in the rubric on its own, then averages.
// Decomposing the verdict is more defensible than one holistic number. Falls back to
// the single Criteria, or a default, when no rubric is given.
public sealed class RubricScorer(IChatClient judge) : IScorer
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "rubric";

    public async Task<double> Score(GoldenCase c, string answer)
    {
        string[] criteria = c.Rubric is { Length: > 0 } r ? r
            : c.Criteria is { } one ? [one]
            : ["the answer is correct, relevant, and complete"];

        var numbered = string.Join("\n", criteria.Select((x, i) => $"{i + 1}. {x}"));
        var prompt =
            $"""
            Question: {c.Input}
            Answer: {answer}

            Score each criterion from 0.0 to 1.0:
            {numbered}

            Reply with only a JSON array of {criteria.Length} numbers, in order. Example: [1.0, 0.5]
            """;

        var resp = await judge.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)]);
        var text = string.Concat(resp.Messages.Select(m => m.Text)).Trim();
        var scores = ParseScores(text, criteria.Length);
        return scores.Count == 0 ? 0 : scores.Average();
    }

    static List<double> ParseScores(string text, int count)
    {
        try
        {
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start >= 0 && end > start
                && JsonSerializer.Deserialize<double[]>(text[start..(end + 1)], Json) is { Length: > 0 } arr)
                return arr.Select(n => Math.Clamp(n, 0, 1)).ToList();
        }
        catch { /* fall through to the loose parse */ }

        // loose fallback: keep only values that look like scores (0..1), so stray
        // integers in the judge's prose ("3 criteria: ...") don't get counted, and
        // never take more than the rubric has criteria.
        var nums = new List<double>();
        foreach (Match m in Regex.Matches(text, @"\d+(\.\d+)?"))
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 1)
                nums.Add(n);
        return nums.Count > count ? nums.Take(count).ToList() : nums;
    }
}
