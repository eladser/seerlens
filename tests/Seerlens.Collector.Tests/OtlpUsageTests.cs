using System.Text.Json;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class OtlpUsageTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // an attribute value is an int, long, double, string or string[]
    static IngestSpan Map(bool proto, params (string Key, object Value)[] attrs) => Assert.Single(Assert.Single(
        Otlp.ToTraces(proto ? ViaProto(attrs) : ViaJson(attrs))).Spans);

    static object JsonValue(object v) => v switch
    {
        int or long => new { intValue = v },
        double => new { doubleValue = v },
        string[] a => new { arrayValue = new { values = a.Select(s => new { stringValue = s }).ToArray() } },
        _ => new { stringValue = (string)v },
    };

    static OtlpRequest ViaJson((string Key, object Value)[] attrs)
    {
        var body = new
        {
            resourceSpans = new[] { new { scopeSpans = new[] { new { spans = new[] { new {
                traceId = "t", spanId = "s", name = "chat",
                startTimeUnixNano = 1_700_000_000_000_000_000L, endTimeUnixNano = 1_700_000_000_500_000_000L,
                attributes = attrs.Select(a => new { key = a.Key, value = JsonValue(a.Value) }).ToArray(),
            } } } } } },
        };
        return JsonSerializer.Deserialize<OtlpRequest>(JsonSerializer.Serialize(body), Web)!;
    }

    static OtlpRequest ViaProto((string Key, object Value)[] attrs)
    {
        var span = new Span
        {
            TraceId = Google.Protobuf.ByteString.CopyFrom(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()),
            SpanId = Google.Protobuf.ByteString.CopyFrom(Enumerable.Range(1, 8).Select(i => (byte)i).ToArray()),
            Name = "chat",
            StartTimeUnixNano = 1_700_000_000_000_000_000UL,
            EndTimeUnixNano = 1_700_000_000_500_000_000UL,
        };
        foreach (var (k, v) in attrs)
        {
            var any = v switch
            {
                int i => new AnyValue { IntValue = i },
                long l => new AnyValue { IntValue = l },
                double d => new AnyValue { DoubleValue = d },
                string[] a => new AnyValue { ArrayValue = Arr(a) },
                _ => new AnyValue { StringValue = (string)v },
            };
            span.Attributes.Add(new KeyValue { Key = k, Value = any });
        }
        var scope = new ScopeSpans();
        scope.Spans.Add(span);
        var rs = new ResourceSpans();
        rs.ScopeSpans.Add(scope);
        var data = new TracesData();
        data.ResourceSpans.Add(rs);
        return OtlpProto.ToRequest(data);
    }

    static ArrayValue Arr(string[] items)
    {
        var a = new ArrayValue();
        foreach (var s in items) a.Values.Add(new AnyValue { StringValue = s });
        return a;
    }

    static readonly (string Key, object Value) Model = ("gen_ai.request.model", "gpt-4o");

    [Theory]
    [InlineData("gen_ai.usage.cache_read.input_tokens", false)]
    [InlineData("gen_ai.usage.cache_read.input_tokens", true)]
    [InlineData("gen_ai.usage.cache_read_input_tokens", false)]
    [InlineData("gen_ai.usage.cache_read_input_tokens", true)]
    [InlineData("ai.usage.inputTokenDetails.cacheReadTokens", false)]
    [InlineData("ai.usage.inputTokenDetails.cacheReadTokens", true)]
    [InlineData("ai.usage.cachedInputTokens", false)]
    [InlineData("ai.usage.cachedInputTokens", true)]
    public void Reads_every_cache_read_spelling(string key, bool proto)
    {
        var s = Map(proto, Model, (key, 640));
        Assert.Equal(640, s.CacheReadTokens);
        Assert.Null(s.CacheWriteTokens);
        Assert.Null(s.ReasoningTokens);
    }

    [Theory]
    [InlineData("gen_ai.usage.cache_write.input_tokens", false)]
    [InlineData("gen_ai.usage.cache_write.input_tokens", true)]
    [InlineData("gen_ai.usage.cache_creation.input_tokens", false)]
    [InlineData("gen_ai.usage.cache_creation.input_tokens", true)]
    [InlineData("gen_ai.usage.cache_creation_input_tokens", false)]
    [InlineData("gen_ai.usage.cache_creation_input_tokens", true)]
    [InlineData("ai.usage.inputTokenDetails.cacheWriteTokens", false)]
    [InlineData("ai.usage.inputTokenDetails.cacheWriteTokens", true)]
    public void Reads_every_cache_write_spelling(string key, bool proto)
    {
        var s = Map(proto, Model, (key, 128));
        Assert.Equal(128, s.CacheWriteTokens);
        Assert.Null(s.CacheReadTokens);
    }

    [Theory]
    [InlineData("gen_ai.usage.reasoning.output_tokens", false)]
    [InlineData("gen_ai.usage.reasoning.output_tokens", true)]
    [InlineData("gen_ai.usage.reasoning_tokens", false)]
    [InlineData("gen_ai.usage.reasoning_tokens", true)]
    [InlineData("llm.usage.reasoning_tokens", false)]
    [InlineData("llm.usage.reasoning_tokens", true)]
    [InlineData("ai.usage.outputTokenDetails.reasoningTokens", false)]
    [InlineData("ai.usage.outputTokenDetails.reasoningTokens", true)]
    [InlineData("ai.usage.reasoningTokens", false)]
    [InlineData("ai.usage.reasoningTokens", true)]
    public void Reads_every_reasoning_spelling(string key, bool proto)
    {
        var s = Map(proto, Model, (key, 300));
        Assert.Equal(300, s.ReasoningTokens);
        Assert.Null(s.CacheReadTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_first_listed_spelling_wins_whatever_the_attribute_order(bool proto)
    {
        var s = Map(proto, Model,
            ("gen_ai.usage.cache_read_input_tokens", 99),
            ("gen_ai.usage.cache_creation_input_tokens", 98),
            ("gen_ai.usage.reasoning_tokens", 97),
            ("gen_ai.usage.cache_read.input_tokens", 10),
            ("gen_ai.usage.cache_creation.input_tokens", 11),
            ("gen_ai.usage.reasoning.output_tokens", 12));

        Assert.Equal(10, s.CacheReadTokens);
        Assert.Equal(11, s.CacheWriteTokens);
        Assert.Equal(12, s.ReasoningTokens);
    }

    [Theory]
    [InlineData("invoke_agent")]
    [InlineData("invoke_workflow")]
    [InlineData("invoke_runner")]
    [InlineData("invoke_my_entrypoint")]
    [InlineData("create_agent")]
    public void Agent_operations_map_to_the_agent_kind(string op)
    {
        var s = Map(false, Model, ("gen_ai.operation.name", op), ("gen_ai.usage.input_tokens", 1000));
        Assert.Equal("agent", s.Kind);
        Assert.Equal(op, s.Operation);
    }

    [Theory]
    [InlineData("chat", "llm")]
    [InlineData("embeddings", "llm")]
    [InlineData("execute_tool", "tool")]
    public void Other_operations_keep_their_kind(string op, string kind)
    {
        var s = Map(true, Model, ("gen_ai.operation.name", op));
        Assert.Equal(kind, s.Kind);
        Assert.Equal(op, s.Operation);
    }

    [Fact]
    public void An_mcp_span_stays_mcp_even_with_an_invoke_operation()
    {
        var s = Map(false, ("mcp.method.name", "tools/call"), ("gen_ai.operation.name", "invoke_agent"));
        Assert.Equal("mcp", s.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collects_request_params_and_finish_reasons(bool proto)
    {
        var s = Map(proto, Model,
            ("gen_ai.request.temperature", 0.7),
            ("gen_ai.request.max_tokens", 256),
            ("gen_ai.request.stop_sequences", new[] { "END" }),
            ("gen_ai.response.finish_reasons", new[] { "stop" }),
            ("gen_ai.response.id", "resp-1"));

        Assert.Equal("""{"finish_reasons":["stop"],"max_tokens":256,"stop_sequences":["END"],"temperature":0.7}""", s.Params);
    }

    [Fact]
    public void Params_are_null_when_there_are_none()
    {
        Assert.Null(Map(false, Model).Params);
        Assert.Null(Map(true, Model).Params);
    }
}
