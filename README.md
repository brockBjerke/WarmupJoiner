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

Steam must already be installed and running - joining works by handing off to
Steam's own `steam://connect/...` link (the same one WarmupServer's "Instant
Connect" button uses), so Steam launches/foregrounds CS2 and connects for you.

## How it works

1. Polls WarmupServer's live server-list feed (the same data that backs
   [warmupserver.net/servers.php](https://warmupserver.net/servers.php)).
2. Filters servers by your preferences:
   - exclude servers that currently have bots
   - minimum number of human players already in the server
   - preferred region(s) (leave all unchecked for "any")
   - a hard rule, always on: only considers servers with an actual free slot
3. Pings the surviving candidates for real (a direct query to the game port,
   the same kind real server browsers use - not just a ping to the website)
   and drops anything over your max-ping setting.
4. Picks the most-populated server that still fits your filters and ping
   limit, and opens `steam://connect/<ip>:<port>`.
5. If nothing matches, it waits (your "re-check every" interval) and tries
   again, until it finds a match, you stop it, or your timeout is hit.

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
