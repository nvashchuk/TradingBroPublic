using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Models;
using TradingBro.Strategies.PriceFeeds;

namespace TradingBro.Strategies.Catalog;

/// Bybit USDT linear-perpetuals catalog (in-memory cache).
/// Pure existence/metadata source — does NOT decide minimum-age policy
/// (that's a strategy concern).
public sealed class BybitFuturesInstrumentsCache(
    IHttpClientFactory httpClientFactory,
    ILogger<BybitFuturesInstrumentsCache> logger)
    : IFuturesInstrumentsSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, FuturesInstrument> _byBase = new(StringComparer.Ordinal);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public Exchange Exchange => Exchange.Bybit;

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

            var client = httpClientFactory.CreateClient(RestPriceFeed.BybitClient);
            const string url = "https://api.bybit.com/v5/market/instruments-info?category=linear&limit=1000";
            BybitInstrumentsResponse? resp;
            try
            {
                resp = await client.GetFromJsonAsync<BybitInstrumentsResponse>(url, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh Bybit instruments — keeping previous snapshot");
                return;
            }

            var dict = new Dictionary<string, FuturesInstrument>(StringComparer.Ordinal);
            foreach (var inst in resp?.Result?.List ?? new List<BybitInstrumentRaw>())
            {
                // Skip dated futures (LinearFutures). Only USDT-perp here.
                if (inst.ContractType != "LinearPerpetual"
                    || inst.QuoteCoin != "USDT" || inst.Status != "Trading"
                    || string.IsNullOrEmpty(inst.Symbol) || string.IsNullOrEmpty(inst.BaseCoin)
                    || !long.TryParse(inst.LaunchTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var launchMs))
                {
                    continue;
                }
                dict[inst.BaseCoin] = new FuturesInstrument(
                    Exchange: Exchange.Bybit,
                    ContractSymbol: inst.Symbol,
                    BaseAsset: inst.BaseCoin,
                    QuoteAsset: "USDT",
                    LaunchTimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(launchMs).UtcDateTime);
            }

            _byBase = dict;
            _lastRefreshUtc = DateTime.UtcNow;
            logger.LogInformation("Bybit futures cache refreshed: {Count} USDT-perp symbols", dict.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class BybitInstrumentsResponse
    {
        [JsonPropertyName("retCode")] public int RetCode { get; set; }
        [JsonPropertyName("result")] public BybitInstrumentsResult? Result { get; set; }
    }

    private sealed class BybitInstrumentsResult
    {
        [JsonPropertyName("list")] public List<BybitInstrumentRaw>? List { get; set; }
    }

    private sealed class BybitInstrumentRaw
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("baseCoin")] public string? BaseCoin { get; set; }
        [JsonPropertyName("quoteCoin")] public string? QuoteCoin { get; set; }
        [JsonPropertyName("contractType")] public string? ContractType { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("launchTime")] public string? LaunchTime { get; set; }
    }
}
