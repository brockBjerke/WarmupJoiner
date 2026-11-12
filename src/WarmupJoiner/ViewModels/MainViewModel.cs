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

    private bool _excludeServersWithBots;
    private string _minHumanPlayersText = "2";
    private string _maxPingMsText = "80";
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
        _maxPingMsText = saved.MaxPingMs.ToString();
        _pollIntervalSecondsText = saved.PollIntervalSeconds.ToString();
        _searchTimeoutSecondsText = saved.SearchTimeoutSeconds.ToString();

        StartStopCommand = new RelayCommand(OnStartStop);

        Log("Ready. Set your preferences and hit \"Find & Join\".");
        _ = PopulateRegionsAsync(saved.PreferredRegions);
    }

    public ObservableCollection<RegionOption> Regions { get; } = new();

    public RelayCommand StartStopCommand { get; }

    public bool ExcludeServersWithBots
    {
        get => _excludeServersWithBots;
        set => SetField(ref _excludeServersWithBots, value);
    }

    public string MinHumanPlayersText
    {
        get => _minHumanPlayersText;
        set => SetField(ref _minHumanPlayersText, value);
    }

    public string MaxPingMsText
    {
        get => _maxPingMsText;
        set => SetField(ref _maxPingMsText, value);
    }

    public string PollIntervalSecondsText
    {
        get => _pollIntervalSecondsText;
        set => SetField(ref _pollIntervalSecondsText, value);
    }

    public string SearchTimeoutSecondsText
    {
        get => _searchTimeoutSecondsText;
        set => SetField(ref _searchTimeoutSecondsText, value);
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

    private async Task PopulateRegionsAsync(IReadOnlyCollection<string> previouslySelected)
    {
        try
        {
            using var client = new WarmupApiClient();
            var cards = await client.FetchServersAsync(CancellationToken.None).ConfigureAwait(false);
            var regionNames = cards.Select(c => c.Region).Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct().OrderBy(r => r);

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var name in regionNames)
                {
                    Regions.Add(new RegionOption(name) { IsSelected = previouslySelected.Contains(name) });
                }
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Log($"Could not load region list yet ({ex.Message}). You can still search with \"any region\".");
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

        if (!int.TryParse(MaxPingMsText, out var maxPing) || maxPing <= 0)
        {
            error = "Max ping must be a positive number.";
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
        settings.MaxPingMs = maxPing;
        settings.PollIntervalSeconds = poll;
        settings.SearchTimeoutSeconds = timeout;
        settings.PreferredRegions = Regions.Where(r => r.IsSelected).Select(r => r.Name).ToList();
        return true;
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
