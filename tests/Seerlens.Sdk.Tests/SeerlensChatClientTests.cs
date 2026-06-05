using Microsoft.Extensions.AI;
using Seerlens.Sdk;

namespace Seerlens.Sdk.Tests;

public class SeerlensChatClientTests
{
    [Fact]
    public async Task Records_a_trace_with_model_and_tokens()
    {
        var sink = new CapturingSink();
        Seerlens.UseSink(sink);

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
        Seerlens.UseSink(sink);

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
        Seerlens.UseSink(new ThrowingSink());

        var client = new FakeChatClient("hi", "gpt-4o", 1, 1).UseSeerlens();
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("hi", response.Text);
    }

    [Fact]
    public async Task BeginTrace_groups_calls_and_tools_into_one_trace()
    {
        var sink = new CapturingSink();
        Seerlens.UseSink(sink);

        var client = new FakeChatClient("answer", "gpt-4o", 50, 20).UseSeerlens();

        using (Seerlens.BeginTrace("support question"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "where is my order")]);
            using (Seerlens.Tool("lookupOrder")) { await Task.Delay(1); }
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "thanks")]);
        }

        var trace = Assert.Single(sink.Traces);
        Assert.Equal("support question", trace.Name);
        Assert.Equal(3, trace.Spans.Count);
        Assert.Equal(2, trace.Spans.Count(s => s.Kind == "llm"));
        Assert.Single(trace.Spans, s => s.Kind == "tool" && s.Name == "lookupOrder");
    }
}
