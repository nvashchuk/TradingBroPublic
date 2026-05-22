using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public enum NotificationCategory
{
    System = 0,
    Listings = 1,
    Strategies = 2,
    Positions = 3,
    Errors = 4,
}

/// Base for everything sent through the notification pipeline.
/// New event types: add a sealed record below + a Format case
/// in TelegramMessageFormatter. Category drives optional per-channel routing.
public abstract record NotificationEvent
{
    public DateTime At { get; init; } = DateTime.UtcNow;

    public abstract NotificationCategory Category { get; }
}

public sealed record AnnouncementNotification(Announcement Announcement) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Listings;
}

public sealed record StrategyStartedNotification(
    string StrategyName,
    Exchange Exchange,
    string Symbol,
    Market Market,
    Side Side,
    decimal QuoteAmount,
    Guid AnnouncementId,
    string? Note = null) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Strategies;
}

public sealed record PositionOpenedNotification(Position Position) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Positions;
}

public sealed record PositionClosedNotification(
    Position Position,
    decimal? RealizedPnlUsd,
    decimal? RealizedPnlPct,
    string? Reason = null) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Positions;
}

public sealed record StopLossMovedNotification(
    Position Position,
    decimal OldStopPrice,
    decimal NewStopPrice) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Positions;
}

public sealed record ErrorNotification(string Message, string? Detail = null) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.Errors;
}

public sealed record SystemNotification(string Message, string? Detail = null) : NotificationEvent
{
    public override NotificationCategory Category => NotificationCategory.System;
}

public interface INotifier
{
    /// Non-blocking: enqueues the event for asynchronous delivery.
    /// Returns once the event is accepted (or dropped if pipeline is shut down).
    ValueTask SendAsync(NotificationEvent evt, CancellationToken ct = default);
}
