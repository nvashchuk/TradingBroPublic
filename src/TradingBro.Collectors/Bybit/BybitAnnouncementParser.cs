using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bybit;

public sealed class BybitAnnouncementParser : IAnnouncementParser
{
    public Exchange Exchange => Exchange.Bybit;

    public IReadOnlyList<Announcement> Parse(RawAnnouncement raw)
    {
        var apiType = raw.RawMetadata?.GetValueOrDefault("api_type") ?? string.Empty;
        var tags = (raw.RawMetadata?.GetValueOrDefault("tags") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        var classifications = BybitAnnouncementClassifier.Classify(raw.Title, apiType, tags);
        if (classifications.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        var symbols = SymbolExtraction.ExtractFromTitle(raw.Title);
        if (symbols.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        DateTime? eventAt = null;
        if (raw.RawMetadata?.TryGetValue("event_at", out var eventAtStr) is true
            && DateTime.TryParse(eventAtStr, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            eventAt = parsed;
        }

        var output = new List<Announcement>(symbols.Count * classifications.Count);
        foreach (var symbol in symbols)
        {
            foreach (var (market, eventType) in classifications)
            {
                output.Add(new Announcement
                {
                    Id = Guid.NewGuid(),
                    Exchange = Exchange.Bybit,
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
