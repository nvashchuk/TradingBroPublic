using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Binance;

internal sealed class BinanceCmsListResponse
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public BinanceCmsListData? Data { get; set; }
}

internal sealed class BinanceCmsListData
{
    [JsonPropertyName("articles")] public List<BinanceCmsArticle>? Articles { get; set; }
    [JsonPropertyName("catalogs")] public List<BinanceCmsCatalog>? Catalogs { get; set; }
}

internal sealed class BinanceCmsCatalog
{
    [JsonPropertyName("articles")] public List<BinanceCmsArticle>? Articles { get; set; }
}

internal sealed class BinanceCmsArticle
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("releaseDate")] public long? ReleaseDate { get; set; }
}
