using System.Text.RegularExpressions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Upbit;

public static partial class UpbitAnnouncementClassifier
{
    [GeneratedRegex(@"신규\s*거래지원|신규\s*상장|거래지원\s*개시|상장\s*안내")]
    private static partial Regex ListingRegex();

    [GeneratedRegex(@"거래지원\s*종료|상장\s*폐지|거래\s*종료")]
    private static partial Regex DelistRegex();

    [GeneratedRegex(@"유의\s*종목|투자\s*유의|투자유의")]
    private static partial Regex CautionRegex();

    public static IReadOnlyList<(Market market, EventType eventType)> Classify(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }
        if (CautionRegex().IsMatch(title))
        {
            return Array.Empty<(Market, EventType)>();
        }
        if (DelistRegex().IsMatch(title))
        {
            return new[] { (Market.Spot, EventType.Delisting) };
        }
        if (ListingRegex().IsMatch(title))
        {
            return new[] { (Market.Spot, EventType.Listing) };
        }
        return Array.Empty<(Market, EventType)>();
    }
}
