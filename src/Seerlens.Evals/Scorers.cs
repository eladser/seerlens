using System.Globalization;
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
