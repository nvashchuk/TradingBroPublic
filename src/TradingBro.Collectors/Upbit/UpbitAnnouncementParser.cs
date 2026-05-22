using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Upbit;

public sealed class UpbitAnnouncementParser : IAnnouncementParser
{
    public Exchange Exchange => Exchange.Upbit;

    public IReadOnlyList<Announcement> Parse(RawAnnouncement raw)
    {
        var classifications = UpbitAnnouncementClassifier.Classify(raw.Title);
        if (classifications.Count == 0)
        {
            return Array.Empty<Announcement>();
        }
        var symbols = SymbolExtraction.ExtractFromTitle(raw.Title);
        if (symbols.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        var output = new List<Announcement>(symbols.Count * classifications.Count);
        foreach (var symbol in symbols)
        {
            foreach (var (market, eventType) in classifications)
            {
                output.Add(new Announcement
                {
                    Id = Guid.NewGuid(),
                    Exchange = Exchange.Upbit,
                    Symbol = symbol,
                    Market = market,
                    EventType = eventType,
                    AnnouncedAt = raw.AnnouncedAt,
                    EventAt = null,
                    Title = raw.Title,
                    Url = raw.Url,
                    BodyText = raw.BodyText,
                    ExternalId = raw.ExternalId,
                });
            }
        }
        return output;
    }
}
