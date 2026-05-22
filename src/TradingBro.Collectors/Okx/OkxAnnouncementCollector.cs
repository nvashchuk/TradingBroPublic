using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Okx;

public sealed class OkxAnnouncementCollector(IHttpClientFactory httpClientFactory, ILogger<OkxAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "okx-announcements";

    private const string Endpoint = "https://www.okx.com/api/v5/support/announcements";
    private const string Locale = "en-US";
    private const int PageSize = 20;

    public Exchange Exchange => Exchange.Okx;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<RawAnnouncement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var added = await FetchFeedAsync(sinceUtc, results, seen, ct);
        logger.LogDebug("OKX mixed feed: {Count} new announcements since {Since:O}", added, sinceUtc);

        return results;
    }

    private async Task<int> FetchFeedAsync(
        DateTime sinceUtc,
        List<RawAnnouncement> sink,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{Endpoint}?locale={Locale}&limit={PageSize}";

        OkxAnnouncementsResponse? payload;
        try
        {
            payload = await client.GetFromJsonAsync<OkxAnnouncementsResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OKX fetch failed (url={Url})", url);
            return 0;
        }

        if (payload?.Code is not "0" || payload.Data is not { Count: > 0 } pages)
        {
            logger.LogDebug("OKX response had no usable data (code={Code})", payload?.Code);
            return 0;
        }

        // Items live at /data/0/details
        var page = pages[0];
        if (page.Details is not { Count: > 0 } items)
        {
            return 0;
        }

        logger.LogDebug("OKX page fetched: {ItemCount} items (totalPage={TotalPage})", items.Count, page.TotalPage);

        var added = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.PTime))
            {
                continue;
            }

            // pTime is a string of unix milliseconds
            if (!long.TryParse(item.PTime, out var pTimeMs))
            {
                logger.LogWarning("OKX: could not parse pTime={PTime} for url={Url}", item.PTime, item.Url);
                continue;
            }

            var publishUtc = DateTimeOffset.FromUnixTimeMilliseconds(pTimeMs).UtcDateTime;
            if (publishUtc <= sinceUtc)
            {
                continue;
            }

            var externalId = ExtractCodeFromUrl(item.Url);
            if (!seen.Add(externalId))
            {
                continue;
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(item.AnnType))
            {
                metadata["okx.annType"] = item.AnnType;
            }

            if (!string.IsNullOrWhiteSpace(item.BusinessPTime)
                && long.TryParse(item.BusinessPTime, out var businessPTimeMs))
            {
                metadata["okx.businessPTime"] = DateTimeOffset.FromUnixTimeMilliseconds(businessPTimeMs)
                    .UtcDateTime
                    .ToString("O");
            }

            sink.Add(new RawAnnouncement(
                Exchange: Exchange.Okx,
                ExternalId: externalId,
                Title: item.Title.Trim(),
                Url: item.Url,
                AnnouncedAt: publishUtc,
                BodyText: null,
                RawMetadata: metadata));
            added++;
        }

        return added;
    }

    private static string ExtractCodeFromUrl(string url)
    {
        var idx = url.LastIndexOf('/');
        if (idx < 0 || idx + 1 >= url.Length)
        {
            return url;
        }
        var tail = url[(idx + 1)..].TrimEnd('/');
        return string.IsNullOrEmpty(tail) ? url : tail;
    }
}
