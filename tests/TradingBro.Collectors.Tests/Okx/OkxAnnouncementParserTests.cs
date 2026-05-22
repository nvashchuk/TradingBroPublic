using System.Text.Json;
using TradingBro.Collectors.Okx;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Tests.Okx;

/// <summary>
/// Integration-style tests for <see cref="OkxAnnouncementParser"/>.
/// No mocks — the parser is exercised end-to-end against real fixture data.
/// </summary>
public sealed class OkxAnnouncementParserTests
{
    // -----------------------------------------------------------------------
    // Shared parser instance
    // -----------------------------------------------------------------------

    private static readonly OkxAnnouncementParser Parser = new();

    // -----------------------------------------------------------------------
    // Helper: build a minimal RawAnnouncement (mirrors what OkxAnnouncementCollector emits)
    // -----------------------------------------------------------------------

    private static RawAnnouncement MakeRaw(
        string title,
        string annType = "announcements-new-listings",
        string? url = null,
        string? businessPTime = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["okx.annType"] = annType,
        };
        if (businessPTime is not null)
        {
            metadata["okx.businessPTime"] = businessPTime;
        }

        return new RawAnnouncement(
            Exchange: Exchange.Okx,
            ExternalId: url ?? $"https://www.okx.com/help/{title.GetHashCode()}",
            Title: title,
            Url: url ?? $"https://www.okx.com/help/{title.GetHashCode()}",
            AnnouncedAt: DateTime.UtcNow,
            RawMetadata: metadata);
    }

    // -----------------------------------------------------------------------
    // Exchange identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Exchange_IsOkx()
    {
        Parser.Exchange.Should().Be(Exchange.Okx);
    }

    // -----------------------------------------------------------------------
    // Spot listing — "OKX will launch AI/USDⓈ for spot trading"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_WillLaunchAiUsds_ProducesSpotListingForAI()
    {
        var raw = MakeRaw(
            "OKX will launch AI/USDⓈ for spot trading",
            url: "https://www.okx.com/help/okx-will-launch-ai-usds-for-spot-trading");

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(a =>
            a.Exchange == Exchange.Okx &&
            a.Symbol == "AI" &&
            a.Market == Market.Spot &&
            a.EventType == EventType.Listing,
            because: "'OKX will launch AI/USDⓈ for spot trading' should yield Symbol=AI, SpotListing");
    }

    // -----------------------------------------------------------------------
    // Spot listing — "OKX to list PROS/USDT (Pharos) for spot trading"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_ToListProsUsdt_ProducesSpotListingForPROS()
    {
        var raw = MakeRaw(
            "OKX to list PROS/USDT (Pharos) for spot trading",
            url: "https://www.okx.com/help/okx-to-list-pros-usdt-pharos-for-spot-trading");

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(a =>
            a.Exchange == Exchange.Okx &&
            a.Symbol == "PROS" &&
            a.Market == Market.Spot &&
            a.EventType == EventType.Listing,
            because: "'OKX to list PROS/USDT (Pharos) for spot trading' should yield Symbol=PROS, SpotListing");
    }

    // -----------------------------------------------------------------------
    // Futures listing — "OKX to list perpetual futures for GLW equity"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_EquityPerpGlw_ProducesFuturesListingForGLW()
    {
        var raw = MakeRaw(
            "OKX to list perpetual futures for GLW equity",
            url: "https://www.okx.com/help/okx-to-list-perpetual-futures-for-glw-equity");

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(a =>
            a.Exchange == Exchange.Okx &&
            a.Symbol == "GLW" &&
            a.Market == Market.Futures &&
            a.EventType == EventType.Listing,
            because: "'OKX to list perpetual futures for GLW equity' should yield Symbol=GLW, FuturesListing");
    }

    // -----------------------------------------------------------------------
    // Ignored: network upgrade → empty list
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_NetworkUpgrade_ReturnsEmpty()
    {
        var raw = MakeRaw(
            "OKX to support Base network upgrade",
            annType: "announcements-deposit-withdrawal-suspension-resumption",
            url: "https://www.okx.com/help/okx-to-support-base-network-upgrade");

        var result = Parser.Parse(raw);

        result.Should().BeEmpty(
            because: "'OKX to support Base network upgrade' is an infrastructure notice and must be ignored");
    }

    // -----------------------------------------------------------------------
    // Ignored: Spot Trade-to-Earn promo → empty list
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_SpotTradeToEarnPromo_ReturnsEmpty()
    {
        var raw = MakeRaw(
            "OKX × Gensyn (AI) Spot Trade-to-Earn: Trade and Share 500,000 USDT",
            annType: "latest-events",
            url: "https://www.okx.com/help/okx-gensyn-ai-spot-trade-to-earn-trade-and-share-500-000-usdt");

        var result = Parser.Parse(raw);

        result.Should().BeEmpty(
            because: "'Spot Trade-to-Earn' promotions must not produce Announcement records");
    }

    // -----------------------------------------------------------------------
    // Ignored: earn-product delisting → empty list
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_OnchainEarnDelisting_ReturnsEmpty()
    {
        var raw = MakeRaw(
            "USDT on Morpho (Katana) delisting from Onchain Earn",
            annType: "announcements-earn-and-loan");

        var result = Parser.Parse(raw);

        result.Should().BeEmpty(
            because: "'delisting from Onchain Earn' is a DeFi earn-product event, not a trading-pair delisting");
    }

    // -----------------------------------------------------------------------
    // Multi-ticker equity perp
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_MultiTickerEquityPerp_ProducesOneFuturesAnnouncementPerTicker()
    {
        var raw = MakeRaw(
            "OKX to list perpetual futures for GEV and URNM equities",
            url: "https://www.okx.com/help/okx-to-list-perpetual-futures-for-gev-and-urnm-equities");

        var result = Parser.Parse(raw);

        result.Should().HaveCount(2, because: "two equity tickers produce two Futures/Listing announcements");
        result.Should().AllSatisfy(a =>
        {
            a.Exchange.Should().Be(Exchange.Okx);
            a.Market.Should().Be(Market.Futures);
            a.EventType.Should().Be(EventType.Listing);
        });
        result.Select(a => a.Symbol).Should().BeEquivalentTo(new[] { "GEV", "URNM" });
    }

    // -----------------------------------------------------------------------
    // Expiry Perps / X-Perp
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_ExpiryPerpsXPerp_ProducesMultipleFuturesAnnouncements()
    {
        var raw = MakeRaw(
            "OKX to list TAOUSD, BNBUSD, HYPEUSD, LINKUSD and TRXUSD Expiry Perps (X-Perp)",
            url: "https://www.okx.com/help/okx-to-list-taousd-bnbusd-hypeusd-linkusd-and-trxusd-expiry-perps-x-perp");

        var result = Parser.Parse(raw);

        result.Should().NotBeEmpty(because: "Expiry Perp listing should produce at least one Futures/Listing announcement");
        result.Should().AllSatisfy(a =>
        {
            a.Exchange.Should().Be(Exchange.Okx);
            a.Market.Should().Be(Market.Futures);
            a.EventType.Should().Be(EventType.Listing);
        });
    }

    // -----------------------------------------------------------------------
    // Fixture-driven: all items in items.json → parser emits ≥1 result for
    // well-known listing titles (annType = announcements-new-listings that
    // contain a spot or futures listing pattern)
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_RealItems_EmitsAnnouncementsForListingItems()
    {
        // Arrange
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Okx", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<OkxFixtureItem>>(json)!;

        var listingItems = items
            .Where(i => i.AnnType == "announcements-new-listings")
            .ToList();

        var emptyResults = new List<string>();

        // Act
        foreach (var item in listingItems)
        {
            var raw = MakeRaw(
                title: item.Title ?? string.Empty,
                annType: item.AnnType ?? "announcements-new-listings",
                url: item.Url);

            var result = Parser.Parse(raw);
            if (result.Count == 0)
            {
                emptyResults.Add(item.Title ?? "(null)");
            }
        }

        // Assert — all new-listing items should produce at least one Announcement
        emptyResults.Should().BeEmpty(
            because: $"every 'announcements-new-listings' item should emit at least one Announcement. " +
                     $"Got no output for:\n" +
                     string.Join("\n", emptyResults.Select(t => $"  - {t}")));
    }

    // -----------------------------------------------------------------------
    // Fixture-driven: all items in items.json — parser never throws
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_RealItems_NeverThrows()
    {
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Okx", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<OkxFixtureItem>>(json)!;

        var act = () =>
        {
            foreach (var item in items)
            {
                var raw = MakeRaw(
                    title: item.Title ?? string.Empty,
                    annType: item.AnnType ?? string.Empty,
                    url: item.Url);
                Parser.Parse(raw);
            }
        };

        act.Should().NotThrow("the parser must be resilient to all real fixture inputs");
    }

    // -----------------------------------------------------------------------
    // Metadata: businessPTime is parsed into EventAt
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_WithBusinessPTime_SetsEventAt()
    {
        var raw = MakeRaw(
            "OKX will launch AI/USDⓈ for spot trading",
            businessPTime: "2025-05-22T16:00:00.0000000Z");

        var result = Parser.Parse(raw);

        result.Should().ContainSingle();
        result[0].EventAt.Should().Be(new DateTime(2025, 5, 22, 16, 0, 0, DateTimeKind.Utc),
            because: "businessPTime should be propagated as EventAt in UTC");
    }

    // -----------------------------------------------------------------------
    // Private DTO for deserialising fixture JSON
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
