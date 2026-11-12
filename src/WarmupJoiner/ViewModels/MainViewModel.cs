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

    private readonly ServerFinder _finder = new();
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _searchCts;
    private bool _initialized;

    private bool _excludeServersWithBots;
    private string _minHumanPlayersText = "2";
    private string _pollIntervalSecondsText = "15";
    private string _searchTimeoutSecondsText = "0";
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

        StartStopCommand = new RelayCommand(OnStartStop);

        Log("Ready. Set your preferences and hit \"Find & Join\".");
        _ = PopulateFiltersAsync(saved.PreferredCities, saved.PreferredModes);
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
        try
        {
            var found = await _finder.RunUntilFoundAsync(settings, Log, ct).ConfigureAwait(false);
            if (found is not null && found.TryGetEndpoint(out var host, out var port))
            {
                Log($"Joining {found.DisplayName}...");
                SteamLauncher.Connect(host, port);
            }
        }
        catch (Exception ex)
        {
            Log($"Search stopped unexpectedly: {ex.Message}");
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

        settings.ExcludeServersWithBots = ExcludeServersWithBots;
        settings.MinHumanPlayers = minHumans;
        settings.PollIntervalSeconds = poll;
        settings.SearchTimeoutSeconds = timeout;
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
