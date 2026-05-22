namespace TradingBro.Strategies.Catalog;

/// Aggregates every registered IFuturesInstrumentsSource. Asks each in parallel
/// and returns the combined list of contracts for a given base symbol.
/// Strategies receive ALL candidates and decide their own selection policy.
public sealed class FuturesInstrumentsRegistry(IEnumerable<IFuturesInstrumentsSource> sources)
    : IFuturesInstrumentsRegistry
{
    private readonly IFuturesInstrumentsSource[] _sources = sources.ToArray();

    public async Task<IReadOnlyList<FuturesInstrument>> FindByBaseAsync(string baseSymbol, CancellationToken ct)
    {
        if (_sources.Length == 0 || string.IsNullOrWhiteSpace(baseSymbol))
        {
            return Array.Empty<FuturesInstrument>();
        }

        var tasks = _sources.Select(s => s.TryResolveAsync(baseSymbol, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).ToList()!;
    }
}
