using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;
using TradingBro.Strategies.Common;

namespace TradingBro.Strategies.Engine;

/// Subscribes to IAnnouncementBus, asks each registered IStrategy whether it
/// wants to act on the announcement, persists resulting TradeSignals and
/// publishes them to ITradeSignalBus for downstream paper/real traders.
internal sealed class StrategyEngine(
    IAnnouncementBus announcementBus,
    ITradeSignalBus signalBus,
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStrategy> strategies,
    INotifier notifier,
    IOptions<StrategiesOptions> options,
    ILogger<StrategyEngine> logger) : BackgroundService
{
    private readonly ChannelReader<Announcement> _reader = announcementBus.Subscribe();
    private readonly IStrategy[] _strategies = strategies.ToArray();
    private readonly StrategiesOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("StrategyEngine disabled via config");
            return;
        }
        if (_strategies.Length == 0)
        {
            logger.LogWarning("StrategyEngine enabled but no IStrategy registered");
            return;
        }

        logger.LogInformation("StrategyEngine started with {Count} strategies: {Names}",
            _strategies.Length, string.Join(", ", _strategies.Select(s => s.Name)));

        var staleThreshold = TimeSpan.FromMinutes(Math.Max(1, _options.MaxAnnouncementAgeMinutes));

        await foreach (var announcement in _reader.ReadAllAsync(stoppingToken))
        {
            var age = DateTime.UtcNow - announcement.AnnouncedAt;
            if (age > staleThreshold)
            {
                logger.LogDebug("Skipping stale announcement {Symbol} (age {Age})", announcement.Symbol, age);
                continue;
            }

            foreach (var strategy in _strategies)
            {
                try
                {
                    await EvaluateAsync(strategy, announcement, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Strategy {Strategy} threw on announcement {Symbol}",
                        strategy.Name, announcement.Symbol);
                }
            }
        }
    }

    private async Task EvaluateAsync(IStrategy strategy, Announcement announcement, CancellationToken ct)
    {
        var signal = await strategy.OnAnnouncementAsync(announcement, ct);
        if (signal is null)
        {
            return;
        }

        signal.AnnouncementId = announcement.Id;
        signal.StrategyName = strategy.Name;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();

            // Dedupe by (strategy, exchange, symbol). Same strategy won't open
            // a second position on a tick it already owns (restart-spam, race on
            // duplicate announcements across collectors, etc.). DIFFERENT strategies
            // are free to coexist on the same symbol — e.g. a SHORT-on-listing
            // strategy and a LONG-on-rebound strategy holding DOGE simultaneously.
            var alreadyOpen = await db.Positions.AnyAsync(p =>
                p.Status == PositionStatus.Open
                && p.StrategyName == signal.StrategyName
                && p.Exchange == signal.TargetExchange
                && p.Symbol == signal.Symbol, ct);
            if (alreadyOpen)
            {
                logger.LogInformation(
                    "Skipping {Strategy} signal for {Exchange}/{Symbol}: position already open for this strategy",
                    strategy.Name, signal.TargetExchange, signal.Symbol);
                return;
            }

            db.TradeSignals.Add(signal);
            await db.SaveChangesAsync(ct);
        }

        await signalBus.PublishAsync(signal, ct);
        await notifier.SendAsync(new StrategyStartedNotification(
            StrategyName: strategy.Name,
            Exchange: signal.TargetExchange,
            Symbol: signal.Symbol,
            Market: signal.Market,
            Side: signal.Side,
            QuoteAmount: signal.QuoteAmount,
            AnnouncementId: announcement.Id,
            Note: $"Trigger: {announcement.Exchange} {announcement.EventType} {announcement.Symbol}"), ct);

        logger.LogInformation("Strategy {Strategy} → signal {Side} {Exchange} {Symbol} qty={Qty}",
            strategy.Name, signal.Side, signal.TargetExchange, signal.Symbol, signal.QuoteAmount);
    }
}
