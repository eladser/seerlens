using Microsoft.Extensions.AI;
using Seerlens.Sdk;

namespace Seerlens.Sdk.Tests;

sealed class CapturingSink : ITraceSink
{
    public List<TracePayload> Traces { get; } = new();
    public void Ship(TracePayload trace) => Traces.Add(trace);
}

sealed class ThrowingSink : ITraceSink
{
    public void Ship(TracePayload trace) => throw new InvalidOperationException("boom");
}

// Minimal IChatClient that returns a canned response, or throws.
sealed class FakeChatClient : IChatClient
{
    readonly Func<ChatResponse> _respond;

    public FakeChatClient(string reply, string model, long inTokens, long outTokens)
        => _respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            ModelId = model,
            Usage = new UsageDetails { InputTokenCount = inTokens, OutputTokenCount = outTokens }
        };

    public FakeChatClient(Exception throws) => _respond = () => throw throws;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(_respond());

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
