# macOS Setup

This guide covers running MHServerEmu natively on macOS (tested on an Apple Silicon Mac running macOS 26). The game client itself is Windows-only, so it has to run under Wine. Notes on that are at the end of this guide.

## Prerequisites

- .NET 8 SDK
- Game client files (version 1.52.0.1700)
- A reverse proxy for the client's HTTP/HTTPS requests. This guide uses Caddy.

## Install Dependencies

```bash
brew install dotnet@8 caddy
```

`dotnet@8` is keg-only, so add it to your `PATH` (or call it by its full path):

```bash
export PATH="$(brew --prefix dotnet@8)/bin:$PATH"
```

## Build

```bash
git clone https://github.com/Crypto137/MHServerEmu.git
cd MHServerEmu
dotnet build src/MHServerEmu/MHServerEmu.csproj -c Release -p:Platform=x64
```

On Apple Silicon the build automatically produces platform-neutral (AnyCPU) assemblies instead of x64 ones, because the arm64 .NET runtime cannot load x64 assemblies. It also copies the osx-arm64 build of `SQLite.Interop.dll` to the output directory, so the default SQLite database backend works.

## Set Up Game Data

Copy `Calligraphy.sip` and `mu_cdata.sip` from your Marvel Heroes game files:

```bash
cp "Marvel Heroes/Data/Game/Calligraphy.sip" "MHServerEmu/src/MHServerEmu/bin/x64/Release/net8.0/Data/Game/"
cp "Marvel Heroes/Data/Game/mu_cdata.sip" "MHServerEmu/src/MHServerEmu/bin/x64/Release/net8.0/Data/Game/"
```

## Run the Server

```bash
cd MHServerEmu/src/MHServerEmu/bin/x64/Release/net8.0
dotnet MHServerEmu.dll
```

Create an account with the `!account create [email] [playerName] [password]` console command, or at http://localhost:8080/Dashboard/ while the server is running.

To stop the server, use the `!server shutdown` console command or send it `SIGINT` (Ctrl+C) or `SIGTERM`. All of these save player data and exit the process. Sending a second signal while the shutdown is in progress terminates the process immediately.

If port 8080 is already in use, change it in a `ConfigOverride.ini` next to `Config.ini`:

```ini
[WebFrontend]
Port=4002
```

## Run From a Read-Only Location

By default the server writes into its own directory. To run it from a read-only location (for example inside an app bundle), point the files it writes somewhere else:

```bash
dotnet MHServerEmu.dll --data-dir "$HOME/Library/Application Support/MHServerEmu" --config-override /path/to/ConfigOverride.ini
```

- `--data-dir <path>` holds everything the server writes: `ConfigOverride.ini` (unless `--config-override` is given), the account database and its backups, `Leaderboards/`, `Web/ApiKeys.json`, `Logs/` and `CrashReports/`. The directory is created if needed. Database file names from the config (for example `[SQLiteDBManager] FileName`) are relative to it, and can also be absolute paths.
- `--config-override <path>` uses a specific config override file instead of `ConfigOverride.ini`. If the file doesn't exist, an empty one is created.

Everything else is only read and stays in the server directory: `Config.ini`, `Data/Game` (including `Calligraphy.sip` and `mu_cdata.sip`) and `Data/Web`.

## Configure the Gateway

The client is stricter about addresses than its settings suggest:

- `-siteconfigurl` ignores any port in the URL, so `SiteConfig.xml` must be served on port **80**.
- The client ignores `AuthServerPort` and always sends auth requests to port **443**. It accepts a self-signed certificate.
- The web frontend only matches requests with the `Host` header `localhost:<port>`, and `localhost` may bind to either `::1` or `127.0.0.1`, so the proxy should connect to `localhost` by name and rewrite the `Host` header.
- macOS lets unprivileged processes bind ports 80 and 443 only on the wildcard address, not on `127.0.0.1`. Bind the wildcard address and drop connections from other machines.

Generate a self-signed certificate:

```bash
openssl req -x509 -newkey rsa:2048 -keyout server.key -out server.crt -days 3650 -nodes \
    -subj "/C=US/ST=State/L=City/O=MHServerEmu/CN=localhost"
```

Create a `Caddyfile` (adjust the paths and the web frontend port):

```
{
	admin off
	auto_https off
}

http://:80 {
	@remote not remote_ip 127.0.0.1 ::1
	abort @remote
	root * /path/to/MHServerEmu/assets
	file_server
}

https://:443 {
	@remote not remote_ip 127.0.0.1 ::1
	abort @remote
	tls /path/to/server.crt /path/to/server.key
	reverse_proxy /AuthServer/* localhost:8080 {
		header_up Host localhost:8080
	}
}
```

Make sure `assets/SiteConfig.xml` contains these auth settings:

```xml
<str name="AuthServerAddress" value="localhost" />
<str name="AuthServerUrl" value="/AuthServer/Login/IndexPB" />
<str name="AuthServerPort" value="443" />
```

Then start Caddy:

```bash
caddy run --config Caddyfile
```

## Connect the Client

Run the client under Wine (for example a WineHQ macOS build, which runs x86_64 under Rosetta 2):

```bash
cd "Marvel Heroes/UnrealEngine3/Binaries/Win64"
wine MarvelHeroesOmega.exe -robocopy -nosteam -siteconfigurl=localhost/SiteConfig.xml
```

**Notes:**

- Run the game's own `InstallDependencies` in the Wine prefix first (Visual C++ runtimes, DirectX and PhysX). These install silently under Wine.
- The default Direct3D 9 renderer works through wined3d. `-windowed -ResX=1600 -ResY=900` and `-nostartupmovies` are useful while testing.
- By default a Wine prefix links `Documents` to your real `~/Documents`, so the client writes its settings and logs to `~/Documents/My Games/Marvel Heroes`.
- `WINEDEBUG=trace+wininet` shows the client's HTTP requests, which is the quickest way to debug gateway problems.

## Troubleshooting

**`BadImageFormatException` on startup:** the assemblies were built for x64. Rebuild with the command above, or pass `-p:PlatformTarget=AnyCPU` explicitly.

**`Unable to load shared library 'SQLite.Interop.dll'`:** the native SQLite library is missing from the output directory. Only Apple Silicon is included. On an Intel Mac, build `SQLite.Interop.dll` from the System.Data.SQLite 1.0.118.0 source, or set `UseJsonDBManager=true` in the `[PlayerManager]` section of `ConfigOverride.ini`.
