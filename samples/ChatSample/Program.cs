using ChatSample;
using Microsoft.Extensions.AI;
using Seerlens.Evals;
using Seerlens.Sdk;

var collector = Environment.GetEnvironmentVariable("SEERLENS_URL") ?? "http://localhost:5005";
SeerlensTrace.Configure(collector);

var client = new DemoChatClient().UseSeerlens();

// A few standalone calls across different models.
await Ask(client, "gpt-4o", "What's the capital of France?");
await Ask(client, "gpt-4o-mini", "Summarize the Q3 board update in one line.");
await Ask(client, "claude-sonnet-4-6", "Draft a reply to an unhappy customer.");
await Ask(client, "gpt-4o-mini", "Find docs about refund policy.");

// A streaming call. It's recorded as one span once the stream finishes.
await foreach (var _ in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "Stream a short greeting.")],
    new ChatOptions { ModelId = "gpt-4o" })) { }
Console.WriteLine("[gpt-4o] streamed a reply");

// A call that fails, so the error state shows up in the dashboard.
try
{
    await Ask(client, "gpt-4o", "This one will timeout on purpose.");
}
catch (TimeoutException)
{
    Console.WriteLine("[gpt-4o] timed out (recorded as a failed trace)");
}

// An agent-style interaction: two model calls with a tool lookup in between,
// grouped into a single trace.
using (SeerlensTrace.Begin("answer support ticket"))
{
    await Ask(client, "gpt-4o", "Where is my order #5521?");
    using (var t = SeerlensTrace.Tool("lookupOrder", "{\"id\":\"5521\"}"))
    {
        await Task.Delay(140);
        t.Complete("{\"status\":\"shipped\",\"eta\":\"Thursday\"}");
    }
    await Ask(client, "gpt-4o", "Order found, write the customer reply.");
}

// A research agent that reaches tools over MCP. Shows the step sequence and each
// call's arguments and result in the trace view.
using (SeerlensTrace.Begin("research agent: refund policy"))
{
    await Ask(client, "gpt-4o", "Find our refund policy and summarize it.");
    using (var t = SeerlensTrace.Mcp("search_docs", "{\"query\":\"refund policy\"}"))
    {
        await Task.Delay(120);
        t.Complete("3 matches: refunds.md, terms.md, faq.md");
    }
    using (var t = SeerlensTrace.Mcp("read_file", "{\"path\":\"refunds.md\"}"))
    {
        await Task.Delay(95);
        t.Complete("Refunds are accepted within 30 days of purchase.");
    }
    await Ask(client, "gpt-4o", "Summarize the refund policy in one sentence.");
}

// Evals: score a golden set a few times. The last run switches to a cheaper model
// that answers worse, so the trend shows the regression.
var golden = GoldenSet.Load(Path.Combine(AppContext.BaseDirectory, "evals", "support.json"));
var reporter = new EvalReporter(collector);
await RunEval("gpt-4o", degraded: false);
await Task.Delay(40);
await RunEval("gpt-4o", degraded: false);
await Task.Delay(40);
await RunEval("gpt-4o-mini", degraded: true);

Console.WriteLine($"Sent traces and eval runs to Seerlens. Open {collector} to see them.");

// Give the background exporter a moment to flush before the process exits.
await Task.Delay(500);

async Task RunEval(string target, bool degraded)
{
    var runner = new EvalRunner(new EvalDemoClient(degraded, target), new KeywordScorer());
    var run = await runner.Run(golden, target);
    await reporter.Report(run);
    Console.WriteLine($"[eval] {target} scored {run.Score:P0}");
}

static async Task Ask(IChatClient client, string model, string prompt)
{
    var options = new ChatOptions { ModelId = model };
    var reply = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], options);
    Console.WriteLine($"[{model}] {prompt} -> {reply.Text}");
}
