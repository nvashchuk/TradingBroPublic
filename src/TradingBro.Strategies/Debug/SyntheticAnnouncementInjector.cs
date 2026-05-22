using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;

namespace TradingBro.Strategies.Debug;

/// One-shot debug helper: ~5 seconds after startup, publishes a synthetic
/// spot-listing announcement so we can exercise the end-to-end strategy
/// → paper-trader → position-manager flow without waiting for a real listing.
///
/// Enabled by `Strategies:Debug:InjectSymbol = SYMBOL` in config. Disabled by default.
public sealed class SyntheticAnnouncementInjector(
    IConfiguration configuration,
    IAnnouncementBus bus,
    IServiceScopeFactory scopeFactory,
    ILogger<SyntheticAnnouncementInjector> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var symbol = configuration["Strategies:Debug:InjectSymbol"];
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Exchange = Exchange.Binance,
            Symbol = symbol.ToUpperInvariant(),
            Market = Market.Spot,
            EventType = EventType.Listing,
            AnnouncedAt = DateTime.UtcNow,
            EventAt = null,
            Title = $"[SYNTHETIC] Binance Will List {symbol.ToUpperInvariant()}",
            Url = "https://example.com/synthetic",
            ExternalId = $"synthetic-{Guid.NewGuid():N}",
        };

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
            db.Announcements.Add(announcement);
            await db.SaveChangesAsync(stoppingToken);
        }

        await bus.PublishAsync(announcement, stoppingToken);
        logger.LogWarning("⚙️ Injected synthetic listing announcement for {Symbol}", symbol);
    }
}
