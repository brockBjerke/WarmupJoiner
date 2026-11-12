namespace WarmupJoiner.Models;

/// <summary>User-configurable search preferences, persisted to disk between runs.</summary>
public sealed class UserSettings
{
    public bool ExcludeServersWithBots { get; set; }

    public int MinHumanPlayers { get; set; } = 2;

    public int MaxPingMs { get; set; } = 80;

    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>0 means keep retrying until the user cancels.</summary>
    public int SearchTimeoutSeconds { get; set; }

    /// <summary>Empty means any region is acceptable.</summary>
    public List<string> PreferredRegions { get; set; } = new();
}
