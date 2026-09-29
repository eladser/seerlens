using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Seerlens.Collector;

// Standard OTel exporters default to http/protobuf, not JSON. This turns a decoded
// TracesData message into the same OtlpRequest tree the JSON path builds, so
// Otlp.ToTraces doesn't need to know which wire format a request came in as.
public static class OtlpProto
{
    public static OtlpRequest ToRequest(TracesData data) => new(
        data.ResourceSpans.Select(rs => new OtlpResourceSpans(
            rs.ScopeSpans.Select(ss => new OtlpScopeSpans(
                ss.Spans.Select(MapSpan).ToList()
            )).ToList()
        )).ToList());

    static OtlpSpan MapSpan(Span s) => new(
        HexId(s.TraceId),
        HexId(s.SpanId),
        s.ParentSpanId.IsEmpty ? null : HexId(s.ParentSpanId),
        s.Name,
        s.StartTimeUnixNano.ToString(),
        s.EndTimeUnixNano.ToString(),
        s.Attributes.Select(MapAttr).ToList(),
        s.Status is null ? null : new OtlpStatus((int)s.Status.Code, s.Status.Message is { Length: > 0 } m ? m : null),
        s.Events.Select(e => new OtlpSpanEvent(e.Name, e.Attributes.Select(MapAttr).ToList())).ToList());

    static OtlpAttribute MapAttr(KeyValue kv) => new(kv.Key, MapValue(kv.Value));

    static OtlpValue? MapValue(AnyValue? v) => v?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => new OtlpValue(v.StringValue, null, null, null, null, null),
        AnyValue.ValueOneofCase.IntValue => new OtlpValue(null, v.IntValue.ToString(), null, null, null, null),
        AnyValue.ValueOneofCase.DoubleValue => new OtlpValue(null, null, v.DoubleValue, null, null, null),
        AnyValue.ValueOneofCase.BoolValue => new OtlpValue(null, null, null, v.BoolValue, null, null),
        AnyValue.ValueOneofCase.ArrayValue => new OtlpValue(null, null, null, null,
            new OtlpArrayValue(v.ArrayValue.Values.Select(MapValue).ToList()!), null),
        AnyValue.ValueOneofCase.KvlistValue => new OtlpValue(null, null, null, null, null,
            new OtlpKvlistValue(v.KvlistValue.Values.Select(MapAttr).ToList())),
        _ => null,
    };

    static string HexId(Google.Protobuf.ByteString bytes) =>
        Convert.ToHexString(bytes.Span).ToLowerInvariant();
}
