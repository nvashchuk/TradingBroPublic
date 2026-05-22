using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bithumb;

public sealed partial class BithumbAnnouncementCollector(
    IHttpClientFactory httpClientFactory,
    ILogger<BithumbAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "BithumbApi";

    // `count=20` is the maximum that returns the actual newest items.
    // Higher values (30+) silently fall back to 5 featured notices.
    private const string Endpoint = "https://api.bithumb.com/v1/notices?count=20";

    // published_at format: "2026-05-18 19:00:00" in KST.
    private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    [GeneratedRegex(@"/notice/(\d+)")]
    private static partial Regex NoticeIdRegex();

    public Exchange Exchange => Exchange.Bithumb;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        List<BithumbNotice>? notices;
        try
        {
            notices = await client.GetFromJsonAsync<List<BithumbNotice>>(Endpoint, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bithumb fetch failed");
            return Array.Empty<RawAnnouncement>();
        }

        if (notices is not { Count: > 0 })
        {
            return Array.Empty<RawAnnouncement>();
        }

        var results = new List<RawAnnouncement>();
        foreach (var n in notices)
        {
            if (string.IsNullOrWhiteSpace(n.Title) || string.IsNullOrWhiteSpace(n.PcUrl) || string.IsNullOrWhiteSpace(n.PublishedAt))
            {
                continue;
            }
            if (!TryParseKstAsUtc(n.PublishedAt!, out var publishedUtc))
            {
                continue;
            }
            if (publishedUtc <= sinceUtc)
            {
                continue;
            }

            var externalId = ExtractNoticeId(n.PcUrl!);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["categories"] = string.Join("|", n.Categories ?? new List<string>()),
            };

            results.Add(new RawAnnouncement(
                Exchange: Exchange.Bithumb,
                ExternalId: externalId,
                Title: n.Title!.Trim(),
                Url: n.PcUrl!,
                AnnouncedAt: publishedUtc,
                BodyText: null,
                RawMetadata: metadata));
        }
        return results;
    }

    private static bool TryParseKstAsUtc(string raw, out DateTime utc)
    {
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
        {
            utc = DateTime.SpecifyKind(local - KstOffset, DateTimeKind.Utc);
            return true;
        }
        utc = default;
        return false;
    }

    private static string ExtractNoticeId(string url)
    {
        var m = NoticeIdRegex().Match(url);
        return m.Success ? m.Groups[1].Value : url;
    }
}
