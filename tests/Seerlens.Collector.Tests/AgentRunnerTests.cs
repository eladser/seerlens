using Microsoft.Extensions.AI;
using Seerlens.Collector;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class AgentRunnerTests
{
    // A fake model that calls a fixed list of tools, one per turn, then answers.
    // It decides where it is by counting the tool results already in the conversation.
    sealed class ScriptedAgent(string[] toolCalls, string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
        {
            var done = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Count();
            if (done < toolCalls.Length)
            {
                var call = new FunctionCallContent($"c{done}", toolCalls[done]);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    static GoldenSet SetWith(string[] expected) => new("agent",
    [
        new GoldenCase("c1", "find the refund policy",
            Tools:
            [
                new AgentTool("search_docs", "search", "hits"),
                new AgentTool("read_file", "read", "30 days"),
            ],
            ExpectedTools: expected),
    ]);

    [Fact]
    public async Task Right_tools_in_order_score_full()
    {
        var agent = new ScriptedAgent(["search_docs", "read_file"], "Refunds are 30 days.");
        var run = await new AgentRunner(agent).Run(SetWith(["search_docs", "read_file"]), "test-model");

        Assert.Equal("agent-tools", run.Scorer);
        Assert.Equal(1.0, run.Score);
        Assert.Equal("Refunds are 30 days.", run.Cases[0].Answer);
    }

    [Fact]
    public async Task A_skipped_tool_drops_the_score()
    {
        // the agent only calls search_docs, but read_file was expected too
        var agent = new ScriptedAgent(["search_docs"], "done");
        var run = await new AgentRunner(agent).Run(SetWith(["search_docs", "read_file"]), "test-model");

        Assert.Equal(0.5, run.Score);
    }

    [Fact]
    public async Task A_model_that_never_stops_calling_tools_terminates_at_the_cap()
    {
        // 50 scripted calls, well past the 8-turn cap; the run must still return
        var agent = new ScriptedAgent(Enumerable.Repeat("search_docs", 50).ToArray(), "never reached");
        var run = await new AgentRunner(agent).Run(SetWith(["search_docs"]), "test-model");

        Assert.Equal(1.0, run.Score); // search_docs was called, in order
    }

    [Fact]
    public async Task Duplicate_tool_names_in_a_case_do_not_throw()
    {
        var agent = new ScriptedAgent([], "done"); // answers immediately
        var set = new GoldenSet("agent",
        [
            new GoldenCase("c1", "q",
                Tools: [new AgentTool("search", "s", "r1"), new AgentTool("search", "s", "r2")],
                ExpectedTools: []),
        ]);

        var run = await new AgentRunner(agent).Run(set, "test-model");

        Assert.Equal(1.0, run.Score); // no expected tools means nothing to miss, and no throw
    }
}
