using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

    sealed class ReplyClient(string reply, long inTokens = 10) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                Usage = new UsageDetails { InputTokenCount = inTokens, OutputTokenCount = 5 },
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Prices_each_case_on_its_own_not_the_whole_set_as_one_call()
    {
        // haiku 5.5 doubles-up above 100K input; 20 cases of 6K are 120K together but 6K each
        var ai = new AiProvider(new ReplyClient("x", inTokens: 6_000), "claude-haiku-5-5");
        var cases = Enumerable.Range(1, 20).Select(i => new GoldenCase($"c{i}", "q", ["x"])).ToList();

        var result = await new Comparison(ai).Run(new GoldenSet("big", cases), ["claude-haiku-5-5"], "keyword", null);

        var cost = Assert.Single(result.Rows).CostUsd;
        Assert.Equal(20 * (6_000 * 0.10 + 5 * 0.50) / 1_000_000, cost!.Value, 9);
    }

    [Fact]
    public async Task Cost_is_null_when_no_case_could_be_priced()
    {
        var ai = new AiProvider(new ReplyClient("x"), "mystery-model");
        var set = new GoldenSet("s", [new GoldenCase("c1", "q", ["x"])]);

        var result = await new Comparison(ai).Run(set, ["mystery-model"], "keyword", null);

        Assert.Null(Assert.Single(result.Rows).CostUsd);
    }

    [Fact]
    public async Task Uses_the_requested_scorer_and_reports_it()
    {
        var ai = new AiProvider(new ReplyClient("[0.5]"), "gpt-4o");
        var set = new GoldenSet("s", [new GoldenCase("c1", "q", Rubric: ["be short"])]);

        var result = await new Comparison(ai).Run(set, ["gpt-4o"], "rubric", null);

        Assert.Equal("rubric", result.Scorer);
        Assert.Equal(0.5, Assert.Single(result.Rows).Score);   // keyword would have given 0
    }

    [Fact]
    public async Task The_embedding_scorer_without_an_embedder_throws_instead_of_falling_back()
    {
        var ai = new AiProvider(new ReplyClient("x"), "gpt-4o");
        var set = new GoldenSet("s", [new GoldenCase("c1", "q", Reference: "x")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new Comparison(ai).Run(set, ["gpt-4o"], "embedding", null));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("agent")]
    [InlineData("embedding")]
    public async Task The_compare_endpoint_gets_400_for_unusable_scorers(string scorer)
    {
        using var factory = new Factory();
        var client = factory.CreateClient();
        var name = (await client.GetFromJsonAsync<JsonElement>("/api/sets")).GetProperty("sets")[0].GetString();

        var resp = await client.PostAsJsonAsync("/eval/compare", new { set = name, scorer });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), $"seerlens-cmp-{Guid.NewGuid():N}");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _dir + ".db",
                    ["SEERLENS_EVALS_DIR"] = _dir,
                }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<AiProvider>();
                s.AddSingleton(new AiProvider(new ReplyClient("x"), "gpt-4o"));
            });
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_dir, true); File.Delete(_dir + ".db"); } catch { }
        }
    }
}
