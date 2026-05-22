using System.Text.RegularExpressions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Gate;

/// <summary>
/// Classifies Gate announcement titles into (Market, EventType) pairs.
///
/// Gate-specific design:
///   - <c>cate_id</c> is the primary signal (sourced from the raw API response field of the same name).
///     Listing cate_ids : 61 (New Crypto Listings), 38 (New Spot Listings), 37 (New Futures Listings).
///     Delisting cate_ids: 15 (Delistings), 87 (ETF Delistings).
///   - Title-regex is the backstop: disambiguates futures vs spot within cate_id 61,
///     and detects futures delistings within cate_id 15.
///   - All other cate_ids produce an empty result (promotions, earn, research, etc.).
/// </summary>
public static partial class GateAnnouncementClassifier
{
    // -----------------------------------------------------------------------
    // Futures signal — title backstop within listing and delisting paths.
    //
    // SAMPLE (cate_id=61): "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)"
    // SAMPLE (cate_id=37): "Gate Will List XXXUSDT Perpetual Futures (USDT-M)"
    // SAMPLE (cate_id=37): "Gate Will Launch BTC Quarterly Futures Contract"
    // SAMPLE (cate_id=15): "Gate Futures Will Delist XXXUSDT (USDT-M) Perpetual Contracts"
    //
    // Matches: perpetual, futures, usdt-m, coin-m, contract (in a futures-trading context).
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bperpetual\b|\bfutures\b|\busdt[-\s]?m\b|\bcoin[-\s]?m\b|\bperp\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FuturesHintRegex();

    // -----------------------------------------------------------------------
    // Pre-market signal — a sub-type of futures listing.
    //
    // SAMPLE (cate_id=61): "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)"
    // SAMPLE (cate_id=61): "Gate Pre-Market Trading for TOKENUSDT Perpetual Futures Now Live"
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bpre[-\s]?market\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PreMarketRegex();

