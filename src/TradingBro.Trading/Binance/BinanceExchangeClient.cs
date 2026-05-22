using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Trading.Common;
using CoreExchange = TradingBro.Core.Models.Exchange;
using CoreMarginMode = TradingBro.Core.Models.MarginMode;
using CoreSide = TradingBro.Core.Models.Side;

namespace TradingBro.Trading.Binance;

/// IExchangeClient implementation for Binance USDⓈ-M futures.
/// Supports Testnet and Live environments.
///
/// Note: spot trading not implemented — current strategy lineup never opens
/// spot positions on Binance. Add SpotApi handling here when needed.
public sealed class BinanceExchangeClient : IExchangeClient
{
    private readonly BinanceRestClient _client;
    private readonly ILogger<BinanceExchangeClient> _logger;
    private readonly Dictionary<string, BinanceSymbolFilters> _filters = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _filtersGate = new(1, 1);
    private DateTime _filtersLoadedUtc = DateTime.MinValue;

    public BinanceExchangeClient(TradingOptions options, ILogger<BinanceExchangeClient> logger)
    {
        _logger = logger;
        var env = ResolveEnvironment(options, logger);

        _client = new BinanceRestClient(restOpts =>
        {
            restOpts.Environment = env;
            if (!string.IsNullOrEmpty(options.Binance.ApiKey) && !string.IsNullOrEmpty(options.Binance.ApiSecret))
            {
                // Binance.Net 12.x: ApiCredentials is abstract — use BinanceCredentials
                // (also gives access to RSA key options if we ever need them).
                restOpts.ApiCredentials = new BinanceCredentials(options.Binance.ApiKey, options.Binance.ApiSecret);
            }
        });
    }

