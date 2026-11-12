using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WarmupJoiner.Services;

/// <summary>
/// Checks GitHub Releases for a newer tagged version than the one currently
/// running. This only surfaces a link - it never downloads or replaces
/// anything itself.
/// </summary>
public sealed class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/brockBjerke/WarmupJoiner/releases/latest";

    public async Task<string> CheckAsync(Version currentVersion, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WarmupJoiner-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var release = await http.GetFromJsonAsync<ReleaseInfo>(ReleasesApiUrl, ct).ConfigureAwait(false);
            if (release?.TagName is not { Length: > 0 } tag)
            {
                return string.Empty;
            }

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= currentVersion)
            {
                return string.Empty;
            }

            var url = release.HtmlUrl ?? "https://github.com/brockBjerke/WarmupJoiner/releases/latest";
            return $"A newer version is available: {tag} - {url}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private sealed class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
