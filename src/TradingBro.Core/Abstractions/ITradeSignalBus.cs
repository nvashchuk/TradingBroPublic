using System.Threading.Channels;
using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public interface ITradeSignalBus
{
    ValueTask PublishAsync(TradeSignal signal, CancellationToken ct = default);

    /// Registers a new subscriber channel synchronously and returns its reader.
    /// See IAnnouncementBus.Subscribe for the rationale — same pattern.
    ChannelReader<TradeSignal> Subscribe();
}