    /// Picks the Binance environment based on the explicit RestUrl override (if any),
    /// then Mode. Demo / sandbox URLs ride on top of all routes (REST + sockets) — we
    /// don't trade those over WS yet, so socket URLs point at the same host for
    /// future-proofing.
    private static BinanceEnvironment ResolveEnvironment(TradingOptions options, ILogger logger)
    {
        var url = options.Binance.RestUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning("BinanceExchangeClient using custom environment: {Url}", url);
            // 11 positional args: name, spotRest, spotSocketStream, spotSocketApi,
            // blvtSocket, usdFuturesRest, usdFuturesSocket, usdFuturesSocketApi,
            // coinFuturesRest, coinFuturesSocket, coinFuturesSocketApi.
            return BinanceEnvironment.CreateCustom("Custom",
                url, url, url, url, url, url, url, url, url, url);
        }
        return options.Mode == TradingMode.Testnet ? BinanceEnvironment.Testnet : BinanceEnvironment.Live;
    }

    public CoreExchange Exchange => CoreExchange.Binance;

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (request.Market != Market.Futures)
        {
            return new OrderResult(false, null, null, null, $"Market {request.Market} not supported on Binance trader");
        }

        await EnsureFiltersAsync(ct);
        if (!_filters.TryGetValue(request.Symbol, out var filters))
        {
            return new OrderResult(false, null, null, null, $"Symbol {request.Symbol} not in exchangeInfo");
        }

        // 1) Margin mode (silently ignored if already set)
        try
        {
            await _client.UsdFuturesApi.Account.ChangeMarginTypeAsync(
                request.Symbol,
                request.MarginMode == CoreMarginMode.Cross ? FuturesMarginType.Cross : FuturesMarginType.Isolated,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ChangeMarginType ignored for {Symbol} (often already set)", request.Symbol);
        }

        // 2) Leverage
        var levResult = await _client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(request.Symbol, request.Leverage, ct: ct);
        if (!levResult.Success)
        {
            return new OrderResult(false, null, null, null, $"ChangeLeverage failed: {levResult.Error}");
        }

        // 3) Round quantity to stepSize
        var quantity = RoundDown(request.Quantity, filters.StepSize);
        if (quantity <= 0)
        {
            return new OrderResult(false, null, null, null, "Rounded quantity is zero (below stepSize)");
        }

        // 4) Market entry. orderResponseType=Result tells Binance to wait for
        // the fill before responding, so we get a real AveragePrice/QuantityFilled
        // instead of zeroes in the ACK.
        var entrySide = request.Side == CoreSide.Short ? OrderSide.Sell : OrderSide.Buy;
        var entry = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: request.Symbol,
            side: entrySide,
            type: FuturesOrderType.Market,
            quantity: quantity,
            orderResponseType: OrderResponseType.Result,
            ct: ct);
        if (!entry.Success || entry.Data is null)
        {
            return new OrderResult(false, null, null, null, $"PlaceOrder failed: {entry.Error}");
        }

        // Even with Result mode some fields may still be 0 (rare race). Treat 0 as
        // "no fill data yet" and let caller fall back to mark price.
        decimal? filledPrice = entry.Data.AveragePrice > 0 ? entry.Data.AveragePrice
            : entry.Data.Price > 0 ? entry.Data.Price
            : null;
        decimal? filledQty = entry.Data.QuantityFilled > 0 ? entry.Data.QuantityFilled : null;

        // 5) SL/TP — Binance migrated StopMarket/TakeProfitMarket protective orders
        // to the Conditional Order endpoint (PlaceConditionalOrderAsync). Regular
        // PlaceOrderAsync rejects StopMarket with -4120 regardless of qty/closePos
        // flags. The Conditional endpoint requires explicit quantity even if
        // closePosition=true is set.
        var protectiveQty = filledQty ?? quantity;
        var exitSide = entrySide == OrderSide.Sell ? OrderSide.Buy : OrderSide.Sell;
        var protectiveErrors = new List<string>();
        if (request.StopLossPrice is decimal slPrice)
        {
            var slResult = await PlaceConditionalAsync(request.Symbol, exitSide, ConditionalOrderType.StopMarket,
                protectiveQty, RoundDown(slPrice, filters.TickSize), ct);
            if (!slResult.Success) protectiveErrors.Add($"SL: {slResult.Error}");
        }
        if (request.TakeProfitPrice is decimal tpPrice)
        {
            var tpResult = await PlaceConditionalAsync(request.Symbol, exitSide, ConditionalOrderType.TakeProfitMarket,
                protectiveQty, RoundDown(tpPrice, filters.TickSize), ct);
            if (!tpResult.Success) protectiveErrors.Add($"TP: {tpResult.Error}");
        }

        // Entry succeeded; surface protective-order failures via the error channel
        // so RealTrader can fire a Telegram alert. Position stays open and is still
        // managed by PositionManagementService (time-exit etc.).
        return new OrderResult(
            Success: true,
            ExchangeOrderId: entry.Data.Id.ToString(),
            FilledPrice: filledPrice,
            FilledQuantity: filledQty,
            Error: protectiveErrors.Count == 0 ? null : "Protective orders failed: " + string.Join("; ", protectiveErrors));
    }

    public async Task<OrderResult> UpdateStopLossAsync(string exchangeOrderId, string symbol, Market market, decimal newStopPrice, CancellationToken ct = default)
    {
        if (market != Market.Futures)
        {
            return new OrderResult(false, null, null, null, $"Market {market} not supported");
        }
        await EnsureFiltersAsync(ct);
        if (!_filters.TryGetValue(symbol, out var filters))
        {
            return new OrderResult(false, null, null, null, $"Symbol {symbol} unknown");
        }

        // Cancel any open conditional StopMarket orders for this symbol, then place fresh.
        // Positional: GetOpenConditionalOrdersAsync(symbol, clientOrderId, id, receiveWindow, ct)
        var openOrders = await _client.UsdFuturesApi.Trading.GetOpenConditionalOrdersAsync(symbol, null, null, null, ct);
        if (openOrders.Success && openOrders.Data is not null)
        {
            foreach (var ord in openOrders.Data)
            {
                await _client.UsdFuturesApi.Trading.CancelConditionalOrderAsync(ord.Id, null, null, ct);
            }
        }

        var positions = await _client.UsdFuturesApi.Account.GetPositionInformationAsync(symbol: symbol, ct: ct);
        if (!positions.Success || positions.Data is null)
        {
            return new OrderResult(false, null, null, null, $"GetPositionInformation failed: {positions.Error}");
        }
        var net = positions.Data.FirstOrDefault();
        if (net is null || net.Quantity == 0m)
        {
            return new OrderResult(false, null, null, null, "No open position to attach SL to");
        }
        var exitSide = net.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell;
        var qtyForSl = Math.Abs(net.Quantity);
        var rounded = RoundDown(newStopPrice, filters.TickSize);
        return await PlaceConditionalAsync(symbol, exitSide, ConditionalOrderType.StopMarket, qtyForSl, rounded, ct);
    }

    public async Task<OrderResult> ClosePositionAsync(string exchangeOrderId, string symbol, Market market, CancellationToken ct = default)
    {
        if (market != Market.Futures)
        {
            return new OrderResult(false, null, null, null, $"Market {market} not supported");
        }
        var positions = await _client.UsdFuturesApi.Account.GetPositionInformationAsync(symbol: symbol, ct: ct);
        var pos = positions.Data?.FirstOrDefault();
        if (pos is null || pos.Quantity == 0m)
        {
            return new OrderResult(false, null, null, null, "No open position");
        }

        var exitSide = pos.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell;
        var quantity = Math.Abs(pos.Quantity);
        var resp = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: exitSide,
            type: FuturesOrderType.Market,
            quantity: quantity,
            reduceOnly: true,
            orderResponseType: OrderResponseType.Result,
            ct: ct);
        if (!resp.Success || resp.Data is null)
        {
            return new OrderResult(false, null, null, null, $"Close failed: {resp.Error}");
        }
        decimal? exitPrice = resp.Data.AveragePrice > 0 ? resp.Data.AveragePrice
            : resp.Data.Price > 0 ? resp.Data.Price
            : null;
        decimal? exitQty = resp.Data.QuantityFilled > 0 ? resp.Data.QuantityFilled : null;
        return new OrderResult(true, resp.Data.Id.ToString(), exitPrice, exitQty);
    }

    private async Task<OrderResult> PlaceConditionalAsync(
        string symbol, OrderSide side, ConditionalOrderType type, decimal quantity, decimal stopPrice, CancellationToken ct)
    {
        // Conditional Order endpoint with explicit quantity + reduceOnly.
        // Positional args (20 params):
        //   symbol, side, type, quantity, price, positionSide, timeInForce, reduceOnly,
        //   newClientOrderId, stopPrice (pos 10), activationPrice, callbackRate,
        //   workingType, priceProtect, closePosition (pos 15), priceMatch,
        //   selfTradePreventionMode, goodTillDate, receiveWindow, ct.
        var resp = await _client.UsdFuturesApi.Trading.PlaceConditionalOrderAsync(
            symbol, side, type,
            quantity,           // 4 quantity
            null,               // 5 price
            null,               // 6 positionSide
            null,               // 7 timeInForce
            true,               // 8 reduceOnly
            null,               // 9 newClientOrderId
            stopPrice,          // 10 stopPrice
            null, null, null, null, // 11-14
            null,               // 15 closePosition (let qty drive)
            null, null, null, null, // 16-19
            ct);                // 20
        if (!resp.Success || resp.Data is null)
        {
            _logger.LogWarning("Conditional {Type} order failed: {Error}", type, resp.Error);
            return new OrderResult(false, null, null, null, $"{type}: {resp.Error}");
        }
        return new OrderResult(true, resp.Data.Id.ToString(), null, null);
    }

    private async Task EnsureFiltersAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _filtersLoadedUtc < TimeSpan.FromHours(6))
        {
            return;
        }
        await _filtersGate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _filtersLoadedUtc < TimeSpan.FromHours(6))
            {
                return;
            }
            var info = await _client.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync(ct);
            if (!info.Success || info.Data is null)
            {
                _logger.LogWarning("Failed to refresh Binance futures filters: {Error}", info.Error);
                return;
            }
            _filters.Clear();
            foreach (var sym in info.Data.Symbols)
            {
                if (sym.QuoteAsset != "USDT" || sym.Status != SymbolStatus.Trading) continue;
                _filters[sym.Name] = new BinanceSymbolFilters(
                    StepSize: sym.LotSizeFilter?.StepSize ?? 0.001m,
                    TickSize: sym.PriceFilter?.TickSize ?? 0.0001m);
            }
            _filtersLoadedUtc = DateTime.UtcNow;
            _logger.LogInformation("Binance futures filters refreshed: {Count} symbols", _filters.Count);
        }
        finally
        {
            _filtersGate.Release();
        }
    }

    private static decimal RoundDown(decimal value, decimal step)
    {
        if (step <= 0m) return value;
        return Math.Floor(value / step) * step;
    }
}

internal sealed record BinanceSymbolFilters(decimal StepSize, decimal TickSize);
