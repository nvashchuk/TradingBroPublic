using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Bithumb;

/// Response shape of `https://api.bithumb.com/v1/notices`.
/// The endpoint returns a JSON array (not an envelope) of notice objects.
internal sealed class BithumbNotice
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("pc_url")] public string? PcUrl { get; set; }

    [JsonPropertyName("categories")] public List<string>? Categories { get; set; }

    [JsonPropertyName("published_at")] public string? PublishedAt { get; set; }

    [JsonPropertyName("modified_at")] public string? ModifiedAt { get; set; }
}
