using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public sealed record OrderRequest(
    string Symbol,
    Market Market,
    Side Side,
    decimal Quantity,
    int Leverage = 1,
    MarginMode MarginMode = MarginMode.Isolated,
    decimal? StopLossPrice = null,
    decimal? TakeProfitPrice = null);

public sealed record OrderResult(
    bool Success,
    string? ExchangeOrderId,
    decimal? FilledPrice,
    decimal? FilledQuantity,
    string? Error = null);

public interface IExchangeClient
{
    Exchange Exchange { get; }

    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);

    Task<OrderResult> UpdateStopLossAsync(string exchangeOrderId, string symbol, Market market, decimal newStopPrice, CancellationToken ct = default);

    Task<OrderResult> ClosePositionAsync(string exchangeOrderId, string symbol, Market market, CancellationToken ct = default);
}
