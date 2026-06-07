using Microsoft.Extensions.AI;
using Seerlens.Evals;

namespace Seerlens.Collector;

// Runs a golden set as an agent eval: the model is given the case's tools and left
// to call them, and the run is scored on whether it called the expected tools in
// order. Tools return canned results from the golden set, so a run is repeatable
// and never touches a real system. The loop is driven by hand (not auto-invocation)
// so the order of calls is captured exactly.
public sealed class AgentRunner(IChatClient client)
{
    const int MaxTurns = 8;

    public async Task<EvalRun> Run(GoldenSet set, string targetLabel, CancellationToken ct = default)
    {
        var results = new List<EvalCaseResult>(set.Cases.Count);
        foreach (var c in set.Cases)
            results.Add(await RunCase(c, ct));

        var mean = results.Count == 0 ? 0 : results.Average(r => r.Score);
        return new EvalRun(Guid.NewGuid().ToString("N"), set.Name, targetLabel, "agent-tools",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mean, results);
    }

    async Task<EvalCaseResult> RunCase(GoldenCase c, CancellationToken ct)
    {
        // dedupe by name so a duplicate in the set can't throw when building the tools
        var tools = (c.Tools ?? [])
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        var canned = tools.ToDictionary(t => t.Name, t => t.Result, StringComparer.OrdinalIgnoreCase);
        var options = new ChatOptions
        {
            ToolMode = ChatToolMode.Auto,
            Tools = tools.Select(t => (AITool)AIFunctionFactory.Create(() => t.Result, t.Name, t.Description)).ToList(),
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, c.Input) };
        var called = new List<string>();
        var answer = "";

        try
        {
            for (var turn = 0; turn < MaxTurns; turn++)
            {
                var resp = await client.GetResponseAsync(messages, options, ct);

                // keep the latest text, so a run that hits the turn cap still has an answer
                var text = string.Concat(resp.Messages.Select(m => m.Text)).Trim();
                if (text.Length > 0) answer = text;

                var calls = resp.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
                if (calls.Count == 0) break;

                messages.AddRange(resp.Messages);
                foreach (var call in calls)
                {
                    called.Add(call.Name);
                    var result = canned.GetValueOrDefault(call.Name, "");
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId ?? call.Name, result)]));
                }
            }
        }
        catch (Exception e)
        {
            // a bad tool definition or a provider that can't do tool calls shouldn't take down the run
            return new EvalCaseResult(c.Input, $"error: {e.Message}", 0);
        }

        var score = ToolSequence.Score(c.ExpectedTools ?? [], called).Score;
        return new EvalCaseResult(c.Input, answer, score);
    }
}
