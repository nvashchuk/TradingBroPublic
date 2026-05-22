using System.Text.RegularExpressions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Bybit;

public static partial class BybitAnnouncementClassifier
{
    [GeneratedRegex(@"\bnew\s+listing[:\s].{0,80}?\bperpetual\s+(?:contract|pre-?market)\b|\blisting\s+of\s+\S+\s+on\s+(?:bybit\s+)?perpetual\s+pre-?market\b|\bbybit\s+futures\s+(?:will\s+launch|launches|to\s+launch)\b|\bconvert(?:ing)?\b.{0,80}?\bpre-?market\b.{0,80}?\bto\s+(?:standard|regular)\b.{0,80}?\bperpetual\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FuturesListRegex();

    [GeneratedRegex(@"\bbybit\s+(?:to\s+list|will\s+list|lists)\b.{0,80}?\bon\s+spot\b|\bnew\s+listing[:\s].{0,80}?\bon\s+spot\b|\bnow\s+live\s+on\s+bybit\s+spot\b|\blaunch\s+on\s+bybit\s+spot\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpotListRegex();

    [GeneratedRegex(@"delisting\s+of|remove\s+(the\s+)?following|will\s+delist|delist\s+(of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex DelistRegex();

    [GeneratedRegex(@"\bpre-?market\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreMarketRegex();

    [GeneratedRegex(@"\bbybit\s+launchpool\b|\bbybit\s+launchpad\b", RegexOptions.IgnoreCase)]
    private static partial Regex LaunchpoolRegex();

    public static IReadOnlyList<(Market market, EventType eventType)> Classify(
        string title,
        string apiType,
        IReadOnlyList<string> tags)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        var isDerivatives = tags.Any(t => string.Equals(t, "Derivatives", StringComparison.OrdinalIgnoreCase));
        var isDelisting = string.Equals(apiType, "delistings", StringComparison.OrdinalIgnoreCase)
            || DelistRegex().IsMatch(title);

        if (isDelisting)
        {
            var market = FuturesListRegex().IsMatch(title) || isDerivatives ? Market.Futures : Market.Spot;
            return new[] { (market, EventType.Delisting) };
        }

        var results = new List<(Market, EventType)>();
        if (FuturesListRegex().IsMatch(title))
        {
            var market = PreMarketRegex().IsMatch(title) ? Market.PreMarket : Market.Futures;
            results.Add((market, EventType.Listing));
        }
        if (SpotListRegex().IsMatch(title))
        {
            results.Add((Market.Spot, EventType.Listing));
        }
        if (results.Count == 0 && LaunchpoolRegex().IsMatch(title))
        {
            results.Add((Market.Spot, EventType.LaunchpoolAnnounce));
        }
        return results;
    }
}
