# WarmupJoiner

An unofficial companion app for [warmupserver.net](https://warmupserver.net/) (the free CS2 Deathmatch
server network). Instead of refreshing the server list and copy-pasting connect
codes, set your preferences once, hit **Find & Join**, and the app will keep
checking the live server list until it finds one that fits and connect you
automatically.

This is a third-party tool, not affiliated with WarmupServer / WaSe7 GmbH or Valve.

## Download

Grab the latest build for your OS from the [Releases](../../releases) page -
no install, just unzip and run:

- **Windows**: `WarmupJoiner-windows-x64.zip` → `WarmupJoiner.exe`
  (Windows SmartScreen will warn once because the binary isn't code-signed -
  click "More info" → "Run anyway".)
- **Linux**: `WarmupJoiner-linux-x64.zip` → `chmod +x WarmupJoiner && ./WarmupJoiner`
  (runs fine as-is; to also get it in your app menu/dock with its icon, run
  `./install-desktop-entry.sh` once - GNOME/KDE only show a custom icon for
  apps registered with a `.desktop` entry, not for a bare binary)

Steam must already be installed and running - joining works by handing off to
Steam's own `steam://connect/...` link (the same one WarmupServer's "Instant
Connect" button uses), so Steam launches/foregrounds CS2 and connects for you.

## How it works

1. Polls WarmupServer's live server-list feed (the same data that backs
   [warmupserver.net/servers.php](https://warmupserver.net/servers.php)).
2. Filters servers by your preferences:
   - exclude servers that currently have bots
   - minimum number of human players already in the server
   - region/city - check a whole region (e.g. North America) or drill into
     specific cities (e.g. just Dallas and Chicago)
   - game mode (MultiCFG, Rifles only, AIM-DM, etc.)
   - a hard rule, always on: only considers servers with an actual free slot
3. Among the matches, picks the most-populated one (ping to each candidate
   is measured as an internal tie-break only - a direct query to the game
   port, the same kind real server browsers use - but it never excludes a
   server, since the measurement itself can be unreliable on a flaky
   connection even when the server is fine).
4. Opens `steam://connect/<ip>:<port>` for the winner.
5. If nothing matches, it waits (your "re-check every" interval) and tries
   again, until it finds a match, you stop it, or your timeout is hit.

All of your filter choices are saved to disk as soon as you change them, so
they're already set the next time you open the app.

### Join confirmation (and auto-retry)

Firing the connect link doesn't guarantee you actually get in - someone else
can take the last slot in the gap between the server list refreshing and CS2
loading, or the server can boot you right back out. To catch that, the app
uses CS2's built-in **Game State Integration** to check whether you're
actually in and playing on the target map. This is a two-phase wait, not one
long fixed timer, since a kick after a successful join tends to happen almost
immediately: up to your "Load-in timeout" setting (default 10s - covers
CS2/map load time, tune it up if your machine loads in slower) to see you on
the map at all, then once you're confirmed in, only 5 more seconds to make
sure it sticks before declaring success - so a fast kick gets caught and
retried in seconds, not after sitting out a long fixed window regardless of
what already happened.

- **Confirmed** - you're in. Done.
- **Joined then left** - GSI saw you on the map, then saw you back at the
  menu (kicked/dropped). The app crosses that server off and tries the next
  match automatically.
- **Not confirmed** - never saw you on the map in time (could mean the
  connect failed, or CS2 is just still loading). Also retried automatically.

This needs CS2's GSI to be pointed at the app, which happens automatically -
on first launch, WarmupJoiner writes a small config file into your CS2
install (`game/csgo/cfg/gamestate_integration_warmupjoiner.cfg`) and starts a
local listener. If it can't find your CS2 install to do that automatically,
it'll say so in the log and join confirmation (and the auto-retry-on-failure
behavior) is simply skipped - everything else still works.

### Update checks

On launch, the app checks GitHub's latest release and logs a link if a newer
version exists. It never downloads or replaces anything on its own - that
part's still on you.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build WarmupJoiner.sln
dotnet run --project src/WarmupJoiner/WarmupJoiner.csproj
```

## A note on the data source

WarmupServer doesn't publish a documented public API - this app reads the
same internal JSON feed the website's own server-list page uses. It's
unauthenticated and requires no login, but please don't lower the "re-check
every" interval below a few seconds; this app polls a free community site,
not a dedicated API.

## License

MIT - see [LICENSE](LICENSE).
