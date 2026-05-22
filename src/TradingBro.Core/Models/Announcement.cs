namespace TradingBro.Core.Models;

public class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Exchange Exchange { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public Market Market { get; set; }

    public EventType EventType { get; set; }

    public DateTime AnnouncedAt { get; set; }

    public DateTime? EventAt { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? BodyText { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
