using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Upbit;

internal sealed class UpbitAnnouncementsResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public UpbitAnnouncementsData? Data { get; set; }
}

internal sealed class UpbitAnnouncementsData
{
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("notices")] public List<UpbitNotice>? Notices { get; set; }
}

internal sealed class UpbitNotice
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("listed_at")] public string? ListedAt { get; set; }
    [JsonPropertyName("first_listed_at")] public string? FirstListedAt { get; set; }
}
