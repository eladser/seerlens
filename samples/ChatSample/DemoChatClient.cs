using Microsoft.Extensions.AI;

namespace ChatSample;

// Stands in for a real provider so the sample runs without API keys.
// Adds a bit of latency and token usage so traces look like the real thing.
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

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
    {
        await Task.Delay(_rng.Next(180, 850), ct);

        var prompt = string.Concat(messages.Select(m => m.Text));
        var reply = Replies[_rng.Next(Replies.Length)];

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            ModelId = options?.ModelId ?? "gpt-4o",
            Usage = new UsageDetails
            {
                InputTokenCount = Math.Max(20, prompt.Length / 3),
                OutputTokenCount = Math.Max(15, reply.Length / 3),
            }
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
