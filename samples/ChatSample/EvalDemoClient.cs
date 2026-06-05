using Microsoft.Extensions.AI;

namespace ChatSample;

// A stand-in support bot for the eval demo. The good version answers with the
// details the golden set checks for; the degraded version is vaguer, so the
// keyword score drops, the way a real quality regression would look.
sealed class EvalDemoClient(bool degraded, string model) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
    {
        await Task.Delay(40, ct);

        var q = string.Concat(messages.Select(m => m.Text)).ToLowerInvariant();
        var answer = q switch
        {
            _ when q.Contains("order") => degraded ? "It shipped recently." : "Your order shipped and arrives Thursday.",
            _ when q.Contains("refund") => degraded ? "Refunds are available." : "You can get a refund within 30 days.",
            _ when q.Contains("hours") => degraded ? "We're open on weekdays." : "Support is open 9 to 5 on weekdays.",
            _ => "Sorry, I'm not sure.",
        };

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = model };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
