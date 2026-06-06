using Microsoft.Extensions.AI;
using Seerlens.Collector;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class CompareTests
{
    // Always answers "42", with usage, so the keyword scorer and the cost/token
    // rollups have something real to chew on.
    sealed class FixedClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "the answer is 42"))
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Runs_the_set_across_each_model_with_score_and_tokens()
    {
        var ai = new AiProvider(new FixedClient(), "gpt-4o");
        var set = new GoldenSet("math", [new GoldenCase("c1", "What is 17 + 25?", ["42"])]);

        var result = await new Comparison(ai).Run(set, ["gpt-4o", "gpt-4o-mini"], "keyword", null);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal(1.0, r.Score));   // "42" is in the answer
        Assert.All(result.Rows, r => Assert.Equal(15, r.Tokens));
        Assert.Contains(result.Rows, r => r.Model == "gpt-4o-mini");
    }

    [Fact]
    public async Task Prices_the_run_when_the_model_is_known()
    {
        var ai = new AiProvider(new FixedClient(), "gpt-4o");
        var set = new GoldenSet("math", [new GoldenCase("c1", "What is 17 + 25?", ["42"])]);

        var result = await new Comparison(ai).Run(set, ["gpt-4o"], "keyword", null);

        var row = Assert.Single(result.Rows);
        Assert.NotNull(row.CostUsd);
        Assert.True(row.CostUsd > 0);
    }
}
