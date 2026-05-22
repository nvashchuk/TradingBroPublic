using TradingBro.Core.Models;

namespace TradingBro.Strategies.Catalog;

/// One exchange's view of its USDT-perp catalog. Implementations cache the
/// list internally and refresh on a schedule; calls are cheap.
public interface IFuturesInstrumentsSource
{
    Exchange Exchange { get; }

    Task<FuturesInstrument?> TryResolveAsync(string baseSymbol, CancellationToken ct);
}

/// Cross-exchange registry. Aggregates every IFuturesInstrumentsSource so
/// strategies can ask "give me all contracts for DOGE across all venues".
public interface IFuturesInstrumentsRegistry
{
    Task<IReadOnlyList<FuturesInstrument>> FindByBaseAsync(string baseSymbol, CancellationToken ct);
}
