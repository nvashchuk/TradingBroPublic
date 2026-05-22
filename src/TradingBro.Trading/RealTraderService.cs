using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;
using TradingBro.Trading.Common;

namespace TradingBro.Trading;

/// Subscribes to ITradeSignalBus and routes signals to the matching
/// IExchangeClient for **real** order placement. Replaces PaperTraderService
/// when TradingOptions.Mode is Testnet or Live.
public sealed class RealTraderService(
    ITradeSignalBus signalBus,
    IEnumerable<IExchangeClient> exchangeClients,
    IPriceFeed priceFeed,
    IServiceScopeFactory scopeFactory,
    INotifier notifier,
    IKillSwitch killSwitch,
    IOptions<TradingOptions> options,
    ILogger<RealTraderService> logger) : BackgroundService
{
    private readonly ChannelReader<TradeSignal> _reader = signalBus.Subscribe();
    private readonly Dictionary<Exchange, IExchangeClient> _byExchange = exchangeClients.ToDictionary(c => c.Exchange);
    private readonly TradingOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ConfirmLiveIfNeeded())
        {
            killSwitch.Engage("Live mode missing TRADINGBRO_LIVE_CONFIRM env var");
            await notifier.SendAsync(new ErrorNotification(
                "RealTrader refusing to start in Live mode without TRADINGBRO_LIVE_CONFIRM env match",
                "Killswitch engaged."), stoppingToken);
        }

        logger.LogWarning("RealTraderService started in {Mode} mode (clients: {Clients})",
            _opts.Mode, string.Join(",", _byExchange.Keys));

        await foreach (var signal in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PlaceRealOrderAsync(signal, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RealTrader failed for signal {Symbol}", signal.Symbol);
                await notifier.SendAsync(new ErrorNotification(
                    $"Real-trade failed for {signal.Symbol}", ex.Message), stoppingToken);
            }
        }
    }

    private bool ConfirmLiveIfNeeded()
    {
        if (_opts.Mode != TradingMode.Live)
        {
            return true;
        }
        var actual = Environment.GetEnvironmentVariable("TRADINGBRO_LIVE_CONFIRM");
        return !string.IsNullOrEmpty(_opts.RequireLiveConfirm)
            && string.Equals(actual, _opts.RequireLiveConfirm, StringComparison.Ordinal);
    }

    private async Task PlaceRealOrderAsync(TradeSignal signal, CancellationToken ct)
    {
        if (killSwitch.IsEngaged)
        {
            logger.LogWarning("Killswitch engaged ({Reason}) — skipping signal for {Symbol}",
                killSwitch.Reason, signal.Symbol);
            return;
        }

        if (!_byExchange.TryGetValue(signal.TargetExchange, out var client))
        {
            logger.LogWarning("No IExchangeClient registered for {Exchange} — skipping signal {Symbol}",
                signal.TargetExchange, signal.Symbol);
            return;
        }

        if (!await CheckRiskLimitsAsync(ct))
        {
            return;
        }

        // Resolve entry price up-front to compute SL/TP in absolute terms
        // (the SDK needs explicit stop prices, not pct).
        decimal markPrice;
        try
        {
            markPrice = await priceFeed.GetAsync(signal.TargetExchange, signal.Market, signal.Symbol, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mark price unavailable for {Symbol} — skipping", signal.Symbol);
            return;
        }

        var qty = signal.QuoteAmount * signal.Leverage / markPrice;
        var stopLoss = signal.StopLossPct is decimal slPct ? ApplyPct(markPrice, signal.Side, isLoss: true, slPct) : (decimal?)null;
        var takeProfit = signal.TakeProfitPct is decimal tpPct ? ApplyPct(markPrice, signal.Side, isLoss: false, tpPct) : (decimal?)null;

        var order = new OrderRequest(
            Symbol: signal.Symbol,
            Market: signal.Market,
            Side: signal.Side,
            Quantity: qty,
            Leverage: signal.Leverage,
            MarginMode: signal.MarginMode,
            StopLossPrice: stopLoss,
            TakeProfitPrice: takeProfit);

        var result = await client.PlaceOrderAsync(order, ct);
        if (!result.Success)
        {
            await notifier.SendAsync(new ErrorNotification(
                $"Order rejected: {signal.Symbol}", result.Error ?? "(no detail)"), ct);
            return;
        }

        // Entry succeeded but client reported sub-errors (e.g. protective SL/TP rejected).
        // Position is open on the exchange WITHOUT a stop — operator needs to know.
        if (!string.IsNullOrEmpty(result.Error))
        {
            await notifier.SendAsync(new ErrorNotification(
                $"⚠️ Position {signal.Symbol} open WITHOUT protective orders",
                result.Error), ct);
            logger.LogError("Protective orders failed for {Symbol}: {Error}", signal.Symbol, result.Error);
        }

        var entryPrice = result.FilledPrice ?? markPrice;
        var filled = result.FilledQuantity ?? qty;
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
            Quantity = filled,
            StopLossPrice = stopLoss,
            TakeProfitPrice = takeProfit,
            Leverage = signal.Leverage,
            MarginMode = signal.MarginMode,
            OpenedAt = DateTime.UtcNow,
            Status = PositionStatus.Open,
            IsPaper = false,
            ExchangeOrderId = result.ExchangeOrderId,
        };

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
            db.Positions.Add(position);
            await db.SaveChangesAsync(ct);
        }

        await notifier.SendAsync(new PositionOpenedNotification(position), ct);
        logger.LogInformation("Real position OPEN {Side} {Symbol} qty={Qty} @ {Price} (orderId={OrderId})",
            position.Side, position.Symbol, position.Quantity, position.EntryPrice, position.ExchangeOrderId);
    }

    private async Task<bool> CheckRiskLimitsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
        var openCount = await db.Positions.CountAsync(p => p.Status == PositionStatus.Open && !p.IsPaper, ct);
        if (openCount >= _opts.MaxOpenPositions)
        {
            logger.LogWarning("Risk gate: max open positions ({Max}) reached — skipping new signal", _opts.MaxOpenPositions);
            return false;
        }

        if (_opts.MaxDailyLossUsd > 0)
        {
            var startOfDay = DateTime.UtcNow.Date;
            var todayPnl = await db.Positions
                .Where(p => !p.IsPaper && p.Status == PositionStatus.Closed && p.ClosedAt >= startOfDay)
                .SumAsync(p => p.RealizedPnlUsd ?? 0m, ct);
            if (todayPnl <= -_opts.MaxDailyLossUsd)
            {
                killSwitch.Engage($"Daily loss limit hit ({todayPnl:0.00} USD)");
                await notifier.SendAsync(new ErrorNotification(
                    "Daily loss limit hit — trading halted",
                    $"Today PnL: {todayPnl:0.00} USDT, limit: -{_opts.MaxDailyLossUsd:0.00}"), ct);
                return false;
            }
        }
        return true;
    }

    private static decimal ApplyPct(decimal entry, Side side, bool isLoss, decimal pct)
    {
        var sign = (side, isLoss) switch
        {
            (Side.Long, true) => -1,
            (Side.Long, false) => +1,
            (Side.Short, true) => +1,
            (Side.Short, false) => -1,
            _ => 0,
        };
        return entry * (1m + sign * pct);
    }
}
