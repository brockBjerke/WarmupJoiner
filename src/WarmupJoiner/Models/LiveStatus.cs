using System.Text.Json.Serialization;

namespace WarmupJoiner.Models;

public sealed class LiveStatus
{
    [JsonPropertyName("server_hostname")]
    public string? ServerHostname { get; set; }

    [JsonPropertyName("server_total_shown_slots")]
    public int TotalShownSlots { get; set; }

    [JsonPropertyName("server_current_users")]
    public int CurrentUsers { get; set; }

    [JsonPropertyName("server_current_bots")]
    public int CurrentBots { get; set; }

    [JsonPropertyName("server_current_legends")]
    public int CurrentLegends { get; set; }

    [JsonPropertyName("server_current_pros")]
    public int CurrentPros { get; set; }

    [JsonPropertyName("freshness")]
    public string? Freshness { get; set; }

    [JsonPropertyName("age_seconds")]
    public int AgeSeconds { get; set; }
}
