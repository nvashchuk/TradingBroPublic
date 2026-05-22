using System.Text.RegularExpressions;
using TradingBro.Core.Models;
using TradingBro.Collectors.Common;

namespace TradingBro.Collectors.Binance;

public static partial class BinanceAnnouncementClassifier
{
    [GeneratedRegex(@"binance\s+futures\s+will\s+(launch|add|list)|will\s+launch.*(usd[sⓈ]?\s*-?\s*m|coin\s*-?\s*m|usdc\s*-?\s*margined|usdt\s*-?\s*margined).*perpetual|perpetual\s+contract.*will\s+(launch|list)", RegexOptions.IgnoreCase)]
    private static partial Regex FuturesListRegex();

    [GeneratedRegex(@"binance\s+(will\s+list|lists|will\s+add|adds)|innovation\s+zone|introducing.*on\s+binance\s+(launchpool|launchpad|megadrop)", RegexOptions.IgnoreCase)]
    private static partial Regex SpotListRegex();

    [GeneratedRegex(@"binance\s+futures\s+will\s+(delist|remove|close|settle)|(usd[sⓈ]?\s*-?\s*m|coin\s*-?\s*m).*perpetual.*(delist|remov|settl|clos)", RegexOptions.IgnoreCase)]
    private static partial Regex FuturesDelistRegex();

    [GeneratedRegex(@"binance\s+will\s+delist|notice\s+(on|of)\s+removal|binance\s+delists|removal\s+of\s+spot\s+trading\s+pairs", RegexOptions.IgnoreCase)]
    private static partial Regex SpotDelistRegex();

    [GeneratedRegex(@"binance\s+futures|perpetual|(usd[sⓈ]?\s*-?\s*m|coin\s*-?\s*m)\b|usdc\s*-?\s*margined|usdt\s*-?\s*margined", RegexOptions.IgnoreCase)]
    private static partial Regex FuturesHintRegex();

    [GeneratedRegex(@"introducing.*on\s+binance\s+(launchpool|launchpad|megadrop)", RegexOptions.IgnoreCase)]
    private static partial Regex LaunchpoolRegex();

    [GeneratedRegex(@"perpetual\s+contract\s+pre-?market\s+trading|pre-?market\s+perpetual\s+contract|will\s+launch.*pre-?market.*perpetual", RegexOptions.IgnoreCase)]
    private static partial Regex PreMarketRegex();

    /// Classify a Binance announcement title into one or more (Market, EventType) combinations.
    /// A single title can match multiple markets (e.g. "Binance Adds XAI on Earn, Margin, Futures").
    public static IReadOnlyList<(Market market, EventType eventType)> Classify(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        var results = new List<(Market, EventType)>();
        var futuresHint = FuturesHintRegex().IsMatch(title);
        var isPreMarket = PreMarketRegex().IsMatch(title);

        if (FuturesDelistRegex().IsMatch(title))
        {
            results.Add((Market.Futures, EventType.Delisting));
        }
        if (SpotDelistRegex().IsMatch(title) && !futuresHint)
        {
            results.Add((Market.Spot, EventType.Delisting));
        }

        if (FuturesListRegex().IsMatch(title))
        {
            results.Add((isPreMarket ? Market.PreMarket : Market.Futures, EventType.Listing));
        }
        if (SpotListRegex().IsMatch(title) && !futuresHint)
        {
            var eventType = LaunchpoolRegex().IsMatch(title) ? EventType.LaunchpoolAnnounce : EventType.Listing;
            results.Add((Market.Spot, eventType));
        }

        return results;
    }
}
