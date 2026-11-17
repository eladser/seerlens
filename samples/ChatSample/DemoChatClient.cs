using Microsoft.Extensions.AI;

namespace ChatSample;

// Stands in for a real provider so the sample runs without API keys.
// Adds a bit of latency and token usage so traces look like the real thing.
// A system prompt it has seen before counts as a cache hit, like provider prompt caching.
sealed class DemoChatClient : IChatClient
{
    static readonly string[] Replies =
    {
        "The capital of France is Paris.",
        "Here's a short summary: the quarterly numbers are up and churn is down.",
        "Your order #5521 shipped yesterday and arrives Thursday.",
        "Sure, I've drafted a friendly reply you can send to the customer.",
        "I found three matching results in the knowledge base.",
    };

    readonly Random _rng = new(7);
    readonly HashSet<string> _seen = [];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
    {
        await Task.Delay(_rng.Next(180, 850), ct);

        var prompt = string.Concat(messages.Select(m => m.Text));
        if (prompt.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            throw new TimeoutException("request to the model timed out after 30s");

        var reply = Replies[_rng.Next(Replies.Length)];
        var input = Math.Max(20, prompt.Length / 3);

        var system = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
        long? cached = system is not null && !_seen.Add(system) ? Math.Min(input, system.Length / 3) : null;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            ModelId = options?.ModelId ?? "gpt-4o",
            Usage = new UsageDetails
            {
                InputTokenCount = input,
                OutputTokenCount = Math.Max(15, reply.Length / 3),
                CachedInputTokenCount = cached,
            }
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options?.ModelId ?? "gpt-4o";
        var reply = Replies[_rng.Next(Replies.Length)];

        foreach (var word in reply.Split(' '))
        {
            await Task.Delay(40, ct);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { ModelId = model };
        }

        yield return new ChatResponseUpdate
        {
            ModelId = model,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 30, OutputTokenCount = reply.Length / 3 })],
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
