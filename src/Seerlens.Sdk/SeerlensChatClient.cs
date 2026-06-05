using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Seerlens.Sdk;

public sealed class SeerlensChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var startedAt = TraceBuilder.Now();
        var ts = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.GetResponseAsync(messages, options, ct);
            TryRecord(messages, options, response, null, startedAt, ts);
            return response;
        }
        catch (Exception ex)
        {
            TryRecord(messages, options, null, ex, startedAt, ts);
            throw;
        }
    }

    static void TryRecord(IEnumerable<ChatMessage> messages, ChatOptions? options,
        ChatResponse? response, Exception? error, long startedAt, long ts)
    {
        try
        {
            var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
            var model = response?.ModelId ?? options?.ModelId;

            var span = new SpanPayload(
                Guid.NewGuid().ToString("N"), null,
                model is null ? "chat" : $"chat: {model}", "llm",
                startedAt, ms, model,
                response?.Usage?.InputTokenCount,
                response?.Usage?.OutputTokenCount,
                PromptText(messages),
                response is null ? null : TextOf(response.Messages),
                error?.Message);

            Seerlens.Record(span);
        }
        catch
        {
            // recording must never affect the caller
        }
    }

    static string PromptText(IEnumerable<ChatMessage> messages) =>
        string.Join("\n", messages.Select(m => $"{m.Role.Value}: {m.Text}"));

    static string TextOf(IList<ChatMessage> messages) =>
        string.Join("\n", messages.Select(m => m.Text));
}

public static class SeerlensChatClientExtensions
{
    public static IChatClient UseSeerlens(this IChatClient inner) => new SeerlensChatClient(inner);
}
