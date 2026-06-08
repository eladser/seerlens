using System.ClientModel;
using Microsoft.SemanticKernel;
using OpenAI;
using Seerlens.SemanticKernel;

// Wire Semantic Kernel to any OpenAI-compatible provider, then add Seerlens with one
// line. After that, every kernel function runs through the tracing filter and shows
// up in the dashboard, no SeerlensTrace calls in this file.
var collector = Environment.GetEnvironmentVariable("SEERLENS_URL") ?? "http://localhost:5005";
var baseUrl = Environment.GetEnvironmentVariable("SEERLENS_AI_BASE_URL") ?? "https://api.groq.com/openai/v1";
var key = Environment.GetEnvironmentVariable("SEERLENS_AI_KEY") ?? "";
var model = Environment.GetEnvironmentVariable("SEERLENS_AI_MODEL") ?? "llama-3.3-70b-versatile";

var openai = new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion(model, openai);
builder.AddSeerlens(collector);   // <- that's the whole integration
var kernel = builder.Build();

// A prompt function (a model call).
var capital = kernel.CreateFunctionFromPrompt(
    "What is the capital of {{$country}}? Answer in one word.", functionName: "capitalLookup");
var answer = await kernel.InvokeAsync(capital, new() { ["country"] = "France" });
Console.WriteLine($"[prompt] {answer}");

// A native function (a tool call), invoked through the kernel so the filter sees it.
var clock = KernelFunctionFactory.CreateFromMethod(() => DateTime.UtcNow.ToString("u"), "utcNow");
var now = await kernel.InvokeAsync(clock);
Console.WriteLine($"[tool] {now}");

Console.WriteLine($"Sent Semantic Kernel traces to Seerlens. Open {collector}.");
await Task.Delay(600); // let the background exporter flush before exit
