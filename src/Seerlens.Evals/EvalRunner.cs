using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Seerlens.Evals;

// Runs a golden set through a target model, scores each answer, and returns the run.
public sealed class EvalRunner(IChatClient target, IScorer scorer)
{
    public async Task<EvalRun> Run(GoldenSet set, string targetLabel, CancellationToken ct = default)
    {
        var results = new List<EvalCaseResult>(set.Cases.Count);
        foreach (var c in set.Cases)
        {
            var (resp, step) = await StepRecorder.Call(target, [new ChatMessage(ChatRole.User, c.Input)], null, targetLabel, ct);
            var answer = string.Concat(resp.Messages.Select(m => m.Text)).Trim();
            results.Add(new EvalCaseResult(c.Input, answer, await scorer.Score(c, answer), c.Id, [step]));
        }

        var mean = results.Count == 0 ? 0 : results.Average(r => r.Score);
        return new EvalRun(Guid.NewGuid().ToString("N"), set.Name, targetLabel, scorer.Name,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mean, results);
    }
}

// Sends a finished run to the collector so it shows up on the eval trend.
public sealed class EvalReporter(string collectorUrl)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly Uri _endpoint = new(new Uri(collectorUrl), "eval/runs");

    public async Task Report(EvalRun run)
    {
        using var resp = await _http.PostAsJsonAsync(_endpoint, run, Json);
        resp.EnsureSuccessStatusCode();
    }
}
