using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Seerlens.Evals;

// Times one model call and the usage it reports, and shapes a tool call, as EvalSteps.
public static class StepRecorder
{
    public static async Task<(ChatResponse Resp, EvalStep Step)> Call(
        IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions? options,
        string fallbackModel, CancellationToken ct)
    {
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sw = Stopwatch.StartNew();
        var resp = await client.GetResponseAsync(messages, options, ct);
        sw.Stop();

        var u = resp.Usage;
        var step = new EvalStep("llm", null, resp.ModelId ?? fallbackModel, at, sw.Elapsed.TotalMilliseconds,
            u?.InputTokenCount, u?.OutputTokenCount, u?.CachedInputTokenCount, u?.ReasoningTokenCount, null);
        return (resp, step);
    }

    public static EvalStep Tool(FunctionCallContent call) =>
        new("tool", call.Name, null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0,
            null, null, null, null, JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>()));
}
