using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Binance;

public sealed class BinanceAnnouncementCollector(IHttpClientFactory httpClientFactory, ILogger<BinanceAnnouncementCollector> logger)
    : IAnnouncementCollector
{
    public const string HttpClientName = "BinanceCms";

    private const string CmsListUrl = "https://www.binance.com/bapi/composite/v1/public/cms/article/list/query";
    private const string ArticleUrlTmpl = "https://www.binance.com/en/support/announcement/{0}";
    private const int CatalogListings = 48;
    private const int CatalogDelistings = 161;
    private const int PageSize = 50;

    public Exchange Exchange => Exchange.Binance;

    public async Task<IReadOnlyList<RawAnnouncement>> PollAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var results = new List<RawAnnouncement>();
        await FetchCatalogAsync(CatalogListings, sinceUtc, results, ct);
        await FetchCatalogAsync(CatalogDelistings, sinceUtc, results, ct);
        return results;
    }

    private async Task FetchCatalogAsync(int catalogId, DateTime sinceUtc, List<RawAnnouncement> sink, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{CmsListUrl}?type=1&catalogId={catalogId}&pageNo=1&pageSize={PageSize}";

        BinanceCmsListResponse? payload;
        try
        {
            payload = await client.GetFromJsonAsync<BinanceCmsListResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Binance CMS fetch failed (catalog={Catalog})", catalogId);
            return;
        }

        var articles = ExtractArticles(payload);
        if (articles.Count == 0)
        {
            return;
        }

        foreach (var a in articles)
        {
            if (a.ReleaseDate is null || string.IsNullOrEmpty(a.Code) || string.IsNullOrEmpty(a.Title))
            {
                continue;
            }
            var releaseUtc = DateTimeOffset.FromUnixTimeMilliseconds(a.ReleaseDate.Value).UtcDateTime;
            if (releaseUtc <= sinceUtc)
            {
                continue;
            }

            sink.Add(new RawAnnouncement(
                Exchange: Exchange.Binance,
                ExternalId: a.Code!,
                Title: a.Title!.Trim(),
                Url: string.Format(ArticleUrlTmpl, a.Code),
                AnnouncedAt: releaseUtc));
        }
    }

    private static List<BinanceCmsArticle> ExtractArticles(BinanceCmsListResponse? payload)
    {
        if (payload?.Data is null)
        {
            return new List<BinanceCmsArticle>();
        }
        if (payload.Data.Articles is { Count: > 0 })
        {
            return payload.Data.Articles;
        }
        if (payload.Data.Catalogs is { Count: > 0 })
        {
            return payload.Data.Catalogs
                .SelectMany(c => c.Articles ?? Enumerable.Empty<BinanceCmsArticle>())
                .ToList();
        }
        return new List<BinanceCmsArticle>();
    }
}
