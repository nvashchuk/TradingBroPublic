using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Gate;

public sealed class GateAnnouncementCollector(IHttpClientFactory httpClientFactory, ILogger<GateAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "GateApi";

    private const string Endpoint = "https://www.gate.io/api/web/v1/portal/announcement/list_article";
    private const string BaseUrl = "https://www.gate.com";
    private const int PageSize = 50;

    // Listing cate_ids: 61 (New Crypto Listings), 38 (New Spot Listings), 37 (New Futures Listings)
    // Delisting cate_ids: 15 (Delistings), 87 (ETF Delistings)
    private static readonly HashSet<int> RelevantCateIds = [61, 38, 37, 15, 87];

    public Exchange Exchange => Exchange.Gate;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<RawAnnouncement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var added = await FetchPageAsync(sinceUtc, results, seen, ct);
        logger.LogDebug("Gate: {Count} new announcements since {Since:O}", added, sinceUtc);

        return results;
    }

    private async Task<int> FetchPageAsync(
        DateTime sinceUtc,
        List<RawAnnouncement> sink,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var requestBody = new { page = 1, size = PageSize };

        GateAnnouncementsResponse? payload;
        try
        {
            var response = await client.PostAsJsonAsync(Endpoint, requestBody, ct);
            response.EnsureSuccessStatusCode();
            payload = await response.Content.ReadFromJsonAsync<GateAnnouncementsResponse>(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gate fetch failed (endpoint={Endpoint})", Endpoint);
            return 0;
        }

        if (payload?.Code is not 0 || payload.Data?.List is not { Count: > 0 } items)
        {
            logger.LogDebug("Gate response had no usable data (code={Code})", payload?.Code);
            return 0;
        }

        logger.LogDebug("Gate page fetched: {ItemCount} items (total={Total})", items.Count, payload.Data.Total);

        var added = 0;
        foreach (var item in items)
        {
            if (item.Id is null || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            if (item.CateId is null || !RelevantCateIds.Contains(item.CateId.Value))
            {
                continue;
            }

            // created_t is unix seconds (integer)
            if (item.CreatedT is null)
            {
                continue;
            }

            var announcedAt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedT.Value).UtcDateTime;
            if (announcedAt <= sinceUtc)
            {
                continue;
            }

            var externalId = item.Id.Value.ToString();
            if (!seen.Add(externalId))
            {
                continue;
            }

            // Combine relative URL with base
            var fullUrl = item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? item.Url
                : BaseUrl + item.Url;

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cate_id"] = item.CateId.Value.ToString(),
                ["id"] = externalId,
                ["url_path"] = item.Url,
                ["created_t"] = item.CreatedT.Value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(item.ReleaseTimestamp))
            {
                metadata["release_timestamp"] = item.ReleaseTimestamp;

                // Expose as ISO-8601 for classifier/parser convenience when parseable
                if (long.TryParse(item.ReleaseTimestamp, out var releaseSec))
                {
                    metadata["event_at_iso"] = DateTimeOffset.FromUnixTimeSeconds(releaseSec)
                        .UtcDateTime
                        .ToString("O");
                }
            }

            if (!string.IsNullOrWhiteSpace(item.Tags))
            {
                metadata["tags"] = item.Tags;
            }

            sink.Add(new RawAnnouncement(
                Exchange: Exchange.Gate,
                ExternalId: externalId,
                Title: item.Title.Trim(),
                Url: fullUrl,
                AnnouncedAt: announcedAt,
                BodyText: null,
                RawMetadata: metadata));
            added++;
        }

        return added;
    }
}
