using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Gate;

internal sealed class GateAnnouncementsResponse
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public GateAnnouncementsData? Data { get; set; }
}

internal sealed class GateAnnouncementsData
{
    [JsonPropertyName("total")] public int? Total { get; set; }
    [JsonPropertyName("list")] public List<GateAnnouncementItem>? List { get; set; }
}

internal sealed class GateAnnouncementItem
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("brief")] public string? Brief { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("cate_id")] public int? CateId { get; set; }
    [JsonPropertyName("created_t")] public long? CreatedT { get; set; }
    [JsonPropertyName("release_timestamp")] public string? ReleaseTimestamp { get; set; }
    [JsonPropertyName("tags")] public string? Tags { get; set; }
}
