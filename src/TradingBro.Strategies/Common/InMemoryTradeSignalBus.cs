using System.Collections.Concurrent;
using System.Threading.Channels;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Strategies.Common;

/// Same eager-init multi-cast pattern as InMemoryAnnouncementBus.
public sealed class InMemoryTradeSignalBus : ITradeSignalBus
{
    private readonly ConcurrentDictionary<Guid, Channel<TradeSignal>> _subscribers = new();

    public ValueTask PublishAsync(TradeSignal signal, CancellationToken ct = default)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(signal);
        }
        return ValueTask.CompletedTask;
    }

    public ChannelReader<TradeSignal> Subscribe()
    {
        var channel = Channel.CreateBounded<TradeSignal>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _subscribers[Guid.NewGuid()] = channel;
        return channel.Reader;
    }
}
