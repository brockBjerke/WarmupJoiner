using System.Text.Json.Serialization;

namespace WarmupJoiner.Models;

public sealed class ApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("now")]
    public long Now { get; set; }

    [JsonPropertyName("cards")]
    public List<ServerCard> Cards { get; set; } = new();
}
