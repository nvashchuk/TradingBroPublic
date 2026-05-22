using System.Text.RegularExpressions;

namespace TradingBro.Collectors.Common;

internal static partial class SymbolExtraction
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "USDT","USDC","BUSD","BTC","ETH","BNB","USD","TUSD","FDUSD","DAI","PAX","USDP",
        "EUR","GBP","TRY","BRL","ARS","RUB","NGN","UAH","ZAR","JPY","AUD","RON","PLN","IDRT",
        "KRW","CNY","HKD","BINANCE","BYBIT","UPBIT","BITHUMB","SPOT","FUTURES","PERPETUAL",
        "PRE","MARKET","TRADING","TRADE","CONTRACT","CONTRACTS","WILL","LIST","LISTING",
        "DELIST","DELISTING","ADD","ADDS","NEW","TOKEN","COIN","NETWORK","NOTICE","ANNOUNCEMENT",
        "PAIR","PAIRS","LAUNCH","LAUNCHED","LAUNCHES","LAUNCHPOOL","LAUNCHPAD","MEGADROP",
        "MARGIN","INNOVATION","ZONE","SUPPORT","WITHDRAWAL","DEPOSIT","USDM","USDTM","COINM",
        "AND","WITH","FOR","ON","TO","OF","IN","AT","BE","WE","OR","AS","BY","IS","IT","AN","A","THE","UTC",
    };

    [GeneratedRegex(@"\(([A-Z0-9]{2,15})\)")]
    private static partial Regex ParenRegex();

    [GeneratedRegex(@"\b([A-Z0-9]{2,12})(?:USDT|USDC|BUSD|BTC|ETH|FDUSD|TUSD|KRW)\b")]
    private static partial Regex PairSuffixRegex();

    /// Returns base ticker candidates (uppercase) from title. Prefers tokens inside parens,
    /// falls back to BASEUSDT-style pair suffixes.
    public static IReadOnlyList<string> ExtractFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<string>();
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in ParenRegex().Matches(title))
        {
            var tok = m.Groups[1].Value;
            if (IsValidTicker(tok) && seen.Add(tok))
            {
                found.Add(tok);
            }
        }

        if (found.Count == 0)
        {
            foreach (Match m in PairSuffixRegex().Matches(title))
            {
                var tok = m.Groups[1].Value;
                if (IsValidTicker(tok) && seen.Add(tok))
                {
                    found.Add(tok);
                }
            }
        }

        return found;
    }

    private static bool IsValidTicker(string tok)
    {
        if (tok.Length < 2 || tok.Length > 15) return false;
        if (Stopwords.Contains(tok)) return false;
        if (tok.All(char.IsDigit)) return false;
        return true;
    }
}
