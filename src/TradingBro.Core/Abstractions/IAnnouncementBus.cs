using System.Threading.Channels;
using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public interface IAnnouncementBus
{
    ValueTask PublishAsync(Announcement announcement, CancellationToken ct = default);

    /// Registers a new subscriber channel synchronously and returns its reader.
    /// Call from the **constructor** of a hosted service so the channel is in
    /// place before any publishers fire — eliminates the start-up race window.
    /// Channels are kept for the lifetime of the bus (singleton); no Dispose.
    ChannelReader<Announcement> Subscribe();
}
