using System.Diagnostics;
using Microsoft.Extensions.AI;
using Seerlens.Evals;

namespace Seerlens.Collector;

// Runs one golden set through several models (and an optional prompt prefix) so you
// can put quality, cost and latency side by side before deciding what to ship.
// Cost and latency are why this lives in the collector and not the Evals lib.
public sealed class Comparison(AiProvider ai)
{
    public async Task<CompareResult> Run(GoldenSet set, IReadOnlyList<string> models, string scorerName, string? promptPrefix)
    {
        IScorer scorer = scorerName == "llm-judge" ? new LlmJudgeScorer(ai.Client!) : new KeywordScorer();
        var rows = new List<CompareRow>(models.Count);
        foreach (var model in models)
        {
            if (ai.ClientFor(model) is not { } client)
                continue;
            rows.Add(await RunOne(set, model, client, scorer, promptPrefix));
        }
        return new CompareResult(set.Name, scorerName, promptPrefix, rows);
    }

    static async Task<CompareRow> RunOne(GoldenSet set, string model, IChatClient client, IScorer scorer, string? prefix)
    {
        double scoreSum = 0;
        long inTokens = 0, outTokens = 0;
        double latencySum = 0;

        foreach (var c in set.Cases)
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(prefix))
                messages.Add(new ChatMessage(ChatRole.System, prefix));
            messages.Add(new ChatMessage(ChatRole.User, c.Input));

            var sw = Stopwatch.StartNew();
            var resp = await client.GetResponseAsync(messages);
            sw.Stop();

            var answer = string.Concat(resp.Messages.Select(m => m.Text)).Trim();
            scoreSum += await scorer.Score(c, answer);
            latencySum += sw.Elapsed.TotalMilliseconds;
            inTokens += resp.Usage?.InputTokenCount ?? 0;
            outTokens += resp.Usage?.OutputTokenCount ?? 0;
        }

        var n = Math.Max(set.Cases.Count, 1);
        return new CompareRow(
            model,
            scoreSum / n,
            Pricing.CostFor(model, inTokens, outTokens),
            latencySum / n,
            inTokens + outTokens);
    }
}

public record CompareRow(string Model, double Score, double? CostUsd, double AvgLatencyMs, long Tokens);

public record CompareResult(string Set, string Scorer, string? PromptPrefix, IReadOnlyList<CompareRow> Rows);
