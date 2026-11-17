using System.Text.Json;
using Microsoft.Extensions.AI;
using Seerlens.Sdk;

namespace Seerlens.Sdk.Tests;

[Collection("sink")]
public class CacheUsageTests
{
    sealed class UsageClient(UsageDetails usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { ModelId = "gpt-4o", Usage = usage });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok") { ModelId = "gpt-4o" };
            yield return new ChatResponseUpdate { ModelId = "gpt-4o", Contents = [new UsageContent(usage)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    static readonly UsageDetails Full = new()
    {
        InputTokenCount = 1200, OutputTokenCount = 300, CachedInputTokenCount = 900, ReasoningTokenCount = 120,
    };

    static async Task<SpanPayload> Call(UsageDetails usage, bool stream)
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);
        var client = new UsageClient(usage).UseSeerlens();
        var msgs = new[] { new ChatMessage(ChatRole.User, "hi") };

        if (stream)
            await foreach (var _ in client.GetStreamingResponseAsync(msgs)) { }
        else
            await client.GetResponseAsync(msgs);

        return Assert.Single(Assert.Single(sink.Traces).Spans);
    }

    static List<string> Keys(SpanPayload span)
    {
        var trace = new TracePayload("t", "t", 1000, 10, null, span.Model, "ok", [span]);
        var json = JsonSerializer.Serialize(OtlpExport.Build(trace));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("resourceSpans")[0].GetProperty("scopeSpans")[0].GetProperty("spans").EnumerateArray()
            .SelectMany(s => s.GetProperty("attributes").EnumerateArray())
            .Select(a => a.GetProperty("key").GetString()!).ToList();
    }

    static string IntOf(SpanPayload span, string key)
    {
        var trace = new TracePayload("t", "t", 1000, 10, null, span.Model, "ok", [span]);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(OtlpExport.Build(trace)));
        return doc.RootElement.GetProperty("resourceSpans")[0].GetProperty("scopeSpans")[0].GetProperty("spans").EnumerateArray()
            .SelectMany(s => s.GetProperty("attributes").EnumerateArray())
            .Single(a => a.GetProperty("key").GetString() == key).GetProperty("value").GetProperty("intValue").GetString()!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cache_and_reasoning_tokens_reach_the_span_and_the_otlp_export(bool stream)
    {
        var span = await Call(Full, stream);

        Assert.Equal(900, span.CacheReadTokens);
        Assert.Equal(120, span.ReasoningTokens);
        Assert.Equal("900", IntOf(span, "gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal("120", IntOf(span, "gen_ai.usage.reasoning.output_tokens"));
        Assert.Equal("1200", IntOf(span, "gen_ai.usage.input_tokens"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absent_usage_details_add_no_attributes(bool stream)
    {
        var span = await Call(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }, stream);

        Assert.Null(span.CacheReadTokens);
        Assert.Null(span.ReasoningTokens);
        var keys = Keys(span);
        Assert.DoesNotContain("gen_ai.usage.cache_read.input_tokens", keys);
        Assert.DoesNotContain("gen_ai.usage.reasoning.output_tokens", keys);
        Assert.Contains("gen_ai.usage.input_tokens", keys);
    }

    [Fact]
    public void The_internal_AddSpan_overload_carries_cache_and_reasoning()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        SeerlensTrace.AddSpan("fn", "llm", 5, "gpt-4o", 100, 20, "in", "out", null, 60, 8);

        var span = Assert.Single(Assert.Single(sink.Traces).Spans);
        Assert.Equal(60, span.CacheReadTokens);
        Assert.Equal(8, span.ReasoningTokens);
    }
}
