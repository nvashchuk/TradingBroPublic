using System.Text.RegularExpressions;
using TradingBro.Collectors.Common;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Okx;

public sealed partial class OkxAnnouncementParser : IAnnouncementParser
{
    public Exchange Exchange => Exchange.Okx;

    // -----------------------------------------------------------------------
    // Spot pair: captures the base token from "TOKEN/QUOTE" in a title.
    // SAMPLE: "OKX will launch AI/USDⓈ for spot trading"    → group 1 = "AI"
    // SAMPLE: "OKX to list PROS/USDT (Pharos) for spot trading" → group 1 = "PROS"
    // The quote side may be USDT, USDC, USDⓈ, BTC, ETH, etc.; we capture only the
    // base (left of the slash), which is 2-15 uppercase alphanumeric characters.
    // -----------------------------------------------------------------------
    [GeneratedRegex(@"\b([A-Z0-9]{2,15})/\S+\s+(?:\([^)]*\)\s+)?for\s+spot\s+trading\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpotPairBaseRegex();

    // -----------------------------------------------------------------------
    // Equity perps: captures ticker list from
    // "... perpetual futures for TOKEN1, TOKEN2 and TOKEN3 equit(y|ies)"
    // SAMPLE: "OKX to list perpetual futures for GLW equity"
    //   → captures "GLW"
    // SAMPLE: "OKX to list perpetual futures for SOXL, NBIS, QCOM and CSCO equities"
    //   → captures "SOXL, NBIS, QCOM and CSCO"  (full list group)
    // After capture, individual tickers are split on [,\s]+ or " and ".
    // -----------------------------------------------------------------------
    [GeneratedRegex(@"\bperpetual\s+futures\s+for\s+([\w,\s]+?)\s+equit(?:y|ies)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EquityTickerListRegex();

    public IReadOnlyList<Announcement> Parse(RawAnnouncement raw)
    {
        // --- Read metadata written by OkxAnnouncementCollector ---------------
        var annType = raw.RawMetadata?.GetValueOrDefault("okx.annType") ?? string.Empty;

        // businessPTime is stored as ISO-8601 ("O" format) by the collector.
        DateTime? eventAt = null;
        if (raw.RawMetadata?.TryGetValue("okx.businessPTime", out var businessPTimeStr) is true
            && DateTime.TryParse(
                businessPTimeStr,
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsedEventAt))
        {
            eventAt = parsedEventAt;
        }

        // --- Classify --------------------------------------------------------
        var classifications = OkxAnnouncementClassifier.Classify(raw.Title, annType);
        if (classifications.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        // --- Extract symbols -------------------------------------------------
        var symbols = ExtractSymbols(raw.Title);
        if (symbols.Count == 0)
        {
            return Array.Empty<Announcement>();
        }

        // --- Fan-out: symbol × classification --------------------------------
        var output = new List<Announcement>(symbols.Count * classifications.Count);
        foreach (var symbol in symbols)
        {
            foreach (var (market, eventType) in classifications)
            {
                output.Add(new Announcement
                {
                    Id = Guid.NewGuid(),
                    Exchange = Exchange.Okx,
                    Symbol = symbol,
                    Market = market,
                    EventType = eventType,
                    AnnouncedAt = raw.AnnouncedAt,
                    EventAt = eventAt,
                    Title = raw.Title,
                    Url = raw.Url,
                    BodyText = raw.BodyText,
                    ExternalId = raw.ExternalId,
                });
            }
        }
        return output;
    }

    // -----------------------------------------------------------------------
    // OKX-specific symbol extraction.
    //
    // Priority:
    //   1. Spot pair: extract base token from "TOKEN/QUOTE ... for spot trading".
    //      Handles USDⓈ (U+24C8) and any other quote currency.
    //   2. Equity perpetual futures: extract each ticker from
    //      "perpetual futures for T1, T2 and T3 equit(y|ies)".
    //   3. Fallback: SymbolExtraction.ExtractFromTitle (handles paren tokens and
    //      concatenated BASEUSDT pairs used by other OKX title patterns).
    // -----------------------------------------------------------------------
    private static IReadOnlyList<string> ExtractSymbols(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<string>();
        }

        var upper = title.ToUpperInvariant();

        // 1. Spot pair base token ("AI" from "AI/USDⓈ")
        var spotMatch = SpotPairBaseRegex().Match(title);
        if (spotMatch.Success)
        {
            var tok = spotMatch.Groups[1].Value.ToUpperInvariant();
            if (tok.Length >= 2 && tok.Length <= 15)
            {
                return new[] { tok };
            }
        }

        // 2. Equity ticker list ("GLW" or "SOXL","NBIS","QCOM","CSCO")
        var equityMatch = EquityTickerListRegex().Match(title);
        if (equityMatch.Success)
        {
            var rawList = equityMatch.Groups[1].Value;
            // Split on commas and the word "and", then trim whitespace
            var parts = rawList
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().ToUpperInvariant())
                .Where(p => p != "AND" && p.Length >= 2 && p.Length <= 12
                            && p.All(c => char.IsLetterOrDigit(c)))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (parts.Count > 0)
            {
                return parts;
            }
        }

        // 3. Fallback to common extraction (paren tokens, BASEUSDT pairs)
        return SymbolExtraction.ExtractFromTitle(upper);
    }
}
