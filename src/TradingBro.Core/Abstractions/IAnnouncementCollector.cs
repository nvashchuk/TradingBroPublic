using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public interface IAnnouncementCollector
{
    Exchange Exchange { get; }

    Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct);
}
