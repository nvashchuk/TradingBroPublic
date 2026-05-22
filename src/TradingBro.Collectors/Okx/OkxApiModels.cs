#nullable enable

using System.Text.Json.Serialization;

namespace TradingBro.Collectors.Okx;

internal sealed class OkxAnnouncementsResponse
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("data")] public List<OkxAnnouncementsDataPage>? Data { get; set; }
}

internal sealed class OkxAnnouncementsDataPage
{
    [JsonPropertyName("totalPage")] public string? TotalPage { get; set; }
    [JsonPropertyName("details")] public List<OkxAnnouncementItem>? Details { get; set; }
}

internal sealed class OkxAnnouncementItem
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("annType")] public string? AnnType { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("pTime")] public string? PTime { get; set; }
    [JsonPropertyName("businessPTime")] public string? BusinessPTime { get; set; }
}
