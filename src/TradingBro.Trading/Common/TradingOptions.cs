namespace TradingBro.Trading.Common;

public enum TradingMode
{
    /// No exchange interaction — PaperTraderService handles signals.
    Paper = 0,

    /// Use exchange testnets (Binance Futures testnet, Bybit testnet).
    /// Required first stage before going live.
    Testnet = 1,

    /// Real funds on mainnet exchanges. Requires explicit RequireLiveConfirm
    /// to actually arm — protects against accidental boot in production keys.
    Live = 2,
}

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public TradingMode Mode { get; set; } = TradingMode.Paper;

    /// Extra safety: even with Mode=Live, no orders are placed unless this
    /// matches the value of TRADINGBRO_LIVE_CONFIRM env var. Set to anything
    /// non-empty (e.g. a generated token) and only export the env var when
    /// you actually intend to trade with real money.
    public string RequireLiveConfirm { get; set; } = string.Empty;

    /// Max simultaneously-open real positions across all strategies.
    public int MaxOpenPositions { get; set; } = 3;

    /// Auto-halt trading if realized PnL for the current UTC day drops below this.
    /// 0 = disabled.
    public decimal MaxDailyLossUsd { get; set; } = 0m;

    public BinanceTradingOptions Binance { get; set; } = new();

    public BybitTradingOptions Bybit { get; set; } = new();
}

public sealed class BinanceTradingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// Override REST base URL. When set, takes priority over Mode-based defaults.
    /// Examples:
    ///   "https://demo-api.binance.com"      — Binance Demo Trading (spot + futures via /api/v3 + /fapi)
    ///   "https://testnet.binancefuture.com" — Binance Futures Testnet (separate accounts/keys from live)
    /// Leave empty to use the SDK's built-in Testnet/Live environment for the current Mode.
    public string RestUrl { get; set; } = string.Empty;
}

public sealed class BybitTradingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
