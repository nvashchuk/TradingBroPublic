namespace TradingBro.Core.Models;

public sealed record RawAnnouncement(
    Exchange Exchange,
    string ExternalId,
    string Title,
    string Url,
    DateTime AnnouncedAt,
    string? BodyText = null,
    IReadOnlyDictionary<string, string>? RawMetadata = null);
