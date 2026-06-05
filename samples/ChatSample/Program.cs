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
await Ask(client, "claude-3-5-sonnet", "Draft a reply to an unhappy customer.");
await Ask(client, "gpt-4o-mini", "Find docs about refund policy.");

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
    using (SeerlensTrace.Tool("lookupOrder(#5521)"))
        await Task.Delay(140);
    await Ask(client, "gpt-4o", "Order found, write the customer reply.");
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
