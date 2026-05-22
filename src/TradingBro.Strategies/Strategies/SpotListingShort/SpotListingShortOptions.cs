using TradingBro.Core.Models;

namespace TradingBro.Strategies.Strategies.SpotListingShort;

public sealed class SpotListingShortOptions
{
    public bool Enabled { get; set; } = false;

    /// Which spot-listing announcements should trigger an entry.
    /// Default value lives in appsettings.json — keeping the C# default empty
    /// avoids ConfigurationBinder appending instead of replacing on Local override.
    public Exchange[] TriggerExchanges { get; set; } = [];

    /// Where we're willing to execute, **in priority order**.
    /// The first exchange in this list that has a qualifying futures contract wins.
    /// Default value lives in appsettings.json (Bybit then Binance, matches Python backtester).
    public Exchange[] PreferredExecutionExchanges { get; set; } = [];

    public decimal QuotePerTradeUsdt { get; set; } = 100m;

    /// 1 = no leverage. Configurable per strategy.
    public int Leverage { get; set; } = 1;

    /// Margin mode applied to the exchange position before placing the order.
    /// Default Isolated — safer (loss capped to this position's margin).
    /// Cross mode planned for later strategies.
    public MarginMode MarginMode { get; set; } = MarginMode.Isolated;

    /// Stop-loss as a fraction of entry (0.20 = +20% above entry for SHORT).
    public decimal StopLossPct { get; set; } = 0.20m;

    /// Take-profit as a fraction of entry (0.15 = 15% below entry for SHORT).
    public decimal TakeProfitPct { get; set; } = 0.15m;

    public int MaxHoldHours { get; set; } = 22;

    /// Futures contract must have been listed at least this long ago.
    /// Strategy-level decision — avoids illiquid freshly-listed contracts.
    public int MinFuturesAgeDays { get; set; } = 1;

    public int ManagementIntervalSeconds { get; set; } = 60;
}
