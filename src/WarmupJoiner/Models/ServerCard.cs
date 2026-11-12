using System.Text.Json.Serialization;

namespace WarmupJoiner.Models;

/// <summary>
/// One entry from the WarmupServer.net live server list
/// (https://warmupserver.net/api/servercards/query.php?live=1).
/// </summary>
public sealed class ServerCard
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("map")]
    public string Map { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("skill")]
    public string Skill { get; set; } = string.Empty;

    [JsonPropertyName("lines")]
    public List<string> Lines { get; set; } = new();

    [JsonPropertyName("slots_configured")]
    public int SlotsConfigured { get; set; }

    [JsonPropertyName("live")]
    public LiveStatus? Live { get; set; }

    public string DisplayName => Live?.ServerHostname is { Length: > 0 } hostname
        ? hostname
        : string.Join(" - ", Lines);

    public int Humans => Live?.CurrentUsers ?? 0;

    public int Bots => Live?.CurrentBots ?? 0;

    public int Capacity => Live is { TotalShownSlots: > 0 } live ? live.TotalShownSlots : SlotsConfigured;

    public int FreeSlots => Math.Max(0, Capacity - Humans - Bots);

    public bool IsFresh => Live is null || string.IsNullOrEmpty(Live.Freshness)
        || string.Equals(Live.Freshness, "fresh", StringComparison.OrdinalIgnoreCase);

    public bool TryGetEndpoint(out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var separatorIndex = Ip.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == Ip.Length - 1)
        {
            return false;
        }

        host = Ip[..separatorIndex];
        return int.TryParse(Ip.AsSpan(separatorIndex + 1), out port);
    }
}
