using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public interface IAnnouncementParser
{
    Exchange Exchange { get; }

    IReadOnlyList<Announcement> Parse(RawAnnouncement raw);
}
