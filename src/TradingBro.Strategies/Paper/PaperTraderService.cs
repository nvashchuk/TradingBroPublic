using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;

namespace TradingBro.Strategies.Paper;

/// Subscribes to ITradeSignalBus and opens "paper" positions:
/// real market price from IPriceFeed, but no exchange order is sent.
/// Active only when Trading:Mode = Paper (default). RealTraderService takes
/// over when Mode = Testnet or Live.
internal sealed class PaperTraderService(
    ITradeSignalBus signalBus,
    IPriceFeed priceFeed,
    IServiceScopeFactory scopeFactory,
    INotifier notifier,
    IConfiguration configuration,
    ILogger<PaperTraderService> logger) : BackgroundService
{
    private readonly ChannelReader<TradeSignal> _reader = signalBus.Subscribe();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = configuration["Trading:Mode"];
        if (!string.IsNullOrEmpty(mode) && !string.Equals(mode, "Paper", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("PaperTraderService disabled (Trading:Mode = {Mode}) — RealTrader handles signals", mode);
            return;
        }

        logger.LogInformation("PaperTraderService started");

        await foreach (var signal in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await OpenPaperPositionAsync(signal, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper-trade failed for signal {Symbol}", signal.Symbol);
                await notifier.SendAsync(new ErrorNotification(
                    $"Paper-trade failed for {signal.Symbol}", ex.Message), stoppingToken);
            }
        }
    }

    private async Task OpenPaperPositionAsync(TradeSignal signal, CancellationToken ct)
    {
        decimal entryPrice;
        try
        {
            entryPrice = await priceFeed.GetAsync(signal.TargetExchange, signal.Market, signal.Symbol, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PaperTrader could not resolve entry price for {Symbol} — skipping", signal.Symbol);
            return;
        }

        if (entryPrice <= 0)
        {
            logger.LogWarning("PaperTrader got non-positive price {Price} for {Symbol} — skipping", entryPrice, signal.Symbol);
            return;
        }

        var quantity = signal.QuoteAmount / entryPrice;

        decimal? stopLossPrice = signal.StopLossPct is not null
            ? ApplyPct(entryPrice, signal.Side, isLoss: true, signal.StopLossPct.Value)
            : null;
        decimal? takeProfitPrice = signal.TakeProfitPct is not null
            ? ApplyPct(entryPrice, signal.Side, isLoss: false, signal.TakeProfitPct.Value)
            : null;

        var position = new Position
        {
            Id = Guid.NewGuid(),
            TradeSignalId = signal.Id,
            StrategyName = signal.StrategyName,
            Exchange = signal.TargetExchange,
            Symbol = signal.Symbol,
            Market = signal.Market,
            Side = signal.Side,
            EntryPrice = entryPrice,
            Quantity = quantity,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            Leverage = signal.Leverage,
            MarginMode = signal.MarginMode,
            OpenedAt = DateTime.UtcNow,
            Status = PositionStatus.Open,
            IsPaper = true,
        };

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
            db.Positions.Add(position);
            await db.SaveChangesAsync(ct);
        }

        await notifier.SendAsync(new PositionOpenedNotification(position), ct);

        logger.LogInformation("Paper position opened: {Side} {Symbol} qty={Qty:0.######} @ {Price:0.########}",
            position.Side, position.Symbol, position.Quantity, position.EntryPrice);
    }

    /// For SHORT: loss = price goes UP, profit = price goes DOWN.
    /// For LONG: loss = price goes DOWN, profit = price goes UP.
    private static decimal ApplyPct(decimal entry, Side side, bool isLoss, decimal pct)
    {
        var sign = (side, isLoss) switch
        {
            (Side.Long, true) => -1,    // long: SL below entry
            (Side.Long, false) => +1,   // long: TP above entry
            (Side.Short, true) => +1,   // short: SL above entry
            (Side.Short, false) => -1,  // short: TP below entry
            _ => 0,
        };
        return entry * (1m + sign * pct);
    }
}
