using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bithumb;

public static class BithumbAnnouncementClassifier
{
    private static readonly HashSet<string> ListingCategories = new(StringComparer.Ordinal)
    {
        "마켓 추가",
    };

    private static readonly HashSet<string> DelistCategories = new(StringComparer.Ordinal)
    {
        "거래지원종료",
        "거래유의/거래지원종료",
    };

    /// Classifies a Bithumb notice. Category-driven (notice has explicit `categories` field):
    ///   "마켓 추가" → new KRW market addition (listing)
    ///   "거래지원종료" → trade support terminated (delisting)
    /// Title fallback for older notices that lack the category.
    public static IReadOnlyList<(Market market, EventType eventType)> Classify(
        string title,
        IReadOnlyList<string> categories)
    {
        if (categories.Any(DelistCategories.Contains))
        {
            return new[] { (Market.Spot, EventType.Delisting) };
        }
        if (categories.Any(ListingCategories.Contains))
        {
            return new[] { (Market.Spot, EventType.Listing) };
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }
        if (title.Contains("거래지원 종료", StringComparison.Ordinal) || title.Contains("상장 폐지", StringComparison.Ordinal))
        {
            return new[] { (Market.Spot, EventType.Delisting) };
        }
        if (title.Contains("원화 마켓 추가", StringComparison.Ordinal))
        {
            return new[] { (Market.Spot, EventType.Listing) };
        }
        return Array.Empty<(Market, EventType)>();
    }
}
