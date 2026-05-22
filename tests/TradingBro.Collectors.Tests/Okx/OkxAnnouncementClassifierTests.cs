using System.Text.Json;
using TradingBro.Collectors.Okx;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Tests.Okx;

/// <summary>
/// Unit tests for <see cref="OkxAnnouncementClassifier"/>.
/// No mocks — the classifier is a pure function validated directly.
/// </summary>
public sealed class OkxAnnouncementClassifierTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<(Market market, EventType eventType)> Classify(string title, string? annType = null)
        => OkxAnnouncementClassifier.Classify(title, annType);

    // -----------------------------------------------------------------------
    // Rule: SpotListingRegex — "OKX will launch X/Y for spot trading"
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_WillLaunchSpotPair_ReturnsSpotListing()
    {
        var result = Classify("OKX will launch AI/USDⓈ for spot trading", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Spot && r.eventType == EventType.Listing,
            "because 'will launch X/Y for spot trading' is a spot listing");
    }

    [Fact]
    public void Classify_ToListSpotPairWithParenProject_ReturnsSpotListing()
    {
        var result = Classify("OKX to list PROS/USDT (Pharos) for spot trading", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Spot && r.eventType == EventType.Listing,
            "because 'to list TOKEN/QUOTE (Project) for spot trading' is a spot listing");
    }

    [Fact]
    public void Classify_ToListSpotPairWithParenProjectForGensyn_ReturnsSpotListing()
    {
        var result = Classify("OKX to list AI/USDT (Gensyn) for spot trading", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Spot && r.eventType == EventType.Listing);
    }

    // -----------------------------------------------------------------------
    // Rule: EquityFuturesListingRegex — "OKX to list perpetual futures for X equity"
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_EquityPerpsingleTicker_ReturnsFuturesListing()
    {
        var result = Classify("OKX to list perpetual futures for GLW equity", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Futures && r.eventType == EventType.Listing,
            "because single-ticker equity perp is a futures listing");
    }

    [Fact]
    public void Classify_EquityPerpMultipleTickers_ReturnsFuturesListing()
    {
        var result = Classify("OKX to list perpetual futures for GEV and URNM equities", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Futures && r.eventType == EventType.Listing,
            "because multi-ticker equity perp is a futures listing");
    }

    [Fact]
    public void Classify_EquityPerpFourTickers_ReturnsFuturesListing()
    {
        var result = Classify("OKX to list perpetual futures for SOXL, NBIS, QCOM and CSCO equities", "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Futures && r.eventType == EventType.Listing);
    }

    // -----------------------------------------------------------------------
    // Rule: FuturesListingBroadRegex — expiry perps / X-Perp
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_ExpiryPerpsXPerp_ReturnsFuturesListing()
    {
        var result = Classify(
            "OKX to list TAOUSD, BNBUSD, HYPEUSD, LINKUSD and TRXUSD Expiry Perps (X-Perp)",
            "announcements-new-listings");

        result.Should().ContainSingle(r => r.market == Market.Futures && r.eventType == EventType.Listing,
            "because X-Perp expiry perps are classified as futures listings");
    }

    // -----------------------------------------------------------------------
    // Rule: NetworkUpgradeRegex — infra notices must be ignored
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_NetworkUpgradeBase_ReturnsEmpty()
    {
        var result = Classify("OKX to support Base network upgrade", "announcements-deposit-withdrawal-suspension-resumption");

        result.Should().BeEmpty("because network upgrade notices are infrastructure, not trading events");
    }

    [Fact]
    public void Classify_NetworkUpgradeSui_ReturnsEmpty()
    {
        var result = Classify("OKX to support Sui network upgrade");

        result.Should().BeEmpty("because network upgrade notices are infrastructure, not trading events");
    }

    // -----------------------------------------------------------------------
    // Rule: TradeToEarnRegex — promotional titles must be ignored
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_SpotTradeToEarnPromoWithCross_ReturnsEmpty()
    {
        var result = Classify(
            "OKX × Gensyn (AI) Spot Trade-to-Earn: Trade and Share 500,000 USDT",
            "latest-events");

        result.Should().BeEmpty("because Spot Trade-to-Earn promotions are not listing events");
    }

    [Fact]
    public void Classify_SpotTradeToEarnPromoWithX_ReturnsEmpty()
    {
        var result = Classify(
            "OKX x PROS Spot Trade to Earn: Trade & Share 600,000 PROS",
            "latest-events");

        result.Should().BeEmpty("because Spot Trade to Earn promotions are not listing events");
    }

    // -----------------------------------------------------------------------
    // Rule: OnchainEarnDelistingRegex — earn product removal is NOT a trading delisting
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_OnchainEarnDelisting_ReturnsEmpty()
    {
        var result = Classify(
            "USDT on Morpho (Katana) delisting from Onchain Earn",
            "announcements-earn-and-loan");

        result.Should().BeEmpty("because DeFi earn-product delisting is not a spot/futures delisting");
    }

    // -----------------------------------------------------------------------
    // Rule: DelistingRegex — real trading-pair delisting
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_SpotPairDelisting_ReturnsSpotDelisting()
    {
        var result = Classify("OKX will delist BTC/USDT spot pair");

        result.Should().ContainSingle(r => r.market == Market.Spot && r.eventType == EventType.Delisting,
            "because explicit delist of a spot pair yields Spot/Delisting");
    }

    // -----------------------------------------------------------------------
    // Degenerate: empty/null input
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_EmptyTitle_ReturnsEmpty()
    {
        OkxAnnouncementClassifier.Classify(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Classify_NullTitle_ReturnsEmpty()
    {
        OkxAnnouncementClassifier.Classify(null).Should().BeEmpty();
    }

    [Fact]
    public void Classify_WhitespaceTitle_ReturnsEmpty()
    {
        OkxAnnouncementClassifier.Classify("   ").Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Coverage: >=80% of "announcements-new-listings" titles must be classified.
    //
    // Rationale: the fixture (items.json) intentionally spans all OKX annTypes
    // to validate both positive (listing) and negative (ignored) paths.
    // Non-listing annTypes (API changes, trading-updates, earn, events) are
    // correctly returned as empty — measuring those against the 80% threshold
    // would reward a classifier that naively classifies everything.
    // The meaningful coverage surface is the set of items the classifier is
    // designed to act on: annType = "announcements-new-listings".
    // -----------------------------------------------------------------------

    [Fact]
    public void Classify_RealTitles_ProducesMarketEventPairsForAtLeastEightyPercent()
    {
        // Arrange — load items.json from output directory
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Okx", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<OkxFixtureItem>>(json)!;

        // Focus on listing-type items — the primary classification surface.
        var listingItems = items
            .Where(i => i.AnnType == "announcements-new-listings")
            .ToList();

        var unclassified = new List<string>();

        // Act
        foreach (var item in listingItems)
        {
            var result = OkxAnnouncementClassifier.Classify(item.Title, item.AnnType);
            if (result.Count == 0)
            {
                unclassified.Add(item.Title ?? "(null)");
            }
        }

        // Assert
        var total = listingItems.Count;
        var classified = total - unclassified.Count;
        var ratio = (double)classified / total;

        ratio.Should().BeGreaterThanOrEqualTo(0.80,
            because: $"at least 80% of 'announcements-new-listings' OKX titles must produce a classification. " +
                      $"Unclassified ({unclassified.Count}/{total}):\n" +
                      string.Join("\n", unclassified.Select(t => $"  - {t}")));
    }

    // -----------------------------------------------------------------------
    // Private DTO for deserialising the fixture
    // -----------------------------------------------------------------------

    private sealed class OkxFixtureItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string? Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("annType")]
        public string? AnnType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? Url { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pTime")]
        public string? PTime { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("businessPTime")]
        public string? BusinessPTime { get; set; }
    }
}
