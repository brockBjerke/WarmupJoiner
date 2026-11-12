using System.Net.Http.Json;
using WarmupJoiner.Models;

namespace WarmupJoiner.Services;

/// <summary>
/// Reads the live server list that powers https://warmupserver.net/servers.php.
/// This is the site's own internal endpoint, not a documented public API, so we
/// poll it politely (the caller is expected to space out calls) and identify
/// ourselves via the User-Agent.
/// </summary>
public sealed class WarmupApiClient : IDisposable
{
    private const string Endpoint = "https://warmupserver.net/api/servercards/query.php?live=1";

    private readonly HttpClient _http;

    public WarmupApiClient()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WarmupJoiner/0.1 (unofficial third-party companion app)");
    }

    public async Task<List<ServerCard>> FetchServersAsync(CancellationToken ct)
    {
        var response = await _http.GetFromJsonAsync<ApiResponse>(Endpoint, ct)
            ?? throw new InvalidOperationException("WarmupServer API returned an empty response.");

        if (!response.Ok)
        {
            throw new InvalidOperationException("WarmupServer API reported a non-OK status.");
        }

        return response.Cards;
    }

    public void Dispose() => _http.Dispose();
}
