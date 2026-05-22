using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Strategies.PriceFeeds;

/// REST-polling IPriceFeed.
///   GetAsync: one-shot ticker lookup (used by PaperTrader + PositionManager).
///   Subscribe: simple polling loop wrapped as IAsyncEnumerable; later we'll
///   swap a WebSocket implementation for the same interface.
public sealed class RestPriceFeed(IHttpClientFactory httpClientFactory, ILogger<RestPriceFeed> logger)
    : IPriceFeed
{
    public const string BybitClient = "BybitMarket";
    public const string BinanceFuturesClient = "BinanceFuturesMarket";

    public async Task<decimal> GetAsync(Exchange exchange, Market market, string symbol, CancellationToken ct = default)
    {
        return exchange switch
        {
            Exchange.Bybit => await GetBybitAsync(market, symbol, ct),
            Exchange.Binance => await GetBinanceFuturesAsync(symbol, ct),
            _ => throw new NotSupportedException($"Price feed for {exchange} {market} not implemented"),
        };
    }

    public async IAsyncEnumerable<Tick> Subscribe(
        Exchange exchange,
        Market market,
        string symbol,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // 2-second poll is fine for now — switches to WebSocket in a future stage.
        var period = TimeSpan.FromSeconds(2);
        using var timer = new PeriodicTimer(period);
        do
        {
            decimal price;
            try
            {
                price = await GetAsync(exchange, market, symbol, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Price poll failed for {Exchange} {Symbol}", exchange, symbol);
                continue;
            }
            yield return new Tick(symbol, price, DateTime.UtcNow);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task<decimal> GetBybitAsync(Market market, string symbol, CancellationToken ct)
    {
        var category = market == Market.Spot ? "spot" : "linear";
        var url = $"https://api.bybit.com/v5/market/tickers?category={category}&symbol={symbol}";
        var client = httpClientFactory.CreateClient(BybitClient);
        var resp = await client.GetFromJsonAsync<BybitTickerResponse>(url, ct);
        var price = resp?.Result?.List?.FirstOrDefault()?.LastPrice;
        if (string.IsNullOrEmpty(price) || !decimal.TryParse(price, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            throw new InvalidOperationException($"Bybit ticker missing price for {symbol}");
        }
        return v;
    }

    private async Task<decimal> GetBinanceFuturesAsync(string symbol, CancellationToken ct)
    {
        var url = $"https://fapi.binance.com/fapi/v1/ticker/price?symbol={symbol}";
        var client = httpClientFactory.CreateClient(BinanceFuturesClient);
        var resp = await client.GetFromJsonAsync<BinanceTickerResponse>(url, ct);
        if (string.IsNullOrEmpty(resp?.Price) || !decimal.TryParse(resp.Price, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            throw new InvalidOperationException($"Binance ticker missing price for {symbol}");
        }
        return v;
    }

    private sealed class BybitTickerResponse
    {
        [JsonPropertyName("retCode")] public int RetCode { get; set; }
        [JsonPropertyName("result")] public BybitTickerResult? Result { get; set; }
    }

    private sealed class BybitTickerResult
    {
        [JsonPropertyName("list")] public List<BybitTicker>? List { get; set; }
    }

    private sealed class BybitTicker
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("lastPrice")] public string? LastPrice { get; set; }
    }

    private sealed class BinanceTickerResponse
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
    }
}
