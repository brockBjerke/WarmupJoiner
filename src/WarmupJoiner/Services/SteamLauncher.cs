using System.Diagnostics;

namespace WarmupJoiner.Services;

/// <summary>
/// Hands the connect request off to Steam's own "steam://connect/host:port" URI
/// handler - the exact same link WarmupServer's "Instant Connect" button uses.
/// Steam (on Windows or Linux) registers this scheme itself, so this launches
/// or foregrounds CS2 and connects without us touching the game at all.
/// </summary>
public static class SteamLauncher
{
    public static void Connect(string host, int port)
    {
        var uri = $"steam://connect/{host}:{port}";
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        });
    }
}
