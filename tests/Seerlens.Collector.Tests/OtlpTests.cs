using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

    sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-otlp-{Guid.NewGuid():N}.db");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c =>
                c.AddInMemoryCollection(new Dictionary<string, string?> { ["SEERLENS_DB"] = _db }));
            return base.CreateHost(builder);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch { }
        }
    }
}
