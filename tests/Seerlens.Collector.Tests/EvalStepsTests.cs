using Microsoft.Extensions.AI;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class EvalStepsTests
{
    sealed class UsageClient : IChatClient
    {
        public int Calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
        {
            Calls++;
            var resp = new ChatResponse(new ChatMessage(ChatRole.Assistant, "forty two"))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 1000, OutputTokenCount = 200,
                    CachedInputTokenCount = 800, ReasoningTokenCount = 50,
                },
            };
            return Task.FromResult(resp);
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task EvalRunner_records_one_llm_step_per_case_with_usage()
    {
        var client = new UsageClient();
        var set = new GoldenSet("s", [new GoldenCase("c1", "meaning of life?", Keywords: ["forty"])]);

        var run = await new EvalRunner(client, new KeywordScorer()).Run(set, "gpt-4o");

        Assert.Equal(1, client.Calls);   // scoring made no extra model call
        var c = run.Cases[0];
        Assert.Equal("c1", c.CaseId);
        var step = Assert.Single(c.Steps!);
        Assert.Equal("llm", step.Kind);
        Assert.Equal("gpt-4o", step.Model);
        Assert.Equal(1000, step.InputTokens);
        Assert.Equal(200, step.OutputTokens);
        Assert.Equal(800, step.CacheReadTokens);
        Assert.Equal(50, step.ReasoningTokens);
        Assert.Null(step.Args);
        Assert.True(step.StartedAt > 0);
        Assert.True(step.DurationMs >= 0);
    }

    // calls the named tools one per turn, then answers, or throws on a given turn
    sealed class Scripted(string[] tools, int? throwOnTurn = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
        {
            var done = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Count();
            if (done == throwOnTurn) throw new InvalidOperationException("provider down");
            if (done < tools.Length)
            {
                var args = new Dictionary<string, object?> { ["q"] = $"arg{done}" };
                var call = new FunctionCallContent($"c{done}", tools[done], args);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]))
                    { Usage = new UsageDetails { InputTokenCount = 10 + done, OutputTokenCount = 5 } });
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                { Usage = new UsageDetails { InputTokenCount = 99, OutputTokenCount = 9 } });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    static GoldenSet AgentSet() => new("agent",
    [
        new GoldenCase("c1", "find it",
            Tools: [new AgentTool("search", "s", "hits"), new AgentTool("read", "r", "text")],
            ExpectedTools: ["search", "read"]),
    ]);

    [Fact]
    public async Task AgentRunner_records_llm_and_tool_steps_in_order()
    {
        var run = await new AgentRunner(new Scripted(["search", "read"])).Run(AgentSet(), "gpt-4o");

        var c = run.Cases[0];
        Assert.Equal("c1", c.CaseId);
        Assert.Equal(
            ["llm", "tool", "llm", "tool", "llm"],
            c.Steps!.Select(s => s.Kind).ToArray());
        Assert.Equal("search", c.Steps![1].Name);
        Assert.Equal("""{"q":"arg0"}""", c.Steps[1].Args);
        Assert.Equal("read", c.Steps[3].Name);
        Assert.Equal(99, c.Steps[4].InputTokens);
        Assert.All(c.Steps.Where(s => s.Kind == "tool"), s => Assert.Null(s.Model));
    }

    [Fact]
    public async Task AgentRunner_keeps_steps_when_a_turn_throws()
    {
        var run = await new AgentRunner(new Scripted(["search", "read"], throwOnTurn: 1)).Run(AgentSet(), "gpt-4o");

        var c = run.Cases[0];
        Assert.StartsWith("error: ", c.Answer);
        Assert.Equal(0, c.Score);
        Assert.Equal(["llm", "tool"], c.Steps!.Select(s => s.Kind).ToArray());
    }
}
