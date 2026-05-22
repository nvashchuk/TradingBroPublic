using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bybit;

public sealed class BybitAnnouncementCollector(IHttpClientFactory httpClientFactory, ILogger<BybitAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "BybitApi";

    private const string Endpoint = "https://api.bybit.com/v5/announcements/index";
    private const int PageSize = 50;
    private static readonly string[] Types = ["new_crypto", "delistings", "latest_activities"];

    public Exchange Exchange => Exchange.Bybit;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<RawAnnouncement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in Types)
        {
            var added = await FetchTypeAsync(type, sinceUtc, results, seen, ct);
            logger.LogDebug("Bybit type={Type}: {Count} new", type, added);
        }
        return results;
    }

    private async Task<int> FetchTypeAsync(
        string type,
        DateTime sinceUtc,
        List<RawAnnouncement> sink,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{Endpoint}?locale=en-US&type={type}&limit={PageSize}&page=1";

        BybitAnnouncementsResponse? payload;
        try
        {
            payload = await client.GetFromJsonAsync<BybitAnnouncementsResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bybit fetch failed (type={Type})", type);
            return 0;
        }

        if (payload?.RetMsg is not "OK" || payload.Result?.List is not { Count: > 0 } items)
        {
            return 0;
        }

        var added = 0;
        foreach (var a in items)
        {
            if (a.PublishTime is null || string.IsNullOrWhiteSpace(a.Url) || string.IsNullOrWhiteSpace(a.Title))
            {
                continue;
            }
            var publishUtc = DateTimeOffset.FromUnixTimeMilliseconds(a.PublishTime.Value).UtcDateTime;
            if (publishUtc <= sinceUtc)
            {
                continue;
            }

            var externalId = ExtractCodeFromUrl(a.Url!);
            if (!seen.Add(externalId))
            {
                continue;
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["api_type"] = type,
                ["tags"] = string.Join("|", a.Tags ?? new List<string>()),
            };
            if (a.DateTimestamp is not null)
            {
                metadata["event_at"] = DateTimeOffset.FromUnixTimeMilliseconds(a.DateTimestamp.Value).UtcDateTime.ToString("O");
            }

            sink.Add(new RawAnnouncement(
                Exchange: Exchange.Bybit,
                ExternalId: externalId,
                Title: a.Title!.Trim(),
                Url: a.Url!,
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
