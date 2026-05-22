using TradingBro.Core.Models;

namespace TradingBro.Strategies.Catalog;

/// Exchange-agnostic view of a futures contract.
/// One instance per (Exchange × ContractSymbol) — strategies pick the best
/// candidate for their needs without caring how the data was sourced.
public sealed record FuturesInstrument(
    Exchange Exchange,
    string ContractSymbol,    // e.g. "DOGEUSDT" (same on Binance & Bybit for USDT-perp)
    string BaseAsset,         // "DOGE"
    string QuoteAsset,        // "USDT"
    DateTime LaunchTimeUtc)
{
    public TimeSpan Age => DateTime.UtcNow - LaunchTimeUtc;
}
