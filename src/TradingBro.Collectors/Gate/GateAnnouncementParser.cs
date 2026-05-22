using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Gate;

public sealed class GateAnnouncementParser : IAnnouncementParser
{
    public Exchange Exchange => Exchange.Gate;

    public IReadOnlyList<Announcement> Parse(RawAnnouncement raw)
    {
        // cate_id is the primary classification signal — mandatory.
        if (raw.RawMetadata?.TryGetValue("cate_id", out var cateIdStr) is not true
            || !int.TryParse(cateIdStr, out var cateId))
        {
            return Array.Empty<Announcement>();
        }

        var classifications = GateAnnouncementClassifier.Classify(raw.Title, cateId);
        if (classifications.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        var symbols = SymbolExtraction.ExtractFromTitle(raw.Title);
        if (symbols.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        // event_at_iso is the ISO-8601 string derived from release_timestamp by the collector.
        DateTime? eventAt = null;
        if (raw.RawMetadata?.TryGetValue("event_at_iso", out var eventAtIso) is true
            && DateTime.TryParse(
                eventAtIso,
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsedEventAt))
        {
            eventAt = parsedEventAt;
        }

        var output = new List<Announcement>(symbols.Count * classifications.Count);
        foreach (var symbol in symbols)
        {
            foreach (var (market, eventType) in classifications)
            {
                output.Add(new Announcement
                {
                    Id = Guid.NewGuid(),
                    Exchange = Exchange.Gate,
                    Symbol = symbol,
                    Market = market,
                    EventType = eventType,
                    AnnouncedAt = raw.AnnouncedAt,
                    EventAt = eventAt,
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
