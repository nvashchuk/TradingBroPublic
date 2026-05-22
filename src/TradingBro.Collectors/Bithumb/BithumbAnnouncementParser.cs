using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bithumb;

public sealed class BithumbAnnouncementParser : IAnnouncementParser
{
    public Exchange Exchange => Exchange.Bithumb;

    public IReadOnlyList<Announcement> Parse(RawAnnouncement raw)
    {
        var categories = (raw.RawMetadata?.GetValueOrDefault("categories") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        var classifications = BithumbAnnouncementClassifier.Classify(raw.Title, categories);
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
                    Exchange = Exchange.Bithumb,
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
