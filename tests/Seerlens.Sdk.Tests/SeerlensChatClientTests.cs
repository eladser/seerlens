using System.Text.Json;
using Microsoft.Extensions.AI;
using Seerlens.Sdk;

namespace Seerlens.Sdk.Tests;

public class SeerlensChatClientTests
{
    [Fact]
    public async Task Records_a_trace_with_model_and_tokens()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        var client = new FakeChatClient("hi", "gpt-4o", 120, 30).UseSeerlens();
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        var trace = Assert.Single(sink.Traces);
        var span = Assert.Single(trace.Spans);
        Assert.Equal("gpt-4o", span.Model);
        Assert.Equal(120, span.PromptTokens);
        Assert.Equal(30, span.CompletionTokens);
        Assert.Equal("ok", trace.Status);
    }

    [Fact]
    public async Task Rethrows_inner_error_and_still_records_it()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        var client = new FakeChatClient(new TimeoutException("upstream down")).UseSeerlens();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        var trace = Assert.Single(sink.Traces);
        Assert.Equal("error", trace.Status);
        Assert.Equal("upstream down", trace.Spans[0].Error);
    }

    [Fact]
    public async Task A_failing_sink_does_not_break_the_call()
    {
        SeerlensTrace.UseSink(new ThrowingSink());

        var client = new FakeChatClient("hi", "gpt-4o", 1, 1).UseSeerlens();
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("hi", response.Text);
    }

    [Fact]
    public async Task BeginTrace_groups_calls_and_tools_into_one_trace()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        var client = new FakeChatClient("answer", "gpt-4o", 50, 20).UseSeerlens();

        using (SeerlensTrace.Begin("support question"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "where is my order")]);
            using (SeerlensTrace.Tool("lookupOrder")) { await Task.Delay(1); }
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "thanks")]);
        }

        var trace = Assert.Single(sink.Traces);
        Assert.Equal("support question", trace.Name);
        Assert.Equal(3, trace.Spans.Count);
        Assert.Equal(2, trace.Spans.Count(s => s.Kind == "llm"));
        Assert.Single(trace.Spans, s => s.Kind == "tool" && s.Name == "lookupOrder");
    }

    [Fact]
    public void Otlp_export_emits_root_genai_and_mcp_spans()
    {
        var trace = new TracePayload("t1", "research agent", 1000, 300, "openai", "gpt-4o", "ok",
        [
            new SpanPayload("s1", null, "chat", "llm", 1000, 100, "gpt-4o", 40, 12, "hi", "there", null),
            new SpanPayload("s2", null, "search_docs", "mcp", 1100, 80, null, null, null, "{\"q\":\"x\"}", "hit", null),
        ]);

        var json = JsonSerializer.Serialize(OtlpExport.Build(trace));

        // a root span carrying the trace name, plus the standard attributes
        Assert.Contains("\"research agent\"", json);
        Assert.Contains("gen_ai.request.model", json);
        Assert.Contains("gen_ai.usage.input_tokens", json);
        Assert.Contains("mcp.tool.name", json);
        Assert.Contains("\"parentSpanId\":\"t1\"", json); // steps nested under the root
    }

    [Fact]
    public async Task Mcp_call_records_kind_arguments_and_result()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        using (SeerlensTrace.Begin("research agent"))
        using (var t = SeerlensTrace.Mcp("search_docs", "{\"q\":\"refunds\"}"))
        {
            await Task.Delay(1);
            t.Complete("3 hits");
        }

        var trace = Assert.Single(sink.Traces);
        var span = Assert.Single(trace.Spans);
        Assert.Equal("mcp", span.Kind);
        Assert.Equal("search_docs", span.Name);
        Assert.Equal("{\"q\":\"refunds\"}", span.PromptText);
        Assert.Equal("3 hits", span.CompletionText);
    }

    [Fact]
    public async Task Streaming_passes_chunks_through_and_records_the_assembled_call()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        var client = new FakeStreamingChatClient("gpt-4o", 40, 12).UseSeerlens();

        var chunks = new List<string>();
        await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            chunks.Add(u.Text);

        Assert.Equal("Hello world", string.Concat(chunks));

        var span = Assert.Single(sink.Traces).Spans[0];
        Assert.Equal("gpt-4o", span.Model);
        Assert.Equal("Hello world", span.CompletionText);
        Assert.Equal(40, span.PromptTokens);
        Assert.Equal(12, span.CompletionTokens);
    }

    [Fact]
    public async Task Streaming_error_propagates_and_is_recorded()
    {
        var sink = new CapturingSink();
        SeerlensTrace.UseSink(sink);

        var client = new FakeStreamingChatClient("gpt-4o", 1, 1, throwMidway: true).UseSeerlens();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) { }
        });

        var trace = Assert.Single(sink.Traces);
        Assert.Equal("error", trace.Status);
        Assert.Equal("stream dropped", trace.Spans[0].Error);
    }
}
