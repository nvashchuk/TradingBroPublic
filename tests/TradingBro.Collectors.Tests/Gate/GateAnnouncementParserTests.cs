using System.Text.Json;
using TradingBro.Collectors.Gate;
using TradingBro.Core.Models;

namespace TradingBro.Collectors.Tests.Gate;

/// <summary>
/// Integration-style tests for <see cref="GateAnnouncementParser"/>.
/// No mocks — the parser is exercised end-to-end against real fixture data.
///
/// Metadata keys emitted by <see cref="GateAnnouncementCollector"/>:
///   "cate_id"           — category id (string-encoded int); mandatory
///   "id"                — item id (string)
///   "url_path"          — relative URL from Gate API
///   "created_t"         — unix-seconds publish timestamp (string)
///   "release_timestamp" — raw release timestamp string from API
///   "event_at_iso"      — ISO-8601 UTC string derived from release_timestamp (optional)
///   "tags"              — comma-separated tags (optional)
///
/// Symbol extraction (SymbolExtraction.ExtractFromTitle) rules:
///   1. Paren pattern  : "(SYMBOL)" — e.g., "(SPCX)"
///   2. Pair-suffix    : "SYMBOLUSDT" / "SYMBOLBTC" / etc. — e.g., "SPCXUSDT"
///   Slash-separated pairs ("SYMBOL/USDT") are NOT supported by the extractor.
/// </summary>
public sealed class GateAnnouncementParserTests
{
    // -----------------------------------------------------------------------
    // Shared parser instance
    // -----------------------------------------------------------------------

    private static readonly GateAnnouncementParser Parser = new();

    // -----------------------------------------------------------------------
    // Helper: build a RawAnnouncement that mirrors what GateAnnouncementCollector emits
    // -----------------------------------------------------------------------

    private static RawAnnouncement MakeRaw(
        string title,
        int? cateId,
        string? url = null,
        string? eventAtIso = null,
        string? externalId = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (cateId.HasValue)
        {
            metadata["cate_id"] = cateId.Value.ToString();
        }

        if (eventAtIso is not null)
        {
            metadata["event_at_iso"] = eventAtIso;
        }

        var id = externalId ?? Math.Abs(title.GetHashCode()).ToString();
        var fullUrl = url ?? $"https://www.gate.com/announcements/article/{id}";

        return new RawAnnouncement(
            Exchange: Exchange.Gate,
            ExternalId: id,
            Title: title,
            Url: fullUrl,
            AnnouncedAt: DateTime.UtcNow,
            RawMetadata: metadata);
    }

    // -----------------------------------------------------------------------
    // Exchange identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Exchange_IsGate()
    {
        Parser.Exchange.Should().Be(Exchange.Gate);
    }

    // -----------------------------------------------------------------------
    // Missing / invalid cate_id → must always produce empty
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_MissingCateId_ReturnsEmpty()
    {
        var raw = MakeRaw(
            "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)",
            cateId: null);

        Parser.Parse(raw).Should().BeEmpty(
            because: "cate_id is mandatory; a missing key must produce no announcements");
    }

    [Fact]
    public void Parse_NullMetadata_ReturnsEmpty()
    {
        var raw = new RawAnnouncement(
            Exchange: Exchange.Gate,
            ExternalId: "99999",
            Title: "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)",
            Url: "https://www.gate.com/announcements/article/99999",
            AnnouncedAt: DateTime.UtcNow,
            RawMetadata: null);

        Parser.Parse(raw).Should().BeEmpty(
            because: "null RawMetadata means no cate_id is available");
    }

    [Fact]
    public void Parse_InvalidCateIdString_ReturnsEmpty()
    {
        var raw = new RawAnnouncement(
            Exchange: Exchange.Gate,
            ExternalId: "99998",
            Title: "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)",
            Url: "https://www.gate.com/announcements/article/99998",
            AnnouncedAt: DateTime.UtcNow,
            RawMetadata: new Dictionary<string, string> { ["cate_id"] = "not-a-number" });

        Parser.Parse(raw).Should().BeEmpty(
            because: "an unparseable cate_id string must produce no announcements");
    }

    [Fact]
    public void Parse_IrrelevantCateId_ReturnsEmpty()
    {
        // cate_id 32 = promotional/airdrop — outside {15, 37, 38, 61, 87}
        var raw = MakeRaw(
            "Gate Futures Points Airdrop #120: Claim 60 HYPE3L",
            cateId: 32);

        Parser.Parse(raw).Should().BeEmpty(
            because: "cate_id 32 is a promotional category, not a listing/delisting");
    }

