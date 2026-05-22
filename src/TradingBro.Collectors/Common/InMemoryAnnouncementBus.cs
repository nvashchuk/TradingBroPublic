using System.Collections.Concurrent;
using System.Threading.Channels;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Common;

/// Multi-cast in-memory bus. Each Subscribe() call eagerly creates a Channel
/// and returns its reader — so subscribers can register from their constructors
/// and be ready before any publisher fires.
public sealed class InMemoryAnnouncementBus : IAnnouncementBus
{
    private readonly ConcurrentDictionary<Guid, Channel<Announcement>> _subscribers = new();

    public ValueTask PublishAsync(Announcement announcement, CancellationToken ct = default)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(announcement);
        }
        return ValueTask.CompletedTask;
    }

    public ChannelReader<Announcement> Subscribe()
    {
        var channel = Channel.CreateBounded<Announcement>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _subscribers[Guid.NewGuid()] = channel;
        return channel.Reader;
    }
}
