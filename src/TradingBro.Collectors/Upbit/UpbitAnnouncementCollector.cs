using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Upbit;

public sealed class UpbitAnnouncementCollector(IHttpClientFactory httpClientFactory, ILogger<UpbitAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "UpbitApi";

    // Upbit API caps per_page at 30 (50+ returns HTTP 400).
    private const string Endpoint = "https://api-manager.upbit.com/api/v1/announcements";
    private const int PageSize = 30;
    private const string Category = "trade";

    public Exchange Exchange => Exchange.Upbit;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<RawAnnouncement>();
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{Endpoint}?os=web&category={Category}&page=1&per_page={PageSize}";

        UpbitAnnouncementsResponse? payload;
        try
        {
            payload = await client.GetFromJsonAsync<UpbitAnnouncementsResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upbit fetch failed");
            return results;
        }

        if (payload is not { Success: true } || payload.Data?.Notices is not { Count: > 0 } notices)
        {
            return results;
        }

        foreach (var n in notices)
        {
            if (string.IsNullOrEmpty(n.Title) || string.IsNullOrEmpty(n.ListedAt))
            {
                continue;
            }
            if (!DateTime.TryParse(n.ListedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var publishUtc))
            {
                continue;
            }
            if (publishUtc <= sinceUtc)
            {
                continue;
            }

            var id = n.Id.ToString(CultureInfo.InvariantCulture);
            results.Add(new RawAnnouncement(
                Exchange: Exchange.Upbit,
                ExternalId: id,
                Title: n.Title!.Trim(),
                Url: $"https://upbit.com/service_center/notice?id={id}",
                AnnouncedAt: publishUtc));
        }
        return results;
    }
}