    // -----------------------------------------------------------------------
    // Real listing item (cate_id=61, pre-market perpetual) → full fanout
    // Using real item id=51322 from items.json fixture.
    // "SPCXUSDT" matches PairSuffixRegex → symbol "SPCX".
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_RealPreMarketItem_ProducesPreMarketListingAnnouncement()
    {
        // Real item from items.json: id=51322
        var raw = MakeRaw(
            "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)",
            cateId: 61,
            url: "https://www.gate.com/announcements/article/51322",
            externalId: "51322",
            eventAtIso: "2025-05-22T12:22:51.0000000Z");

        var result = Parser.Parse(raw);

        result.Should().NotBeEmpty(
            because: "a pre-market perpetual futures listing must produce at least one Announcement");

        var ann = result.Should().ContainSingle(
            a => a.Market == Market.PreMarket && a.EventType == EventType.Listing,
            because: "pre-market + perpetual signals on cate_id 61 → PreMarket/Listing").Subject;

        ann.Exchange.Should().Be(Exchange.Gate, because: "Gate parser must stamp Exchange=Gate");
        ann.Symbol.Should().Be("SPCX",
            because: "PairSuffixRegex extracts 'SPCX' from 'SPCXUSDT'");
        ann.ExternalId.Should().Be("51322",
            because: "ExternalId must be forwarded from RawAnnouncement");
        ann.Title.Should().Be(
            "Gate Launches Pre-Market Trading for SPCXUSDT Perpetual Futures (USDT-M)");
        ann.Url.Should().Be("https://www.gate.com/announcements/article/51322");
    }

    // -----------------------------------------------------------------------
    // event_at_iso propagation
    // Titles use "PROSUSDT" so PairSuffixRegex can extract "PROS".
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_WithEventAtIso_SetsEventAt()
    {
        // "PROSUSDT" → PairSuffixRegex → symbol "PROS"
        var raw = MakeRaw(
            "Gate Will List PROSUSDT on Spot",
            cateId: 38,
            eventAtIso: "2025-06-01T08:00:00.0000000Z");

        var result = Parser.Parse(raw);

        result.Should().NotBeEmpty(
            because: "'PROSUSDT' should yield symbol 'PROS' via PairSuffixRegex");
        result[0].EventAt.Should().Be(
            new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            because: "event_at_iso must be parsed and propagated as EventAt in UTC");
    }

    [Fact]
    public void Parse_WithoutEventAtIso_LeavesEventAtNull()
    {
        // "ALPUSDT" → PairSuffixRegex → symbol "ALP"
        var raw = MakeRaw(
            "Gate Will List ALPUSDT on Spot",
            cateId: 38);

        var result = Parser.Parse(raw);

        result.Should().NotBeEmpty(
            because: "'ALPUSDT' should yield symbol 'ALP' via PairSuffixRegex");
        result[0].EventAt.Should().BeNull(
            because: "when event_at_iso is absent, EventAt must remain null");
    }

    // -----------------------------------------------------------------------
    // Spot listing — cate_id 38
    // "GMTUSDT" → PairSuffixRegex → symbol "GMT"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_SpotListingCateId38_ProducesSpotListingForSymbol()
    {
        var raw = MakeRaw(
            "Gate Will List GMTUSDT on Spot Trading",
            cateId: 38,
            url: "https://www.gate.com/announcements/article/12345",
            externalId: "12345");

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(
            a => a.Market == Market.Spot && a.EventType == EventType.Listing,
            because: "cate_id 38 is New Spot Listings → Spot/Listing");

        result[0].Exchange.Should().Be(Exchange.Gate,
            because: "Gate parser must stamp Exchange=Gate on every Announcement");
    }

    // -----------------------------------------------------------------------
    // Futures listing — cate_id 37
    // "SPCXUSDT" → PairSuffixRegex → symbol "SPCX"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_FuturesListingCateId37_ProducesFuturesListing()
    {
        var raw = MakeRaw(
            "Gate Will List SPCXUSDT Perpetual Futures (USDT-M)",
            cateId: 37);

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(
            a => a.Market == Market.Futures && a.EventType == EventType.Listing,
            because: "cate_id 37 without pre-market signal → Futures/Listing");

        result[0].Symbol.Should().Be("SPCX");
    }

