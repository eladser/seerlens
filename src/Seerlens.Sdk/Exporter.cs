using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace Seerlens.Sdk;

interface ITraceSink
{
    void Ship(TracePayload trace);
}

// Ships traces to the collector on a background loop. If the queue fills or the
// collector is down, traces are dropped. The host app is never blocked or thrown into.
sealed class Exporter : ITraceSink
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    readonly Uri _endpoint;
    readonly Channel<TracePayload> _queue = Channel.CreateBounded<TracePayload>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropWrite });

    public Exporter(string collectorUrl)
    {
        _endpoint = new Uri(new Uri(collectorUrl), "ingest");
        _ = Task.Run(Pump);
    }

    public void Ship(TracePayload trace) => _queue.Writer.TryWrite(trace);

    async Task Pump()
    {
        await foreach (var trace in _queue.Reader.ReadAllAsync())
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync(_endpoint, trace, Json);
            }
            catch
            {
                // collector unreachable, nothing we can do here, move on
            }
        }
    }
}
