using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var startedAt = TraceBuilder.Now();
        var ts = Stopwatch.GetTimestamp();
        var updates = new List<ChatResponseUpdate>();
        Exception? error = null;

        var stream = base.GetStreamingResponseAsync(messages, options, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                try
                {
                    if (!await stream.MoveNextAsync()) break;
                }
                catch (Exception ex)
                {
                    error = ex;
                    break;
                }
                updates.Add(stream.Current);
                yield return stream.Current;
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        TryRecordStream(messages, options, updates, error, startedAt, ts);
        if (error is not null) ExceptionDispatchInfo.Throw(error);
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

            SeerlensTrace.Record(span);
        }
        catch
        {
            // recording must never affect the caller
        }
    }

    static void TryRecordStream(IEnumerable<ChatMessage> messages, ChatOptions? options,
        List<ChatResponseUpdate> updates, Exception? error, long startedAt, long ts)
    {
        try
        {
            var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
            var model = updates.Select(u => u.ModelId).FirstOrDefault(m => m is not null) ?? options?.ModelId;
            var usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().LastOrDefault()?.Details;
            var completion = string.Concat(updates.Select(u => u.Text));

            var span = new SpanPayload(
                Guid.NewGuid().ToString("N"), null,
                model is null ? "chat" : $"chat: {model}", "llm",
                startedAt, ms, model,
                usage?.InputTokenCount, usage?.OutputTokenCount,
                PromptText(messages),
                completion.Length == 0 ? null : completion,
                error?.Message);

            SeerlensTrace.Record(span);
        }
        catch
        {
            // recording must never affect the caller
        }
    }

    static string PromptText(IEnumerable<ChatMessage> messages) =>
        string.Join("\n", messages.Select(m => $"{m.Role.Value}: {m.Text}"));

    static string TextOf(IList<ChatMessage> messages) =>
        string.Join("\n", messages.Select(m => m.Text ?? ""));
}

public static class SeerlensChatClientExtensions
{
    public static IChatClient UseSeerlens(this IChatClient inner) => new SeerlensChatClient(inner);
}
