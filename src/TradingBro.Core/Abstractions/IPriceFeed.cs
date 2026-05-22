using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public sealed record Tick(string Symbol, decimal Price, DateTime At);

public interface IPriceFeed
{
    Task<decimal> GetAsync(Exchange exchange, Market market, string symbol, CancellationToken ct = default);

    IAsyncEnumerable<Tick> Subscribe(Exchange exchange, Market market, string symbol, CancellationToken ct = default);
}
