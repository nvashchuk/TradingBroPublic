using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Strategies.Catalog;

namespace TradingBro.Strategies.Strategies.SpotListingShort;

/// MVP port of strategy/listing_short.py.
///
/// Trigger: spot listing announcement on a configured exchange (default
///          Binance / Upbit / Bithumb).
/// Action:  ask the futures registry for every USDT-perp contract on the
///          configured execution exchanges, drop ones younger than
///          MinFuturesAgeDays, pick the first available in
///          PreferredExecutionExchanges order, and SHORT it.
public sealed class SpotListingShortStrategy(
    IFuturesInstrumentsRegistry registry,
    IOptions<SpotListingShortOptions> options,
    ILogger<SpotListingShortStrategy> logger)
    : IStrategy
{
    private readonly SpotListingShortOptions _opts = options.Value;
    private readonly HashSet<Exchange> _triggerExchanges = new(options.Value.TriggerExchanges);

    public string Name => "spot-listing-short";

    public TimeSpan ManagementInterval => TimeSpan.FromSeconds(Math.Max(5, _opts.ManagementIntervalSeconds));

    public async Task<TradeSignal?> OnAnnouncementAsync(Announcement a, CancellationToken ct)
    {
        if (!_opts.Enabled || a.EventType != EventType.Listing || a.Market != Market.Spot)
        {
            return null;
        }
        if (!_triggerExchanges.Contains(a.Exchange))
        {
            return null;
        }

        var minAge = TimeSpan.FromDays(Math.Max(0, _opts.MinFuturesAgeDays));
        var candidates = await registry.FindByBaseAsync(a.Symbol, ct);

        var eligible = candidates.Where(c => c.Age >= minAge).ToList();
        if (eligible.Count == 0)
        {
            logger.LogDebug("Skip {Symbol}: no futures ≥ {Age} on any execution venue", a.Symbol, minAge);
            return null;
        }

        var chosen = PickByPreference(eligible);
        if (chosen is null)
        {
            logger.LogDebug("Skip {Symbol}: no contract on preferred venues {Venues}",
                a.Symbol, string.Join(",", _opts.PreferredExecutionExchanges));
            return null;
        }

        logger.LogInformation("Strategy triggered: {Source}/{Symbol} → SHORT {Exch} {Contract} (age {Age:%d}d)",
            a.Exchange, a.Symbol, chosen.Exchange, chosen.ContractSymbol, chosen.Age);

        return new TradeSignal
        {
            Id = Guid.NewGuid(),
            TargetExchange = chosen.Exchange,
            Symbol = chosen.ContractSymbol,
            Market = Market.Futures,
            Side = Side.Short,
            QuoteAmount = _opts.QuotePerTradeUsdt,
            StopLossPct = _opts.StopLossPct,
            TakeProfitPct = _opts.TakeProfitPct,
            Leverage = _opts.Leverage,
            MarginMode = _opts.MarginMode,
        };
    }

    private FuturesInstrument? PickByPreference(IReadOnlyList<FuturesInstrument> eligible)
    {
        foreach (var preferred in _opts.PreferredExecutionExchanges)
        {
            var match = eligible.FirstOrDefault(c => c.Exchange == preferred);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    public Task ManagePositionAsync(Position position, decimal currentPrice, CancellationToken ct)
    {
        if (position.Status != PositionStatus.Open)
        {
            return Task.CompletedTask;
        }

        // Time-based exit: close at current market price after MaxHoldHours.
        if (DateTime.UtcNow - position.OpenedAt >= TimeSpan.FromHours(_opts.MaxHoldHours))
        {
            ClosePosition(position, currentPrice);
            logger.LogInformation("Position {Id} closed by time-exit ({Hours}h)", position.Id, _opts.MaxHoldHours);
        }

        return Task.CompletedTask;
    }

    private static void ClosePosition(Position pos, decimal exitPrice)
    {
        var pnlPerUnit = pos.Side == Side.Long
            ? exitPrice - pos.EntryPrice
            : pos.EntryPrice - exitPrice;
        pos.ExitPrice = exitPrice;
        pos.RealizedPnlUsd = pnlPerUnit * pos.Quantity;
        pos.Status = PositionStatus.Closed;
        pos.ClosedAt = DateTime.UtcNow;
    }
}
