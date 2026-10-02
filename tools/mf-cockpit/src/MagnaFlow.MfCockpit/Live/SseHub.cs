using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MagnaFlow.MfCockpit.Live;

/// <summary>project + kind only — no payloads over SSE (ontwerp-v0.1.md "Live updates"). Clients
/// re-fetch whatever page data corresponds to `kind`.</summary>
public sealed record LiveEvent(string Project, string Kind);

/// <summary>Fan-out for /api/events: each connected browser subscribes its own channel; a
/// FileSystemWatcher-driven ProjectWatcher broadcasts into all of them.</summary>
public sealed class SseHub
{
    private readonly ConcurrentDictionary<Guid, Channel<LiveEvent>> _subscribers = new();

    public (Guid Id, ChannelReader<LiveEvent> Reader) Subscribe()
    {
        var channel = Channel.CreateUnbounded<LiveEvent>(new UnboundedChannelOptions { SingleReader = true });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }

    public void Broadcast(LiveEvent evt)
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(evt);
    }
}
