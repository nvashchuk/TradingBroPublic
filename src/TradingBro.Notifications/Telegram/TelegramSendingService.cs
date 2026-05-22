using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using TradingBro.Core.Abstractions;

namespace TradingBro.Notifications.Telegram;

/// Pumps the TelegramNotifier queue and ships messages to Telegram with
/// retry-on-429 and per-chat error isolation.
internal sealed class TelegramSendingService(
    TelegramNotifier notifier,
    ITelegramBotClient botClient,
    IOptions<TelegramOptions> options,
    MuteState muteState,
    ILogger<TelegramSendingService> logger)
    : BackgroundService
{
    private readonly TelegramOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelegramSendingService started (default chats: {Count})", _options.ChatIds.Count);

        await foreach (var evt in notifier.Reader.ReadAllAsync(stoppingToken))
        {
            if (muteState.IsMuted)
            {
                logger.LogDebug("Muted — dropping {Type}", evt.GetType().Name);
                continue;
            }

            var text = TelegramMessageFormatter.Format(evt);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var chats = ResolveChats(evt.Category);
            foreach (var chatId in chats)
            {
                try
                {
                    await DeliverAsync(chatId, text, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Telegram send failed for chat {ChatId} (event {Type})", chatId, evt.GetType().Name);
                }
            }
        }
    }

    private IReadOnlyList<long> ResolveChats(NotificationCategory category)
    {
        if (_options.Routing.TryGetValue(category, out var routed) && routed.Count > 0)
        {
            return routed;
        }
        return _options.ChatIds;
    }

    private async Task DeliverAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            await botClient.SendMessage(chatId, text, ParseMode.Html, disableNotification: false, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == (int)HttpStatusCode.TooManyRequests)
        {
            var retryAfter = ex.Parameters?.RetryAfter ?? 5;
            logger.LogWarning("Telegram 429 on chat {ChatId}, retrying in {Sec}s", chatId, retryAfter);
            await Task.Delay(TimeSpan.FromSeconds(retryAfter), ct);
            await botClient.SendMessage(chatId, text, ParseMode.Html, cancellationToken: ct);
        }
    }
}
