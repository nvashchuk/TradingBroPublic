using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Notifications.Telegram;

public sealed class AnnouncementSubscriber(
    IAnnouncementBus bus, 
    INotifier notifier, 
    ILogger<AnnouncementSubscriber> logger) : BackgroundService
{
    private readonly ChannelReader<Announcement> _reader = bus.Subscribe();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnnouncementSubscriber started");
        await foreach (var announcement in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await notifier.SendAsync(new AnnouncementNotification(announcement), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Broadcast failed for announcement {Symbol}", announcement.Symbol);
            }
        }
    }
}
