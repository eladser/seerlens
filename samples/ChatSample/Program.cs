using ChatSample;
using Microsoft.Extensions.AI;
using Seerlens.Sdk;

var collector = Environment.GetEnvironmentVariable("SEERLENS_URL") ?? "http://localhost:5005";
SeerlensTrace.Configure(collector);

var client = new DemoChatClient().UseSeerlens();

// A few standalone calls across different models.
await Ask(client, "gpt-4o", "What's the capital of France?");
await Ask(client, "gpt-4o-mini", "Summarize the Q3 board update in one line.");
await Ask(client, "claude-3-5-sonnet", "Draft a reply to an unhappy customer.");
await Ask(client, "gpt-4o-mini", "Find docs about refund policy.");

// An agent-style interaction: two model calls with a tool lookup in between,
// grouped into a single trace.
using (SeerlensTrace.Begin("answer support ticket"))
{
    await Ask(client, "gpt-4o", "Where is my order #5521?");
    using (SeerlensTrace.Tool("lookupOrder(#5521)"))
        await Task.Delay(140);
    await Ask(client, "gpt-4o", "Order found, write the customer reply.");
}

Console.WriteLine($"Sent traces to Seerlens. Open {collector} to see them.");

// Give the background exporter a moment to flush before the process exits.
await Task.Delay(500);

static async Task Ask(IChatClient client, string model, string prompt)
{
    var options = new ChatOptions { ModelId = model };
    var reply = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], options);
    Console.WriteLine($"[{model}] {prompt} -> {reply.Text}");
}