    // -----------------------------------------------------------------------
    // Delisting — cate_id 15
    //
    // Spot delisting: "ALPUSDT" → PairSuffixRegex → symbol "ALP"
    // Futures delisting: "XXXUSDT" → PairSuffixRegex → symbol "XXX"
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_SpotDelistingCateId15_ReturnsSpotDelisting()
    {
        // "ALPUSDT" → symbol "ALP". Title has no futures keywords → Spot/Delisting.
        var raw = MakeRaw(
            "Gate Will Delist ALPUSDT Spot Pair",
            cateId: 15);

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(
            a => a.Market == Market.Spot && a.EventType == EventType.Delisting,
            because: "cate_id 15 without futures keywords → Spot/Delisting");

        result[0].Symbol.Should().Be("ALP");
    }

    [Fact]
    public void Parse_FuturesDelistingCateId15_ReturnsFuturesDelisting()
    {
        // "XXXUSDT" → symbol "XXX". "Perpetual" + "USDT-M" → Futures/Delisting.
        var raw = MakeRaw(
            "Gate Futures Will Delist XXXUSDT Perpetual Contract (USDT-M)",
            cateId: 15);

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(
            a => a.Market == Market.Futures && a.EventType == EventType.Delisting,
            because: "cate_id 15 with 'Perpetual' and 'USDT-M' → Futures/Delisting");
    }

    // -----------------------------------------------------------------------
    // ETF delisting — cate_id 87
    // "(ETF)" matched by ParenRegex → symbol "ETF" (not in stopwords)
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_EtfDelistingCateId87_ReturnsSpotDelisting()
    {
        // "(ETF)" gives symbol "ETF" via ParenRegex.
        var raw = MakeRaw(
            "Gate Will Delist BTC3L (ETF)",
            cateId: 87);

        var result = Parser.Parse(raw);

        result.Should().ContainSingle(
            a => a.Market == Market.Spot && a.EventType == EventType.Delisting,
            because: "cate_id 87 (ETF Delistings) are spot-side leveraged tokens → Spot/Delisting");
    }

    // -----------------------------------------------------------------------
    // Parser never throws on any real fixture input
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_RealItems_NeverThrows()
    {
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Gate", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<GateFixtureItem>>(json)!;

        var act = () =>
        {
            foreach (var item in items)
            {
                var raw = MakeRaw(
                    title: item.Title ?? string.Empty,
                    cateId: item.CateId,
                    url: item.Url,
                    externalId: item.Id?.ToString());
                Parser.Parse(raw);
            }
        };

        act.Should().NotThrow("the parser must be resilient to all real fixture inputs");
    }

    // -----------------------------------------------------------------------
    // Fixture-driven: items in relevant categories → parser emits ≥1 result
    // for at least 80% of them.
    //
    // Note: the fixture only contains one item in a relevant category (id=51322,
    // cate_id=61). If the fixture grows, this threshold stays meaningful.
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_RealItems_EmitsAnnouncements()
    {
        var fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Gate", "Fixtures", "items.json");

        var json = File.ReadAllText(fixturesPath);
        var items = JsonSerializer.Deserialize<List<GateFixtureItem>>(json)!;

        // Only items in relevant listing/delisting categories
        int[] relevantCateIds = [15, 37, 38, 61, 87];
        var listingItems = items
            .Where(i => i.CateId.HasValue && relevantCateIds.Contains(i.CateId.Value))
            .ToList();

        // Nothing to assert if the fixture contains no relevant items.
        if (listingItems.Count == 0)
        {
            return;
        }

        var emptyResults = new List<string>();

        foreach (var item in listingItems)
        {
            var raw = MakeRaw(
                title: item.Title ?? string.Empty,
                cateId: item.CateId,
                url: item.Url,
                externalId: item.Id?.ToString());

            var result = Parser.Parse(raw);
            if (result.Count == 0)
            {
                emptyResults.Add($"[cate_id={item.CateId}] {item.Title ?? "(null)"}");
            }
        }

        var total = listingItems.Count;
        var classified = total - emptyResults.Count;
        var ratio = total > 0 ? (double)classified / total : 1.0;

        ratio.Should().BeGreaterThanOrEqualTo(0.80,
            because: $"at least 80% of listing/delisting category items should emit announcements. " +
                     $"Silent items ({emptyResults.Count}/{total}):\n" +
                     string.Join("\n", emptyResults.Select(t => $"  - {t}")));
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
