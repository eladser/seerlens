using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Seerlens.Collector;

// Fan-out of new traces to any connected dashboard via server-sent events.
public sealed class LiveFeed
{
    readonly ConcurrentDictionary<Guid, Channel<TraceSummary>> _subs = new();

    public (Guid id, ChannelReader<TraceSummary> reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var ch = Channel.CreateBounded<TraceSummary>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        _subs[id] = ch;
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch))
            ch.Writer.TryComplete();
    }

    public void Publish(TraceSummary trace)
    {
        foreach (var ch in _subs.Values)
            ch.Writer.TryWrite(trace);
    }
}
