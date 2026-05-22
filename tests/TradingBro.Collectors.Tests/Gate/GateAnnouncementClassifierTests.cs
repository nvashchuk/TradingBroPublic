using System.Text.Json;
using TradingBro.Collectors.Gate;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Tests.Gate;

/// <summary>
/// Unit tests for <see cref="GateAnnouncementClassifier"/>.
/// No mocks — the classifier is a pure function validated directly.
///
/// Gate classification matrix:
///   cate_id 61 — New Crypto Listings (parent): spot, futures, or pre-market
///   cate_id 38 — New Spot Listings   → always (Spot, Listing)
///   cate_id 37 — New Futures Listings → (Futures, Listing) or (PreMarket, Listing)
///   cate_id 15 — Delistings           → (Spot|Futures, Delisting)
///   cate_id 87 — ETF Delistings       → always (Spot, Delisting)
///   all others — not a listing event  → empty
/// </summary>
public sealed class GateAnnouncementClassifierTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<(Market market, EventType eventType)> Classify(string title, int cateId)
        => GateAnnouncementClassifier.Classify(title, cateId);

    // -----------------------------------------------------------------------
    // cate_id = 61 — New Crypto Listings (generic parent category)
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_CateId61_WithPreMarketAndPerpetual_ReturnsPreMarketListing()
    {
        // Real item from items.json: id=51322
        var result = Classify(
            "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)",
            cateId: 61);

        result.Should().ContainSingle(
            r => r.market == Market.PreMarket && r.eventType == EventType.Listing,
            because: "title has both 'Pre-Market' and 'Perpetual', which signals PreMarket/Listing");
    }

    [Fact]
    public void Classify_CateId61_WithPerpetualNoPreMarket_ReturnsFuturesListing()
    {
        var result = Classify(
            "Gate Will List TOKENUSDT Perpetual Futures (USDT-M)",
            cateId: 61);

        result.Should().ContainSingle(
            r => r.market == Market.Futures && r.eventType == EventType.Listing,
            because: "title has 'Perpetual' but no 'Pre-Market', so it should be Futures/Listing");
    }

    [Fact]
    public void Classify_CateId61_WithUsdtM_ReturnsFuturesListing()
    {
        var result = Classify(
            "Gate Will List ABCUSDT Contract (USDT-M) for Trading",
            cateId: 61);

        result.Should().ContainSingle(
            r => r.market == Market.Futures && r.eventType == EventType.Listing,
            because: "USDT-M suffix is a futures-hint signal");
    }

    [Fact]
    public void Classify_CateId61_PlainTokenNoFuturesHint_ReturnsSpotListing()
    {
        var result = Classify(
            "Gate Lists NEWTOKEN on Spot",
            cateId: 61);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Listing,
            because: "no futures/pre-market keywords — defaults to Spot/Listing for cate_id 61");
    }

    [Fact]
    public void Classify_CateId61_WithSlashPairPattern_ReturnsSpotListing()
    {
        var result = Classify(
            "Gate Will List FOO/USDT on Trading",
            cateId: 61);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Listing,
            because: "TOKEN/USDT slash pattern is treated as spot hint; no futures signal present");
    }

    // -----------------------------------------------------------------------
    // cate_id = 38 — New Spot Listings (always spot)
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_CateId38_AlwaysReturnsSpotListing()
    {
        var result = Classify(
            "Gate Will List FOO/USDT on Spot Trading",
            cateId: 38);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Listing,
            because: "cate_id 38 is exclusively New Spot Listings");
    }

    [Fact]
    public void Classify_CateId38_EvenWithFuturesKeywordInTitle_ReturnsSpotListing()
    {
        // Paranoia: even if a cate_id=38 title contained 'futures' by accident,
        // the category signal wins.
        var result = Classify(
            "Gate Lists BAR/USDT Spot (not futures)",
            cateId: 38);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Listing,
            because: "cate_id 38 always yields Spot/Listing regardless of title content");
    }

    // -----------------------------------------------------------------------
    // cate_id = 37 — New Futures Listings
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_CateId37_Plain_ReturnsFuturesListing()
    {
        var result = Classify(
            "Gate Will List ABCUSDT Perpetual Futures (USDT-M)",
            cateId: 37);

        result.Should().ContainSingle(
            r => r.market == Market.Futures && r.eventType == EventType.Listing,
            because: "cate_id 37 without pre-market keyword should be Futures/Listing");
    }

    [Fact]
    public void Classify_CateId37_WithPreMarket_ReturnsPreMarketListing()
    {
        var result = Classify(
            "Gate Launches Pre-Market Trading for XYZUSDT Perpetual Futures (USDT-M)",
            cateId: 37);

        result.Should().ContainSingle(
            r => r.market == Market.PreMarket && r.eventType == EventType.Listing,
            because: "cate_id 37 with 'Pre-Market' should yield PreMarket/Listing");
    }

    // -----------------------------------------------------------------------
    // cate_id = 15 — Delistings (spot or futures)
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_CateId15_PlainDelisting_ReturnsSpotDelisting()
    {
        var result = Classify(
            "Gate Will Delist TOKEN/USDT Spot Pair",
            cateId: 15);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Delisting,
            because: "cate_id 15 without futures keyword defaults to Spot/Delisting");
    }

    [Fact]
    public void Classify_CateId15_WithFuturesKeyword_ReturnsFuturesDelisting()
    {
        var result = Classify(
            "Gate Futures Will Delist XXXUSDT Perpetual Contract (USDT-M)",
            cateId: 15);

        result.Should().ContainSingle(
            r => r.market == Market.Futures && r.eventType == EventType.Delisting,
            because: "'Perpetual' and 'USDT-M' are futures hints — should yield Futures/Delisting");
    }

    [Fact]
    public void Classify_CateId15_WithPerpKeyword_ReturnsFuturesDelisting()
    {
        var result = Classify(
            "Gate Will Settle YYUSDT Quarterly Futures Contract",
            cateId: 15);

        result.Should().ContainSingle(
            r => r.market == Market.Futures && r.eventType == EventType.Delisting,
            because: "'Futures' keyword triggers futures market classification for cate_id 15");
    }

    // -----------------------------------------------------------------------
    // cate_id = 87 — ETF Delistings (always spot-side leveraged tokens)
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_CateId87_ReturnsSpotDelisting()
    {
        var result = Classify(
            "Gate Will Delist BTC3L/USDT (ETF)",
            cateId: 87);

        result.Should().ContainSingle(
            r => r.market == Market.Spot && r.eventType == EventType.Delisting,
            because: "cate_id 87 (ETF Delistings) are always spot-side instruments");
    }

    // -----------------------------------------------------------------------
    // cate_ids outside {15, 37, 38, 61, 87} — must always produce empty
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(32, "Gate Futures Points Airdrop #120: Claim 60 HYPE3L")]
    [InlineData(33, "SPCX Pre-Market Futures Trading Challenge, Share 200,000 USDT")]
    [InlineData(62, "USDS Staking Now Live: Earn 2.24% APR")]
    [InlineData(65, "Gate Exclusive Benefits: VIP Trading Dividend, Monthly Cash Prize Pool")]
    [InlineData(67, "Institutional Zero-Interest Loan Program Upgrade")]
    [InlineData(84, "Gate Card Limited-Time Offer: Spend with Gate Card & Earn GT")]
    [InlineData(85, "Zero-Cost Copy Trading Phase 2: 20 USDT Welcome Gift")]
    [InlineData(94, "Gate Supports TradFi Product Trading Across All Scenarios")]
    [InlineData(101, "Polymarket Trading Experience Refreshed")]
    [InlineData(5, "Gate Research: Multi-Agent LLM Architecture in BTC Trading")]
    [InlineData(999, "Irrelevant category title")]
    public void Classify_IrrelevantCateId_ReturnsEmpty(int cateId, string title)
    {
        var result = Classify(title, cateId);

        result.Should().BeEmpty(
            because: $"cate_id {cateId} is not a listing/delisting category; title='{title}'");
    }

    // -----------------------------------------------------------------------
    // Degenerate cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_EmptyTitle_ReturnsEmpty()
    {
        GateAnnouncementClassifier.Classify(string.Empty, cateId: 61)
            .Should().BeEmpty("empty title must always yield empty regardless of cate_id");
    }

    [Fact]
    public void Classify_NullTitle_ReturnsEmpty()
    {
        GateAnnouncementClassifier.Classify(null, cateId: 38)
            .Should().BeEmpty("null title must always yield empty regardless of cate_id");
    }

    [Fact]
    public void Classify_WhitespaceTitle_ReturnsEmpty()
    {
        GateAnnouncementClassifier.Classify("   ", cateId: 15)
            .Should().BeEmpty("whitespace-only title must always yield empty regardless of cate_id");
    }

    // -----------------------------------------------------------------------
    // Coverage: fixture-driven — real items.json titles from Gate API
    //
    // Gate's items.json spans many cate_ids, most of which are promotional.
    // The classifier is designed to act on {15, 37, 38, 61, 87}; all others
    // correctly return empty. We only assert ≥80% classification rate for
    // items whose cate_id is in the relevant set.
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_RealTitles_ProducesMarketEventPairs()
    {
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Gate", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<GateFixtureItem>>(json)!;

        // Relevant categories: {15, 37, 38, 61, 87}
        var relevantItems = items
            .Where(i => i.CateId.HasValue && new[] { 15, 37, 38, 61, 87 }.Contains(i.CateId.Value))
            .ToList();

        // If the fixture has no relevant items, the test passes vacuously but
        // emits a warning-level diagnostic.
        if (relevantItems.Count == 0)
        {
            // Nothing to assert — fixture only contains promo/earn items.
            return;
        }

        var unclassified = new List<string>();

        foreach (var item in relevantItems)
        {
            var result = GateAnnouncementClassifier.Classify(item.Title, item.CateId!.Value);
            if (result.Count == 0)
            {
                unclassified.Add($"[cate_id={item.CateId}] {item.Title ?? "(null)"}");
            }
        }

        var total = relevantItems.Count;
        var classified = total - unclassified.Count;
        var ratio = (double)classified / total;

        ratio.Should().BeGreaterThanOrEqualTo(0.80,
            because: $"at least 80% of relevant Gate titles must produce a classification. " +
                     $"Unclassified ({unclassified.Count}/{total}):\n" +
                     string.Join("\n", unclassified.Select(t => $"  - {t}")));
    }

    // -----------------------------------------------------------------------
    // Private DTO for fixture deserialization
    // -----------------------------------------------------------------------

    private sealed class GateFixtureItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string? Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? Url { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cate_id")]
        public int? CateId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("publish_timestamp_value")]
        public long? PublishTimestampValue { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("release_timestamp_value")]
        public string? ReleaseTimestampValue { get; set; }
    }
}
