using System.Text.RegularExpressions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Okx;

/// <summary>
/// Classifies OKX announcement titles into (Market, EventType) pairs.
/// OKX-specific quirks handled here:
///   - No dedicated delisting annType; must detect from title text.
///   - annType="announcements-new-listings" covers both spot and futures listings.
///   - Titles use the Unicode USDⓈ symbol (U+24C8) for the USDS stablecoin pair.
///   - Equity perpetual futures titles follow the pattern "perpetual futures for X equity/equities".
///   - Expiry perps / X-Perp listings also classify as Futures.
///   - "Spot Trade-to-Earn" and "Trade to Earn" promotions are not listings (Ignore).
///   - "network upgrade" and API/migration notices are infrastructure (Ignore).
/// </summary>
public static partial class OkxAnnouncementClassifier
{
    // -----------------------------------------------------------------------
    // Spot listing
    // SAMPLE: "OKX will launch AI/USDⓈ for spot trading"
    // SAMPLE: "OKX to list AI/USDT (Gensyn) for spot trading"
    // SAMPLE: "OKX will launch PROS/USDⓈ for spot trading"
    // SAMPLE: "OKX to list PROS/USDT (Pharos) for spot trading"
    // Matches: "OKX (will launch|to list) <TOKEN>/(USDT|USDⓈ|USDC|BTC|ETH|...) for spot trading"
    // Does NOT match equity perp titles because those say "perpetual futures for" (no slash pair).
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bOKX\s+(?:will\s+launch|to\s+list)\s+\S+/\S+\s+(?:\([^)]*\)\s+)?for\s+spot\s+trading\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpotListingRegex();

    // -----------------------------------------------------------------------
    // Futures listing — equity perpetual futures
    // SAMPLE: "OKX to list perpetual futures for GEV and URNM equities"
    // SAMPLE: "OKX to list perpetual futures for SOXL, NBIS, QCOM and CSCO equities"
    // SAMPLE: "OKX to list perpetual futures for GLW equity"
    // SAMPLE: "OKX to list perpetual futures for CBRS equity"
    // SAMPLE: "OKX to list perpetual futures for COHR equity"
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bOKX\s+to\s+list\s+perpetual\s+futures\s+for\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EquityFuturesListingRegex();

    // -----------------------------------------------------------------------
    // Futures listing — expiry perps / X-Perp / named perp contracts
    // SAMPLE: "OKX to list TAOUSD, BNBUSD, HYPEUSD, LINKUSD and TRXUSD Expiry Perps (X-Perp)"
    // Also catches generic "OKX to list ... futures" without equity keyword.
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bOKX\s+to\s+list\b.{0,120}?\b(?:perps?|futures|X-Perp)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FuturesListingBroadRegex();

    // -----------------------------------------------------------------------
    // Token/trading-pair delisting — title-based (no dedicated annType on OKX)
    // SAMPLE (earn product — intentionally excluded by requiring a ticker-ish context):
    //   "USDT on Morpho (Katana) delisting from Onchain Earn"
    //   → this contains "delisting from Onchain Earn" which we reject via the
    //     NegativeOnchainEarnRegex below.
    // Real trading pair delisting would read: "OKX to delist BTC/USDT", "OKX will remove XYZ spot pair", etc.
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\b(?:delist(?:ing|ed)?|remove\s+the\s+following|will\s+remove|discontinue\s+trading)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DelistingRegex();

    // -----------------------------------------------------------------------
    // Exclusion: "delisting from Onchain Earn" is a DeFi earn-product removal,
    // not a spot/futures trading-pair delisting. Return empty for these.
    // SAMPLE: "USDT on Morpho (Katana) delisting from Onchain Earn"
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bdelisting\s+from\s+(?:Onchain\s+Earn|Earn)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OnchainEarnDelistingRegex();

    // -----------------------------------------------------------------------
    // Ignore: spot trade-to-earn promotions
    // SAMPLE: "OKX × Gensyn (AI) Spot Trade-to-Earn: Trade and Share 500,000 USDT"
    // SAMPLE: "OKX x PROS Spot Trade to Earn: Trade & Share 600,000 PROS"
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bSpot\s+Trade[-\s]to[-\s]Earn\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TradeToEarnRegex();

    // -----------------------------------------------------------------------
    // Ignore: network upgrade / deposit-withdrawal infrastructure notices
    // SAMPLE: "OKX to support Base network upgrade"
    // SAMPLE: "OKX to support Sui network upgrade"
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bnetwork\s+upgrade\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NetworkUpgradeRegex();

    /// <summary>
    /// Classifies an OKX announcement into zero or more (Market, EventType) pairs.
    /// </summary>
    /// <param name="title">The announcement title (may be null or empty).</param>
    /// <param name="annType">
    ///   The OKX <c>annType</c> field (e.g. "announcements-new-listings", "latest-events").
    ///   Used as a secondary signal — title regex is the primary classifier.
    /// </param>
    /// <returns>
    ///   A read-only list of (Market, EventType) pairs. Returns <see cref="Array.Empty{T}"/>
    ///   for titles that represent infrastructure notices, promotions, or admin changes.
    /// </returns>
    public static IReadOnlyList<(Market market, EventType eventType)> Classify(
        string? title,
        string? annType = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        // --- Fast-path ignores (promos, infra) --------------------------------

        // "Spot Trade-to-Earn" / "Spot Trade to Earn" promotions
        if (TradeToEarnRegex().IsMatch(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        // Network upgrade notices (infra, not trading events)
        if (NetworkUpgradeRegex().IsMatch(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        // --- Delisting --------------------------------------------------------

        if (DelistingRegex().IsMatch(title))
        {
            // Exclude DeFi earn-product removals (not trading-pair delistings)
            if (OnchainEarnDelistingRegex().IsMatch(title))
            {
                return Array.Empty<(Market, EventType)>();
            }

            // Futures context: title explicitly mentions futures/perps
            var isFuturesDelisting = FuturesListingBroadRegex().IsMatch(title)
                || EquityFuturesListingRegex().IsMatch(title);
            var market = isFuturesDelisting ? Market.Futures : Market.Spot;
            return new[] { (market, EventType.Delisting) };
        }

        // --- Listings ---------------------------------------------------------

        var results = new List<(Market, EventType)>();

        // Spot listing: "OKX will launch X/USDⓈ for spot trading"
        //               "OKX to list X/USDT (...) for spot trading"
        if (SpotListingRegex().IsMatch(title))
        {
            results.Add((Market.Spot, EventType.Listing));
        }

        // Equity perpetual futures: "OKX to list perpetual futures for X equity/equities"
        if (EquityFuturesListingRegex().IsMatch(title))
        {
            results.Add((Market.Futures, EventType.Listing));
        }
        // Expiry perps / X-Perp / other futures (only if not already caught by equity regex
        // and not a spot listing that happened to match the broad regex)
        else if (FuturesListingBroadRegex().IsMatch(title) && results.Count == 0)
        {
            // Guard: ensure it is not a spot listing that contains the word "futures" coincidentally
            if (!SpotListingRegex().IsMatch(title))
            {
                results.Add((Market.Futures, EventType.Listing));
            }
        }

        return results.Count > 0
            ? results
            : Array.Empty<(Market, EventType)>();
    }
}