    // -----------------------------------------------------------------------
    // Spot listing signal — title backstop for cate_id 61 articles that are
    // clearly spot rather than futures/pre-market.
    //
    // SAMPLE (cate_id=61): "Gate Lists TOKEN on Spot"
    // SAMPLE (cate_id=38): "New Spot Listing: TOKEN/USDT Now Available"
    // SAMPLE (cate_id=38): "Gate Will List TOKEN/USDT on Spot Trading"
    //
    // Detects: "on spot", "spot listing", "spot trading", "spot market",
    //          "TOKEN/USDT" pair pattern (slash-separated ticker).
    // -----------------------------------------------------------------------
    [GeneratedRegex(
        @"\bon\s+spot\b|\bspot\s+(?:listing|trading|market)\b|\b[A-Z]{2,10}/(?:USDT|USDC|BTC|ETH|BNB|USD)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpotHintRegex();

    // -----------------------------------------------------------------------
    // Delisting futures signal — within cate_id 15 (Delistings).
    //
    // SAMPLE (cate_id=15): "Gate Futures Will Delist XXXUSDT Perpetual Contract (USDT-M)"
    // SAMPLE (cate_id=15): "Gate Will Settle YYUSDT Quarterly Futures"
    // SAMPLE (cate_id=87): "Gate Will Delist BTC3L/USDT (ETF)"
    //
    // Uses FuturesHintRegex for futures context; remaining delistings default to spot.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Classifies a Gate announcement into zero or more (Market, EventType) pairs.
    /// </summary>
    /// <param name="title">
    ///   The announcement title string. Null or whitespace returns an empty list.
    /// </param>
    /// <param name="cateId">
    ///   The <c>cate_id</c> integer from the Gate API response.
    ///   This is the primary classification signal.
    ///   <list type="bullet">
    ///     <item>61 — New Crypto Listings (parent; may contain spot, futures, or pre-market)</item>
    ///     <item>38 — New Spot Listings</item>
    ///     <item>37 — New Futures Listings</item>
    ///     <item>15 — Delistings</item>
    ///     <item>87 — ETF Delistings</item>
    ///     <item>anything else — not a listing/delisting event; returns empty</item>
    ///   </list>
    /// </param>
    /// <returns>
    ///   A read-only list of (Market, EventType) pairs.
    ///   Returns <see cref="Array.Empty{T}"/> for irrelevant announcements.
    /// </returns>
    public static IReadOnlyList<(Market market, EventType eventType)> Classify(
        string? title,
        int cateId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<(Market, EventType)>();
        }

        // ------------------------------------------------------------------
        // Delisting path — cate_id 15 (Delistings) or 87 (ETF Delistings)
        // ------------------------------------------------------------------
        if (cateId is 15 or 87)
        {
            // ETF delistings are always spot-side instruments (leveraged tokens).
            // For cate_id 15, use the futures-hint regex to distinguish.
            var market = cateId == 87
                ? Market.Spot
                : FuturesHintRegex().IsMatch(title) ? Market.Futures : Market.Spot;

            return new[] { (market, EventType.Delisting) };
        }

        // ------------------------------------------------------------------
        // New Spot Listings — cate_id 38
        // Titles always describe a spot pair; no further disambiguation needed.
        //
        // SAMPLE: "Gate Will List TOKEN/USDT on Spot Trading"
        // SAMPLE: "New Spot Listing: TOKEN/USDT Now Available"
        // ------------------------------------------------------------------
        if (cateId == 38)
        {
            return new[] { (Market.Spot, EventType.Listing) };
        }

        // ------------------------------------------------------------------
        // New Futures Listings — cate_id 37
        // Titles always describe a futures contract; use pre-market regex to
        // refine to PreMarket when relevant.
        //
        // SAMPLE: "Gate Will Launch XXXUSDT Perpetual Futures (USDT-M)"
        // SAMPLE: "Gate Launches Pre-Market Trading for XXXUSDT Perpetual Futures (USDT-M)"
        // ------------------------------------------------------------------
        if (cateId == 37)
        {
            var market = PreMarketRegex().IsMatch(title) ? Market.PreMarket : Market.Futures;
            return new[] { (market, EventType.Listing) };
        }

        // ------------------------------------------------------------------
        // New Crypto Listings — cate_id 61
        // This is the "generic" listing category that includes spot, futures,
        // and pre-market entries. Use title-regex to disambiguate.
        //
        // SAMPLE (futures/pre-market):
        //   "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)"
        //     → PreMarket Listing  (has "pre-market" + "perpetual")
        //
        // SAMPLE (futures, no pre-market):
        //   "Gate Will List TOKENUSDT Perpetual Futures (USDT-M)"
        //     → Futures Listing
        //
        // SAMPLE (spot):
        //   "Gate Lists TOKEN/USDT on Spot"
        //     → Spot Listing
        //
        // Fallback for ambiguous titles (neither spot hint nor futures hint):
        //   Default to Spot — most "new token" announcements without explicit
        //   futures keywords are spot listings on Gate.
        // ------------------------------------------------------------------
        if (cateId == 61)
        {
            var hasFutures = FuturesHintRegex().IsMatch(title);
            var hasPreMarket = PreMarketRegex().IsMatch(title);
            var hasSpot = SpotHintRegex().IsMatch(title);

            if (hasFutures || hasPreMarket)
            {
                // If there is a pre-market signal, classify as PreMarket.
                // If there is a futures signal but no spot signal, classify as Futures.
                // If BOTH spot and futures signals are present, emit both.
                var results = new List<(Market, EventType)>();

                if (hasPreMarket)
                {
                    results.Add((Market.PreMarket, EventType.Listing));
                }
                else
                {
                    results.Add((Market.Futures, EventType.Listing));
                }

                // If the title also explicitly mentions spot (rare but possible for combo listings)
                if (hasSpot && !hasPreMarket)
                {
                    results.Add((Market.Spot, EventType.Listing));
                }

                return results;
            }

            // No futures/pre-market signal — treat as spot listing (default for cate_id 61).
            return new[] { (Market.Spot, EventType.Listing) };
        }

        // ------------------------------------------------------------------
        // All other cate_ids — promotions, earn, research, copy-trading, etc.
        // Not a listing or delisting event.
        // ------------------------------------------------------------------
        return Array.Empty<(Market, EventType)>();
    }
}
