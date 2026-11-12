using WarmupJoiner.Models;

namespace WarmupJoiner.Services;

/// <summary>
/// The core "find me a server" logic: fetch the live list, filter it down to
/// what the user actually wants, and rank the survivors. Ping is measured for
/// internal tie-breaking only - it is never a user-facing filter, since the
/// measurement itself can be unreliable on a flaky connection.
/// </summary>
public sealed class ServerFinder
{
    public const int PingTimeoutMs = 1000;
    private const int MaxCandidatesToPing = 15;

    private readonly WarmupApiClient _api = new();

    public Task<List<ServerCard>> FetchCardsAsync(CancellationToken ct) => _api.FetchServersAsync(ct);

    public static List<ServerCard> FilterCandidates(IEnumerable<ServerCard> cards, UserSettings settings) =>
        cards
            .Where(c => c.IsFresh && c.FreeSlots > 0)
            .Where(c => c.Humans >= settings.MinHumanPlayers)
            .Where(c => !settings.ExcludeServersWithBots || c.Bots == 0)
            .Where(c => settings.PreferredCities.Count == 0 || settings.PreferredCities.Contains(c.City))
            .Where(c => settings.PreferredModes.Count == 0 || settings.PreferredModes.Contains(c.Mode))
            .OrderByDescending(c => c.Humans)
            .ThenBy(c => c.Bots)
            .ToList();

    /// <summary>
    /// When nothing fully matches, surface the few servers that were actually
    /// close - real activity, right city/mode - and say exactly why each one
    /// got passed over, instead of a flat "no match" with no explanation.
    /// Empty/dead servers are never reported here; they're not "close", just noise.
    /// </summary>
    private static List<(ServerCard Card, string Reason)> FindNearMisses(
        IEnumerable<ServerCard> cards,
        UserSettings settings,
        int maxToReport = 3)
    {
        var withinScope = cards
            .Where(c => c.IsFresh && c.Humans > 0)
            .Where(c => settings.PreferredCities.Count == 0 || settings.PreferredCities.Contains(c.City))
            .Where(c => settings.PreferredModes.Count == 0 || settings.PreferredModes.Contains(c.Mode));

        var withReasons = new List<(ServerCard Card, string Reason)>();
        foreach (var card in withinScope)
        {
            var reasons = new List<string>();

            if (card.FreeSlots <= 0)
            {
                reasons.Add($"it's full ({card.Humans + card.Bots}/{card.Capacity})");
            }

            if (settings.ExcludeServersWithBots && card.Bots > 0)
            {
                reasons.Add($"it has {card.Bots} bot{(card.Bots == 1 ? "" : "s")}");
            }

            if (card.Humans < settings.MinHumanPlayers)
            {
                reasons.Add($"only {card.Humans} player{(card.Humans == 1 ? "" : "s")} online (you asked for {settings.MinHumanPlayers}+)");
            }

            if (reasons.Count > 0)
            {
                withReasons.Add((card, string.Join(" and ", reasons)));
            }
        }

        return withReasons
            .OrderByDescending(r => r.Card.Humans)
            .Take(maxToReport)
            .ToList();
    }

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

            var filtered = FilterCandidates(cards, settings);
            if (filtered.Count == 0)
            {
                log("No server matches your filters yet.");
                foreach (var (card, reason) in FindNearMisses(cards, settings))
                {
                    log($"  Close: would join {card.DisplayName}, but {reason}.");
                }
            }
            else
            {
                log($"{filtered.Count} server(s) match your filters, checking ping...");
                var best = await PickBestAsync(filtered.Take(MaxCandidatesToPing), ct).ConfigureAwait(false);

                var pingLabel = best.PingMs is { } ms ? $"{ms}ms" : "unknown (no response)";
                log($"Best match: {best.Card.DisplayName} - {best.Card.Humans} players, ping {pingLabel}.");
                return best.Card;
            }

            if (deadline is not null && DateTime.UtcNow >= deadline)
            {
                log("Search timed out - no server matched your preferences.");
                return null;
            }

            log($"Waiting {settings.PollIntervalSeconds}s before retrying...");
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

    private static async Task<(ServerCard Card, int? PingMs)> PickBestAsync(IEnumerable<ServerCard> candidates, CancellationToken ct)
    {
        var tasks = candidates.Select(async card =>
        {
            if (!card.TryGetEndpoint(out var host, out var port))
            {
                return (Card: card, PingMs: (int?)null);
            }

            var ping = await ServerPinger.MeasureAsync(host, port, PingTimeoutMs, ct).ConfigureAwait(false);
            return (Card: card, PingMs: ping);
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Ping never excludes a candidate - it's only a tie-break among
        // equally populated servers. Unknown ping sorts last, not out.
        return results
            .OrderByDescending(r => r.Card.Humans)
            .ThenBy(r => r.PingMs ?? int.MaxValue)
            .First();
    }
}
