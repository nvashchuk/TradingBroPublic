using TradingBro.Core.Abstractions;

namespace TradingBro.Notifications.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; } = true;

    public string BotToken { get; set; } = string.Empty;

    /// Default chat list — receives every category unless overridden in Routing.
    public List<long> ChatIds { get; set; } = new();

    /// Optional per-category override. Map category → chat IDs.
    /// If a category is absent here, ChatIds (default) is used.
    /// Example:
    ///   "Routing": { "Errors": [ -100123 ], "Listings": [ -100456, -100789 ] }
    public Dictionary<NotificationCategory, List<long>> Routing { get; set; } = new();

    /// How many events can sit in the in-memory queue before back-pressure.
    public int QueueCapacity { get; set; } = 1000;

    /// Whether to consume command updates (/status, /recent, /mute…) from the bot.
    public bool CommandsEnabled { get; set; } = true;
}
