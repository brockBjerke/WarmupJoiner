using System.Text.RegularExpressions;
using CounterStrike2GSI;
using CounterStrike2GSI.Nodes;

namespace WarmupJoiner.Services;

public enum JoinConfirmationResult
{
    /// <summary>GSI shows you actively playing on the target map at the end of the confirmation window.</summary>
    Confirmed,

    /// <summary>GSI showed you playing on the target map at some point, but not anymore - likely kicked/dropped.</summary>
    JoinedThenLeft,

    /// <summary>GSI never showed you on the target map within the confirmation window.</summary>
    NotConfirmed,

    /// <summary>The local GSI listener isn't running, so nothing could be observed either way.</summary>
    Unavailable,
}

/// <summary>
/// Uses CS2's Game State Integration to confirm whether a join actually
/// stuck, instead of assuming success the moment steam://connect is fired.
/// CS2 only reports your own SteamID + map/activity, not which server IP
/// you're on, so "confirmed" means "you're now playing on the map we sent
/// you to" - a strong proxy, not a literal IP match.
/// </summary>
public sealed class GsiJoinMonitor : IDisposable
{
    private const int Port = 31337;
    private const string ConfigName = "WarmupJoiner";

    private readonly GameStateListener _listener = new(Port);
    private bool _started;

    public bool TryStart(out string statusMessage)
    {
        if (_started)
        {
            statusMessage = "Join confirmation already running.";
            return true;
        }

        bool configWritten;
        try
        {
            // The library's own writer only even attempts detection on Windows
            // (via the registry) and hardcodes Windows-style backslash paths,
            // so it silently fails on Linux and on non-default Steam installs.
            // Try it first (it's correct where it works), then fall back to our
            // own cross-platform detection, which also knows about Snap Steam.
            configWritten = _listener.GenerateGSIConfigFile(ConfigName) || TryWriteConfigOurselves();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            statusMessage = $"Could not write the CS2 GSI config file ({ex.Message}). Join confirmation disabled.";
            return false;
        }

        if (!_listener.Start())
        {
            statusMessage = "Could not start the local join-confirmation listener (port may be in use). Join confirmation disabled.";
            return false;
        }

        _started = true;
        statusMessage = configWritten
            ? "Join confirmation ready."
            : "Join-confirmation listener started, but couldn't auto-locate your CS2 install to write its config file - " +
              "confirmation won't work until gamestate_integration_warmupjoiner.cfg is placed in CS2's game/csgo/cfg folder manually.";
        return true;
    }

    /// <summary>
    /// Two-phase wait: <paramref name="joinTimeout"/> is how long to wait to see
    /// you on the map AT ALL (covers CS2/map load time). Once that first happens,
    /// we don't keep waiting out the rest of that window - we only wait
    /// <paramref name="settleWindow"/> longer to see whether you immediately get
    /// kicked back out, then report. A kick is typically near-instant, so this
    /// reports success or failure in seconds once you're actually in, instead of
    /// always waiting the full window regardless of what already happened.
    /// </summary>
    public async Task<JoinConfirmationResult> WaitForJoinAsync(
        string expectedMap,
        TimeSpan joinTimeout,
        TimeSpan settleWindow,
        CancellationToken ct)
    {
        if (!_started)
        {
            return JoinConfirmationResult.Unavailable;
        }

        var currentlyConfirmed = false;
        DateTime? firstConfirmedAtUtc = null;

        void OnNewGameState(CounterStrike2GSI.GameState state)
        {
            // This runs on the listener's own thread, outside any async context -
            // an unhandled exception here would be fatal to the whole process,
            // so a malformed/unexpected payload must never be allowed to throw out.
            try
            {
                currentlyConfirmed = state.Player.Activity == PlayerActivity.Playing
                    && string.Equals(state.Map.Name, expectedMap, StringComparison.OrdinalIgnoreCase);
                if (currentlyConfirmed)
                {
                    firstConfirmedAtUtc ??= DateTime.UtcNow;
                }
            }
            catch
            {
                // Ignore - worst case this one update is skipped and we rely on the next.
            }
        }

        _listener.NewGameState += OnNewGameState;
        try
        {
            var joinDeadlineUtc = DateTime.UtcNow + joinTimeout;
            while (!ct.IsCancellationRequested)
            {
                var nowUtc = DateTime.UtcNow;
                var effectiveDeadline = firstConfirmedAtUtc is { } confirmedAt
                    ? confirmedAt + settleWindow
                    : joinDeadlineUtc;

                if (nowUtc >= effectiveDeadline)
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            _listener.NewGameState -= OnNewGameState;
        }

        if (currentlyConfirmed)
        {
            return JoinConfirmationResult.Confirmed;
        }

        return firstConfirmedAtUtc is not null ? JoinConfirmationResult.JoinedThenLeft : JoinConfirmationResult.NotConfirmed;
    }

    private bool TryWriteConfigOurselves()
    {
        var cfgDir = TryFindCs2CfgDirectory();
        if (cfgDir is null)
        {
            return false;
        }

        Directory.CreateDirectory(cfgDir);
        var path = Path.Combine(cfgDir, $"gamestate_integration_{ConfigName}.cfg");
        File.WriteAllText(path, BuildConfigContent(_listener.URI));
        return true;
    }

    private static string? TryFindCs2CfgDirectory()
    {
        foreach (var steamRoot in CandidateSteamRoots())
        {
            foreach (var library in CandidateLibraryFolders(steamRoot))
            {
                var candidate = Path.Combine(library, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "cfg");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"); // Snap-packaged Steam
    }

    private static IEnumerable<string> CandidateLibraryFolders(string steamRoot)
    {
        // The default library is the Steam root itself; additional drives/
        // libraries are listed in steamapps/libraryfolders.vdf.
        yield return steamRoot;

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        string content;
        try
        {
            if (!File.Exists(vdfPath))
            {
                yield break;
            }

            content = File.ReadAllText(vdfPath);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
        {
            yield return match.Groups[1].Value.Replace(@"\\", "/");
        }
    }

    private static string BuildConfigContent(string uri) =>
        $$"""
        "WarmupJoiner Integration Configuration"
        {
            "uri"          "{{uri}}"
            "timeout"      "5.0"
            "buffer"       "0.1"
            "throttle"     "0.1"
            "heartbeat"    "10.0"
            "data"
            {
                "provider"      "1"
                "map"           "1"
                "player_id"     "1"
                "player_state"  "1"
            }
        }
        """;

    public void Dispose() => _listener.Dispose();
}
