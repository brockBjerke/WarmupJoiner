using WarmupJoiner.Models;

namespace WarmupJoiner.Services;

/// <summary>
/// The core "find me a server" loop: fetch the live list, filter it down to
/// what the user actually wants, ping-test the survivors, and pick the best one.
/// </summary>
public sealed class ServerFinder
{
    private const int PingTimeoutMs = 1000;
    private const int MaxCandidatesToPing = 15;

    private readonly WarmupApiClient _api = new();

    public async Task<ServerCard?> RunUntilFoundAsync(UserSettings settings, Action<string> log, CancellationToken ct)
    {
        var deadline = settings.SearchTimeoutSeconds > 0
            ? DateTime.UtcNow.AddSeconds(settings.SearchTimeoutSeconds)
            : (DateTime?)null;

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            log($"Attempt {attempt}: fetching the live server list...");

            List<ServerCard> cards;
            try
            {
                cards = await _api.FetchServersAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                log($"Could not reach WarmupServer ({ex.Message}). Will retry.");
                cards = new List<ServerCard>();
            }

            var best = await FindBestCandidateAsync(cards, settings, log, ct).ConfigureAwait(false);
            if (best is not null)
            {
                return best;
            }

            if (deadline is not null && DateTime.UtcNow >= deadline)
            {
                log("Search timed out - no server matched your preferences.");
                return null;
            }

            log($"No match yet. Waiting {settings.PollIntervalSeconds}s before retrying...");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<ServerCard?> FindBestCandidateAsync(
        IEnumerable<ServerCard> cards,
        UserSettings settings,
        Action<string> log,
        CancellationToken ct)
    {
        var filtered = cards
            .Where(c => c.IsFresh && c.FreeSlots > 0)
            .Where(c => c.Humans >= settings.MinHumanPlayers)
            .Where(c => !settings.ExcludeServersWithBots || c.Bots == 0)
            .Where(c => settings.PreferredRegions.Count == 0 || settings.PreferredRegions.Contains(c.Region))
            .OrderByDescending(c => c.Humans)
            .ThenBy(c => c.Bots)
            .Take(MaxCandidatesToPing)
            .ToList();

        if (filtered.Count == 0)
        {
            log("No server matches your filters yet.");
            return null;
        }

        log($"{filtered.Count} server(s) match your filters, checking ping...");

        var pingResults = await Task.WhenAll(filtered.Select(async card =>
        {
            if (!card.TryGetEndpoint(out var host, out var port))
            {
                return (Card: card, Ping: (int?)null);
            }

            var ping = await ServerPinger.MeasureAsync(host, port, PingTimeoutMs, ct).ConfigureAwait(false);
            return (Card: card, Ping: ping);
        })).ConfigureAwait(false);

        var best = pingResults
            .Where(r => r.Ping is not null && r.Ping <= settings.MaxPingMs)
            .OrderByDescending(r => r.Card.Humans)
            .ThenBy(r => r.Ping)
            .FirstOrDefault();

        if (best.Card is null)
        {
            log("Matching servers were found, but none responded within your ping limit.");
            return null;
        }

        log($"Best match: {best.Card.DisplayName} - {best.Card.Humans} players, {best.Ping}ms ping.");
        return best.Card;
    }
}
