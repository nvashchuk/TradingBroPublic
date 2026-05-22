using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Bybit;

internal sealed class BybitAnnouncementsResponse
{
    [JsonPropertyName("retCode")] public int RetCode { get; set; }
    [JsonPropertyName("retMsg")] public string? RetMsg { get; set; }
    [JsonPropertyName("result")] public BybitAnnouncementsResult? Result { get; set; }
}

internal sealed class BybitAnnouncementsResult
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("list")] public List<BybitAnnouncementItem>? List { get; set; }
}

internal sealed class BybitAnnouncementItem
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("type")] public BybitAnnouncementType? Type { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("dateTimestamp")] public long? DateTimestamp { get; set; }
    [JsonPropertyName("publishTime")] public long? PublishTime { get; set; }
}

internal sealed class BybitAnnouncementType
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
}
