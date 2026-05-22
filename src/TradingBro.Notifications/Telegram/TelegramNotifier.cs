using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;

namespace TradingBro.Notifications.Telegram;

/// Enqueues notifications onto an in-memory channel. Actual delivery happens in
/// TelegramSendingService so that callers (collectors, strategies) never block
/// on Telegram I/O. Safe to inject anywhere as a singleton.
public sealed class TelegramNotifier : INotifier
{
    private readonly Channel<NotificationEvent> _queue;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(IOptions<TelegramOptions> options, ILogger<TelegramNotifier> logger)
    {
        _logger = logger;
        var capacity = Math.Max(16, options.Value.QueueCapacity);
        _queue = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    /// Read-only view of the pending queue — consumed by TelegramSendingService.
    internal ChannelReader<NotificationEvent> Reader => _queue.Reader;

    public ValueTask SendAsync(NotificationEvent evt, CancellationToken ct = default)
    {
        if (!_queue.Writer.TryWrite(evt))
        {
            _logger.LogWarning("Telegram queue full or closed — dropping event {Type}", evt.GetType().Name);
        }
        return ValueTask.CompletedTask;
    }
}
