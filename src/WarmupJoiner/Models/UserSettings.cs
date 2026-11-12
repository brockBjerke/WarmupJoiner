namespace WarmupJoiner.Models;

/// <summary>User-configurable search preferences, persisted to disk between runs.</summary>
public sealed class UserSettings
{
    public bool ExcludeServersWithBots { get; set; }

    public int MinHumanPlayers { get; set; } = 2;

    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>0 means keep retrying until the user cancels.</summary>
    public int SearchTimeoutSeconds { get; set; }

    /// <summary>How long to wait for CS2 to actually show you on the target map after connecting, before giving up on that attempt.</summary>
    public int JoinTimeoutSeconds { get; set; } = 10;

    /// <summary>Empty means any city is acceptable. Selecting a whole region selects all of its cities.</summary>
    public List<string> PreferredCities { get; set; } = new();

    /// <summary>Empty means any game mode is acceptable.</summary>
    public List<string> PreferredModes { get; set; } = new();
}
