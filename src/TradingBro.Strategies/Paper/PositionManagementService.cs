using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;
using TradingBro.Strategies.Common;

namespace TradingBro.Strategies.Paper;

/// Periodically scans open positions and lets the originating strategy update
/// them (move SL, close on time-based exit, etc.). For paper positions it also
/// closes them when SL/TP are crossed (in real-trading mode the exchange handles that).
internal sealed class PositionManagementService(
    IServiceScopeFactory scopeFactory,
    IPriceFeed priceFeed,
    IEnumerable<IStrategy> strategies,
    IEnumerable<IExchangeClient> exchangeClients,
    INotifier notifier,
    IOptions<StrategiesOptions> options,
    ILogger<PositionManagementService> logger)
    : BackgroundService
{
    private readonly Dictionary<string, IStrategy> _strategyByName =
        strategies.ToDictionary(s => s.Name, StringComparer.Ordinal);

    private readonly Dictionary<Core.Models.Exchange, IExchangeClient> _exchangeByName =
        exchangeClients.ToDictionary(c => c.Exchange);

    private readonly StrategiesOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _strategyByName.Count == 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PositionManagementIntervalSeconds));
        logger.LogInformation("PositionManagementService started (interval {Sec}s)", interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PositionManagement tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        List<Position> openPositions;
        using (var readScope = scopeFactory.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
            openPositions = await db.Positions
                .Where(p => p.Status == PositionStatus.Open)
                .ToListAsync(ct);
        }

        foreach (var pos in openPositions)
        {
            await ManageOneAsync(pos, ct);
        }
    }

    private async Task ManageOneAsync(Position pos, CancellationToken ct)
    {
        if (!_strategyByName.TryGetValue(pos.StrategyName, out var strategy))
        {
            // Strategy may have been removed; leave the position untouched.
            return;
        }

        decimal currentPrice;
        try
        {
            currentPrice = await priceFeed.GetAsync(pos.Exchange, pos.Market, pos.Symbol, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Price poll failed for {Symbol} — skipping management tick", pos.Symbol);
            return;
        }

        // Let the strategy update SL or close on its own terms.
        try
        {
            await strategy.ManagePositionAsync(pos, currentPrice, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Strategy {Name} ManagePositionAsync threw for position {Id}", strategy.Name, pos.Id);
        }

        // If still open and paper, enforce SL/TP based on current price.
        // For real positions the exchange already enforces these — we just persist any SL moves.
        string? closeReason = null;
        if (pos.Status == PositionStatus.Open && pos.IsPaper)
        {
            closeReason = CheckSlTp(pos, currentPrice);
            if (closeReason is not null)
            {
                ClosePaper(pos, currentPrice);
            }
        }
        else if (pos.Status == PositionStatus.Closed && pos.ClosedAt != null)
        {
            // Strategy closed it (e.g. time-exit). For real positions we must
            // also instruct the exchange to close the actual order; otherwise
            // we'd "close" in DB but leave the real position open.
            if (!pos.IsPaper && _exchangeByName.TryGetValue(pos.Exchange, out var client))
            {
                var result = await client.ClosePositionAsync(pos.ExchangeOrderId ?? string.Empty, pos.Symbol, pos.Market, ct);
                if (result.Success && result.FilledPrice is decimal fp)
                {
                    var pnlPerUnit = pos.Side == Side.Long ? fp - pos.EntryPrice : pos.EntryPrice - fp;
                    pos.ExitPrice = fp;
                    pos.RealizedPnlUsd = pnlPerUnit * pos.Quantity;
                }
                else if (!result.Success)
                {
                    logger.LogError("Failed to close real position {Symbol} on exchange: {Error}", pos.Symbol, result.Error);
                    pos.Status = PositionStatus.Failed;
                    await notifier.SendAsync(new ErrorNotification(
                        $"Exchange close failed for {pos.Symbol}", result.Error ?? "(no detail)"), ct);
                }
            }
            closeReason = "strategy-exit";
        }

        await PersistPositionAsync(pos, ct);

        if (closeReason is not null)
        {
            await notifier.SendAsync(new PositionClosedNotification(
                Position: pos,
                RealizedPnlUsd: pos.RealizedPnlUsd,
                RealizedPnlPct: ComputeReturnPct(pos),
                Reason: closeReason), ct);
            logger.LogInformation("Paper position closed: {Symbol} {Reason} pnl={Pnl:+0.##;-0.##}",
                pos.Symbol, closeReason, pos.RealizedPnlUsd ?? 0);
        }
    }

    private static string? CheckSlTp(Position pos, decimal currentPrice)
    {
        if (pos.StopLossPrice is decimal sl)
        {
            if ((pos.Side == Side.Short && currentPrice >= sl) ||
                (pos.Side == Side.Long && currentPrice <= sl))
            {
                return "stop-loss";
            }
        }
        if (pos.TakeProfitPrice is decimal tp)
        {
            if ((pos.Side == Side.Short && currentPrice <= tp) ||
                (pos.Side == Side.Long && currentPrice >= tp))
            {
                return "take-profit";
            }
        }
        return null;
    }

    private static void ClosePaper(Position pos, decimal exitPrice)
    {
        var pnlPerUnit = pos.Side == Side.Long
            ? exitPrice - pos.EntryPrice
            : pos.EntryPrice - exitPrice;
        pos.ExitPrice = exitPrice;
        pos.RealizedPnlUsd = pnlPerUnit * pos.Quantity;
        pos.Status = PositionStatus.Closed;
        pos.ClosedAt = DateTime.UtcNow;
    }

    private static decimal? ComputeReturnPct(Position pos)
    {
        if (pos.ExitPrice is null || pos.EntryPrice == 0)
        {
            return null;
        }
        var move = pos.Side == Side.Long
            ? (pos.ExitPrice.Value - pos.EntryPrice) / pos.EntryPrice
            : (pos.EntryPrice - pos.ExitPrice.Value) / pos.EntryPrice;
        return move * 100m;
    }

    private async Task PersistPositionAsync(Position pos, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
        db.Positions.Attach(pos);
        db.Entry(pos).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }
}
