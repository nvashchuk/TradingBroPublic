using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Models;
using TradingBro.Strategies.PriceFeeds;

namespace TradingBro.Strategies.Catalog;

/// Binance USDⓈ-M perpetual catalog cache. Mirrors BybitFuturesInstrumentsCache.
/// Source: fapi.binance.com/fapi/v1/exchangeInfo
public sealed class BinanceFuturesInstrumentsCache(
    IHttpClientFactory httpClientFactory,
    ILogger<BinanceFuturesInstrumentsCache> logger)
    : IFuturesInstrumentsSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, FuturesInstrument> _byBase = new(StringComparer.Ordinal);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public Exchange Exchange => Exchange.Binance;

    public async Task<FuturesInstrument?> TryResolveAsync(string baseSymbol, CancellationToken ct)
    {
        await RefreshIfStaleAsync(ct);
        return _byBase.GetValueOrDefault(baseSymbol);
    }

    private async Task RefreshIfStaleAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < RefreshInterval)
        {
            return;
        }
        await _gate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastRefreshUtc < RefreshInterval)
            {
                return;
            }

            var client = httpClientFactory.CreateClient(RestPriceFeed.BinanceFuturesClient);
            const string url = "https://fapi.binance.com/fapi/v1/exchangeInfo";
            BinanceExchangeInfoResponse? resp;
            try
            {
                resp = await client.GetFromJsonAsync<BinanceExchangeInfoResponse>(url, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh Binance futures instruments — keeping previous snapshot");
                return;
            }

            var dict = new Dictionary<string, FuturesInstrument>(StringComparer.Ordinal);
            foreach (var sym in resp?.Symbols ?? new List<BinanceFuturesSymbol>())
            {
                if (sym.QuoteAsset != "USDT"
                    || sym.Status != "TRADING"
                    || sym.ContractType != "PERPETUAL"
                    || string.IsNullOrEmpty(sym.Symbol)
                    || string.IsNullOrEmpty(sym.BaseAsset)
                    || sym.OnboardDate is null)
                {
                    continue;
                }
                dict[sym.BaseAsset] = new FuturesInstrument(
                    Exchange: Exchange.Binance,
                    ContractSymbol: sym.Symbol,
                    BaseAsset: sym.BaseAsset,
                    QuoteAsset: "USDT",
                    LaunchTimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(sym.OnboardDate.Value).UtcDateTime);
            }

            _byBase = dict;
            _lastRefreshUtc = DateTime.UtcNow;
            logger.LogInformation("Binance futures cache refreshed: {Count} USDⓈ-M perp symbols", dict.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class BinanceExchangeInfoResponse
    {
        [JsonPropertyName("symbols")] public List<BinanceFuturesSymbol>? Symbols { get; set; }
    }

    private sealed class BinanceFuturesSymbol
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("baseAsset")] public string? BaseAsset { get; set; }
        [JsonPropertyName("quoteAsset")] public string? QuoteAsset { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("contractType")] public string? ContractType { get; set; }
        [JsonPropertyName("onboardDate")] public long? OnboardDate { get; set; }
    }
}
