using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using WarmupJoiner.Models;
using WarmupJoiner.Services;
using WarmupJoiner.Util;

namespace WarmupJoiner.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxLogChars = 8000;
    private static readonly TimeSpan JoinSettleWindow = TimeSpan.FromSeconds(5);

    private readonly ServerFinder _finder = new();
    private readonly GsiJoinMonitor _gsi = new();
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _searchCts;
    private bool _initialized;

    private bool _excludeServersWithBots;
    private string _minHumanPlayersText = "2";
    private string _pollIntervalSecondsText = "15";
    private string _searchTimeoutSecondsText = "0";
    private string _joinTimeoutSecondsText = "10";
    private bool _isSearching;
    private string _statusText = string.Empty;
    private string _actionButtonText = "Find & Join";

    public MainViewModel()
    {
        var saved = SettingsStore.Load();
        _excludeServersWithBots = saved.ExcludeServersWithBots;
        _minHumanPlayersText = saved.MinHumanPlayers.ToString();
        _pollIntervalSecondsText = saved.PollIntervalSeconds.ToString();
        _searchTimeoutSecondsText = saved.SearchTimeoutSeconds.ToString();
        _joinTimeoutSecondsText = saved.JoinTimeoutSeconds.ToString();

        StartStopCommand = new RelayCommand(OnStartStop);

        Log("Ready. Set your preferences and hit \"Find & Join\".");
        _ = PopulateFiltersAsync(saved.PreferredCities, saved.PreferredModes);

        if (_gsi.TryStart(out var gsiStatus))
        {
            Log(gsiStatus);
        }
        else
        {
            Log($"{gsiStatus} (joins will no longer be auto-retried on failure.)");
        }

        _ = CheckForUpdatesAsync();
    }

    public ObservableCollection<RegionGroupViewModel> LocationGroups { get; } = new();

    public ObservableCollection<ModeOption> GameModes { get; } = new();

    public RelayCommand StartStopCommand { get; }

    public bool ExcludeServersWithBots
    {
        get => _excludeServersWithBots;
        set
        {
            if (SetField(ref _excludeServersWithBots, value))
            {
                PersistSettings();
            }
        }
    }

    public string MinHumanPlayersText
    {
        get => _minHumanPlayersText;
        set
        {
            if (SetField(ref _minHumanPlayersText, value))
            {
                PersistSettings();
            }
        }
    }

    public string PollIntervalSecondsText
    {
        get => _pollIntervalSecondsText;
        set
        {
            if (SetField(ref _pollIntervalSecondsText, value))
            {
                PersistSettings();
            }
        }
    }

    public string SearchTimeoutSecondsText
    {
        get => _searchTimeoutSecondsText;
        set
        {
            if (SetField(ref _searchTimeoutSecondsText, value))
            {
                PersistSettings();
            }
        }
    }

    public string JoinTimeoutSecondsText
    {
        get => _joinTimeoutSecondsText;
        set
        {
            if (SetField(ref _joinTimeoutSecondsText, value))
            {
                PersistSettings();
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetField(ref _isSearching, value);
    }

    public string ActionButtonText
    {
        get => _actionButtonText;
        private set => SetField(ref _actionButtonText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    private async Task PopulateFiltersAsync(IReadOnlyCollection<string> previousCities, IReadOnlyCollection<string> previousModes)
    {
        try
        {
            using var client = new WarmupApiClient();
            var cards = await client.FetchServersAsync(CancellationToken.None).ConfigureAwait(false);

            var groupedByRegion = cards
                .Where(c => !string.IsNullOrWhiteSpace(c.Region) && !string.IsNullOrWhiteSpace(c.City))
                .GroupBy(c => c.Region)
                .OrderBy(g => g.Key);

            var modeNames = cards.Select(c => c.Mode).Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct().OrderBy(m => m).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var regionGroup in groupedByRegion)
                {
                    var group = new RegionGroupViewModel(regionGroup.Key, PersistSettings);
                    foreach (var city in regionGroup.Select(c => c.City).Distinct().OrderBy(c => c))
                    {
                        group.AddCity(city, previousCities.Contains(city));
                    }

                    LocationGroups.Add(group);
                }

                foreach (var mode in modeNames)
                {
                    GameModes.Add(new ModeOption(mode, PersistSettings) { IsSelected = previousModes.Contains(mode) });
                }

                _initialized = true;
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Log($"Could not load region/mode list yet ({ex.Message}). You can still search with \"any\".");
            _initialized = true;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        var currentVersion = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var message = await new UpdateChecker().CheckAsync(currentVersion, CancellationToken.None).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(message))
        {
            Log(message);
        }
    }

    private void OnStartStop()
    {
        if (IsSearching)
        {
            _searchCts?.Cancel();
            return;
        }

        if (!TryBuildSettings(out var settings, out var error))
        {
            Log($"Fix your settings first: {error}");
            return;
        }

        SettingsStore.Save(settings);

        _searchCts = new CancellationTokenSource();
        IsSearching = true;
        ActionButtonText = "Stop Searching";

        _ = RunSearchAsync(settings, _searchCts.Token);
    }

    private async Task RunSearchAsync(UserSettings settings, CancellationToken ct)
    {
        var failedEndpoints = new HashSet<string>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Scoped per attempt, not around the whole loop: a hiccup on
                // one candidate (e.g. a flaky GSI event) should only cost us
                // that one attempt, never silently end the entire retry loop.
                try
                {
                    var found = await _finder.RunUntilFoundAsync(settings, Log, ct, failedEndpoints).ConfigureAwait(false);
                    if (found is null)
                    {
                        return;
                    }

                    if (!found.TryGetEndpoint(out var host, out var port))
                    {
                        Log($"Could not parse an address for {found.DisplayName}.");
                        return;
                    }

                    Log($"Joining {found.DisplayName}...");
                    SteamLauncher.Connect(host, port);

                    var confirmation = await WaitForJoinWithHeartbeatAsync(found, settings.JoinTimeoutSeconds, ct).ConfigureAwait(false);
                    switch (confirmation)
                    {
                        case JoinConfirmationResult.Confirmed:
                            Log($"Confirmed - you're in and playing on {found.Map}.");
                            return;

                        case JoinConfirmationResult.JoinedThenLeft:
                            Log($"You connected to {found.DisplayName} but left/got kicked shortly after. Trying another server...");
                            failedEndpoints.Add(found.Ip);
                            continue;

                        case JoinConfirmationResult.NotConfirmed:
                            Log($"Could not confirm you joined {found.DisplayName} within {settings.JoinTimeoutSeconds}s. Trying another server...");
                            failedEndpoints.Add(found.Ip);
                            continue;

                        case JoinConfirmationResult.Unavailable:
                        default:
                            Log("Join confirmation isn't available, so assuming that worked.");
                            return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log($"Hit an unexpected error on that attempt ({ex.Message}). Trying another server...");
                }
            }
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsSearching = false;
                ActionButtonText = "Find & Join";
            });
        }
    }

    private async Task<JoinConfirmationResult> WaitForJoinWithHeartbeatAsync(ServerCard found, int joinTimeoutSeconds, CancellationToken ct)
    {
        var joinTimeout = TimeSpan.FromSeconds(joinTimeoutSeconds);
        Log($"Confirming you actually got into {found.Map} (up to {joinTimeoutSeconds}s to load in, " +
            $"then {JoinSettleWindow.TotalSeconds:0}s to make sure it sticks)...");
        Dispatcher.UIThread.Post(() => ActionButtonText = "Confirming join...");

        var confirmTask = _gsi.WaitForJoinAsync(found.Map, joinTimeout, JoinSettleWindow, ct);
        var heartbeatInterval = TimeSpan.FromSeconds(8);
        var elapsedSeconds = 0;

        while (!confirmTask.IsCompleted)
        {
            var tick = await Task.WhenAny(confirmTask, Task.Delay(heartbeatInterval)).ConfigureAwait(false);
            if (tick == confirmTask)
            {
                break;
            }

            elapsedSeconds += (int)heartbeatInterval.TotalSeconds;
            Log($"Still waiting to confirm ({elapsedSeconds}s elapsed)...");
        }

        Dispatcher.UIThread.Post(() => ActionButtonText = "Stop Searching");
        return await confirmTask.ConfigureAwait(false);
    }

    private bool TryBuildSettings(out UserSettings settings, out string error)
    {
        settings = new UserSettings();
        error = string.Empty;

        if (!int.TryParse(MinHumanPlayersText, out var minHumans) || minHumans < 0)
        {
            error = "Minimum human players must be a non-negative number.";
            return false;
        }

        if (!int.TryParse(PollIntervalSecondsText, out var poll) || poll < 3)
        {
            error = "Poll interval must be at least 3 seconds (be nice to WarmupServer's API).";
            return false;
        }

        if (!int.TryParse(SearchTimeoutSecondsText, out var timeout) || timeout < 0)
        {
            error = "Search timeout must be 0 (unlimited) or a positive number of seconds.";
            return false;
        }

        if (!int.TryParse(JoinTimeoutSecondsText, out var joinTimeout) || joinTimeout < 3)
        {
            error = "Load-in timeout must be at least 3 seconds.";
            return false;
        }

        settings.ExcludeServersWithBots = ExcludeServersWithBots;
        settings.MinHumanPlayers = minHumans;
        settings.PollIntervalSeconds = poll;
        settings.SearchTimeoutSeconds = timeout;
        settings.JoinTimeoutSeconds = joinTimeout;
        settings.PreferredCities = LocationGroups.SelectMany(g => g.Cities).Where(c => c.IsSelected).Select(c => c.Name).ToList();
        settings.PreferredModes = GameModes.Where(m => m.IsSelected).Select(m => m.Name).ToList();
        return true;
    }

    private void PersistSettings()
    {
        if (!_initialized)
        {
            return;
        }

        if (TryBuildSettings(out var settings, out _))
        {
            SettingsStore.Save(settings);
        }
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").AppendLine(message);
            if (_log.Length > MaxLogChars)
            {
                _log.Remove(0, _log.Length - MaxLogChars);
            }

            StatusText = _log.ToString();
        });
    }
}
