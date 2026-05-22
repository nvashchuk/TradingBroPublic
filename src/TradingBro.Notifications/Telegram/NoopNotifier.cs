using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;

namespace TradingBro.Notifications.Telegram;

/// Fallback INotifier used when Telegram is disabled in config.
/// Logs the event at debug level and discards it — so callers don't need
/// to null-check or branch on configuration.
internal sealed class NoopNotifier(ILogger<NoopNotifier> logger) : INotifier
{
    public ValueTask SendAsync(NotificationEvent evt, CancellationToken ct = default)
    {
        logger.LogDebug("[noop notifier] {Type} {Category}", evt.GetType().Name, evt.Category);
        return ValueTask.CompletedTask;
    }
}
