using Microsoft.Extensions.AI;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class ScorerTests
{
    sealed class ReplyClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    static GoldenCase Case(string[]? patterns = null, string? schema = null, string[]? rubric = null) =>
        new("c1", "q", Patterns: patterns, Schema: schema, Rubric: rubric);

    // --- regex ---

    [Fact]
    public async Task Regex_scores_fraction_of_patterns_matched()
    {
        var c = Case(patterns: ["\\b42\\b", "missing"]);
        Assert.Equal(0.5, await new RegexScorer().Score(c, "the answer is 42"));
    }

    [Fact]
    public async Task Regex_invalid_pattern_counts_as_a_miss()
    {
        var c = Case(patterns: ["("]); // invalid regex must not throw
        Assert.Equal(0.0, await new RegexScorer().Score(c, "anything"));
    }

    // --- json-schema ---

    const string UserSchema =
        "{\"type\":\"object\",\"required\":[\"name\",\"age\"],\"properties\":{\"name\":{\"type\":\"string\"},\"age\":{\"type\":\"number\"}}}";

    [Fact]
    public async Task JsonSchema_passes_valid_and_pulls_json_from_a_fence()
    {
        var c = Case(schema: UserSchema);
        var fenced = "Sure:\n```json\n{\"name\":\"Ada\",\"age\":36}\n```";
        Assert.Equal(1.0, await new JsonSchemaScorer().Score(c, fenced));
    }

    [Fact]
    public async Task JsonSchema_fails_when_a_required_field_is_missing()
    {
        var c = Case(schema: UserSchema);
        Assert.Equal(0.0, await new JsonSchemaScorer().Score(c, "{\"name\":\"Ada\"}"));
    }

    [Fact]
    public async Task JsonSchema_fails_closed_on_non_json()
    {
        var c = Case(schema: UserSchema);
        Assert.Equal(0.0, await new JsonSchemaScorer().Score(c, "not json at all"));
    }

    [Fact]
    public async Task JsonSchema_extracts_the_object_even_with_a_stray_array_after_it()
    {
        var c = Case(schema: UserSchema);
        // a trailing "]" must not corrupt the slice taken for the leading "{...}"
        var answer = "Here: {\"name\":\"Ada\",\"age\":36} (an array would look like [1,2,3])";
        Assert.Equal(1.0, await new JsonSchemaScorer().Score(c, answer));
    }

    // --- rubric ---

    [Fact]
    public async Task Rubric_averages_per_criterion_scores()
    {
        var c = Case(rubric: ["names Paris", "one word"]);
        var judge = new ReplyClient("[1.0, 0.5]");
        Assert.Equal(0.75, await new RubricScorer(judge).Score(c, "Paris, the capital"));
    }

    [Fact]
    public async Task Rubric_recovers_numbers_when_the_judge_rambles()
    {
        var c = Case(rubric: ["a", "b"]);
        var judge = new ReplyClient("criterion one: 1.0, criterion two: 0.0");
        Assert.Equal(0.5, await new RubricScorer(judge).Score(c, "x"));
    }

    [Fact]
    public async Task Rubric_ignores_stray_integers_in_the_judges_prose()
    {
        var c = Case(rubric: ["a", "b"]);
        // "2" (the count) must not be read as a score; only 1.0 and 0.5 count
        var judge = new ReplyClient("Scoring 2 criteria: 1.0 and 0.5");
        Assert.Equal(0.75, await new RubricScorer(judge).Score(c, "x"));
    }
}
