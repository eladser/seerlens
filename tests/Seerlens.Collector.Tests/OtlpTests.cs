using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class OtlpTests
{
    // An OTLP/HTTP JSON export the way OpenLLMetry and friends send it: an LLM span
    // with GenAI attributes, plus a child tool span.
    const string Payload = """
    {
      "resourceSpans": [{
        "scopeSpans": [{
          "spans": [
            {
              "traceId": "abc123", "spanId": "s1", "parentSpanId": "",
              "name": "chat gpt-4o",
              "startTimeUnixNano": "1700000000000000000",
              "endTimeUnixNano":   "1700000000800000000",
              "attributes": [
                {"key": "gen_ai.system", "value": {"stringValue": "openai"}},
                {"key": "gen_ai.request.model", "value": {"stringValue": "gpt-4o"}},
                {"key": "gen_ai.usage.input_tokens", "value": {"intValue": "1000"}},
                {"key": "gen_ai.usage.output_tokens", "value": {"intValue": "500"}},
                {"key": "gen_ai.prompt", "value": {"stringValue": "user: where is my order"}},
                {"key": "gen_ai.completion", "value": {"stringValue": "it shipped"}}
              ],
              "status": {"code": 1}
            },
            {
              "traceId": "abc123", "spanId": "s2", "parentSpanId": "s1",
              "name": "lookupOrder",
              "startTimeUnixNano": "1700000000800000000",
              "endTimeUnixNano":   "1700000000950000000",
              "attributes": [{"key": "gen_ai.tool.name", "value": {"stringValue": "lookupOrder"}}],
              "status": {"code": 0}
            }
          ]
        }]
      }]
    }
    """;

    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Maps_genai_spans_into_a_trace()
    {
        var req = JsonSerializer.Deserialize<OtlpRequest>(Payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));

        Assert.Equal("abc123", trace.Id);
        Assert.Equal("chat gpt-4o", trace.Name);
        Assert.Equal("openai", trace.Provider);
        Assert.Equal("gpt-4o", trace.Model);
        Assert.Equal(950, trace.DurationMs);
        Assert.Equal(2, trace.Spans.Count);

        var llm = trace.Spans.Single(s => s.Kind == "llm");
        Assert.Equal(1000, llm.PromptTokens);
        Assert.Equal(500, llm.CompletionTokens);
        Assert.Equal("user: where is my order", llm.PromptText);
        Assert.Equal(800, llm.DurationMs);

        Assert.Single(trace.Spans, s => s.Kind == "tool" && s.Name == "lookupOrder");
    }

    [Fact]
    public void Maps_the_current_semconv_keys_alongside_the_old_ones()
    {
        const string payload = """
        {"resourceSpans":[{"scopeSpans":[{"spans":[
          {"traceId":"t2","spanId":"s","parentSpanId":"","name":"chat gpt-4o",
           "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000000800000000",
           "attributes":[
             {"key":"gen_ai.provider.name","value":{"stringValue":"openai"}},
             {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o"}},
             {"key":"gen_ai.usage.input_tokens","value":{"intValue":"1000"}},
             {"key":"gen_ai.usage.output_tokens","value":{"intValue":"500"}},
             {"key":"gen_ai.input.messages","value":{"stringValue":"[{\"role\":\"user\",\"parts\":[{\"type\":\"text\",\"content\":\"where is my order\"}]}]"}},
             {"key":"gen_ai.output.messages","value":{"stringValue":"[{\"role\":\"assistant\",\"parts\":[{\"type\":\"text\",\"content\":\"it shipped\"}]}]"}}
           ],"status":{"code":1}}
        ]}]}]}
        """;
        var req = JsonSerializer.Deserialize<OtlpRequest>(payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));
        Assert.Equal("openai", trace.Provider);
        Assert.Equal("gpt-4o", trace.Model);

        var span = Assert.Single(trace.Spans);
        Assert.Equal(1000, span.PromptTokens);
        Assert.Equal(500, span.CompletionTokens);
        Assert.Equal("user: where is my order", span.PromptText);
        Assert.Equal("assistant: it shipped", span.CompletionText);
    }

    [Fact]
    public void Reads_input_messages_sent_as_a_structured_array_value()
    {
        const string payload = """
        {"resourceSpans":[{"scopeSpans":[{"spans":[
          {"traceId":"t3","spanId":"s","parentSpanId":"","name":"chat",
           "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000000100000000",
           "attributes":[
             {"key":"gen_ai.provider.name","value":{"stringValue":"openai"}},
             {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o"}},
             {"key":"gen_ai.input.messages","value":{"arrayValue":{"values":[
               {"kvlistValue":{"values":[
                 {"key":"role","value":{"stringValue":"user"}},
                 {"key":"parts","value":{"arrayValue":{"values":[
                   {"kvlistValue":{"values":[
                     {"key":"type","value":{"stringValue":"text"}},
                     {"key":"content","value":{"stringValue":"weather in paris?"}}
                   ]}}
                 ]}}}
               ]}}
             ]}}}
           ],"status":{"code":1}}
        ]}]}]}
        """;
        var req = JsonSerializer.Deserialize<OtlpRequest>(payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));
        var span = Assert.Single(trace.Spans);
        Assert.Equal("user: weather in paris?", span.PromptText);
    }

    [Fact]
    public void Falls_back_to_the_inference_details_event_for_message_content()
    {
        const string payload = """
        {"resourceSpans":[{"scopeSpans":[{"spans":[
          {"traceId":"t4","spanId":"s","parentSpanId":"","name":"chat",
           "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000000100000000",
           "attributes":[
             {"key":"gen_ai.provider.name","value":{"stringValue":"openai"}},
             {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o"}}
           ],
           "events":[{"name":"gen_ai.client.inference.operation.details","attributes":[
             {"key":"gen_ai.input.messages","value":{"stringValue":"[{\"role\":\"user\",\"parts\":[{\"type\":\"text\",\"content\":\"hi\"}]}]"}},
             {"key":"gen_ai.output.messages","value":{"stringValue":"[{\"role\":\"assistant\",\"parts\":[{\"type\":\"text\",\"content\":\"hello\"}]}]"}}
           ]}],
           "status":{"code":1}}
        ]}]}]}
        """;
        var req = JsonSerializer.Deserialize<OtlpRequest>(payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));
        var span = Assert.Single(trace.Spans);
        Assert.Equal("user: hi", span.PromptText);
        Assert.Equal("assistant: hello", span.CompletionText);
    }

    [Fact]
    public void Maps_an_mcp_tool_call_with_its_name_and_io()
    {
        const string payload = """
        {"resourceSpans":[{"scopeSpans":[{"spans":[
          {"traceId":"t","spanId":"s","parentSpanId":"","name":"tools/call",
           "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000000100000000",
           "attributes":[
             {"key":"mcp.tool.name","value":{"stringValue":"search_docs"}},
             {"key":"mcp.request.params","value":{"stringValue":"{\"q\":\"refunds\"}"}},
             {"key":"mcp.response.result","value":{"stringValue":"3 hits"}}
           ],"status":{"code":1}}
        ]}]}]}
        """;
        var req = JsonSerializer.Deserialize<OtlpRequest>(payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));
        var span = Assert.Single(trace.Spans);
        Assert.Equal("mcp", span.Kind);
        Assert.Equal("search_docs", span.Name);
        Assert.Equal("{\"q\":\"refunds\"}", span.PromptText);
        Assert.Equal("3 hits", span.CompletionText);
    }

    [Fact]
    public void Duplicate_attribute_keys_do_not_crash_and_last_wins()
    {
        const string payload = """
        {"resourceSpans":[{"scopeSpans":[{"spans":[
          {"traceId":"t","spanId":"s","parentSpanId":"","name":"chat",
           "startTimeUnixNano":"1700000000000000000","endTimeUnixNano":"1700000000100000000",
           "attributes":[
             {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o-mini"}},
             {"key":"gen_ai.request.model","value":{"stringValue":"gpt-4o"}}
           ],"status":{"code":1}}
        ]}]}]}
        """;
        var req = JsonSerializer.Deserialize<OtlpRequest>(payload, Web)!;

        var trace = Assert.Single(Otlp.ToTraces(req));
        Assert.Equal("gpt-4o", trace.Model);
    }

    [Fact]
    public async Task Posting_to_v1_traces_stores_and_prices_the_trace()
    {
        using var factory = new Factory();
        var client = factory.CreateClient();

        var post = await client.PostAsync("/v1/traces",
            new StringContent(Payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var detail = await client.GetFromJsonAsync<TraceDetail>("/api/traces/abc123");
        Assert.NotNull(detail);
        // 1000/1M*2.50 + 500/1M*10 = 0.0075
        Assert.Equal(0.0075, detail!.Trace.CostUsd!.Value, 6);
    }

    [Fact]
    public async Task Posting_a_protobuf_payload_to_v1_traces_is_accepted()
    {
        using var factory = new Factory();
        var client = factory.CreateClient();

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString("00112233445566778899aabbccddeeff")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("0011223344556677")),
            Name = "chat gpt-4o",
            StartTimeUnixNano = 1700000000000000000UL,
            EndTimeUnixNano = 1700000000800000000UL,
            Status = new Status { Code = Status.Types.StatusCode.Ok },
        };
        span.Attributes.Add(new KeyValue { Key = "gen_ai.provider.name", Value = new AnyValue { StringValue = "openai" } });
        span.Attributes.Add(new KeyValue { Key = "gen_ai.request.model", Value = new AnyValue { StringValue = "gpt-4o" } });
        span.Attributes.Add(new KeyValue { Key = "gen_ai.usage.input_tokens", Value = new AnyValue { IntValue = 1000 } });
        span.Attributes.Add(new KeyValue { Key = "gen_ai.usage.output_tokens", Value = new AnyValue { IntValue = 500 } });

        var data = new TracesData();
        var rs = new ResourceSpans();
        var ss = new ScopeSpans();
        ss.Spans.Add(span);
        rs.ScopeSpans.Add(ss);
        data.ResourceSpans.Add(rs);

        var content = new ByteArrayContent(data.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var post = await client.PostAsync("/v1/traces", content);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var traceId = Convert.ToHexString(span.TraceId.Span).ToLowerInvariant();
        var detail = await client.GetFromJsonAsync<TraceDetail>($"/api/traces/{traceId}");
        Assert.NotNull(detail);
        Assert.Equal("gpt-4o", detail!.Trace.Model);
        Assert.Equal(0.0075, detail.Trace.CostUsd!.Value, 6);
    }

    [Fact]
    public async Task A_gzipped_protobuf_payload_is_accepted()
    {
        using var factory = new Factory();
        var client = factory.CreateClient();

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString("ffeeddccbbaa99887766554433221100")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("7766554433221100")),
            Name = "chat gpt-4o",
            StartTimeUnixNano = 1700000000000000000UL,
            EndTimeUnixNano = 1700000000500000000UL,
        };
        span.Attributes.Add(new KeyValue { Key = "gen_ai.request.model", Value = new AnyValue { StringValue = "gpt-4o" } });
        var ss = new ScopeSpans();
        ss.Spans.Add(span);
        var rs = new ResourceSpans();
        rs.ScopeSpans.Add(ss);
        var data = new TracesData();
        data.ResourceSpans.Add(rs);

        using var gz = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            data.WriteTo(z);
        var content = new ByteArrayContent(gz.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("gzip");

        var post = await client.PostAsync("/v1/traces", content);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var detail = await client.GetFromJsonAsync<TraceDetail>("/api/traces/ffeeddccbbaa99887766554433221100");
        Assert.Equal("gpt-4o", detail!.Trace.Model);
    }

    [Fact]
    public async Task A_malformed_payload_gets_400_not_500()
    {
        using var factory = new Factory();
        var client = factory.CreateClient();

        var content = new ByteArrayContent([0xff, 0xff, 0xff, 0x01]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/traces", content)).StatusCode);

        var bad = new StringContent("{not json", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/traces", bad)).StatusCode);
    }

    sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-otlp-{Guid.NewGuid():N}.db");
        readonly string _evalsDir = Path.Combine(Path.GetTempPath(), $"seerlens-otlp-{Guid.NewGuid():N}");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEERLENS_DB"] = _db,
                    ["SEERLENS_EVALS_DIR"] = _evalsDir,
                }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
            try { Directory.Delete(_evalsDir, true); } catch { }
        }
    }
}
