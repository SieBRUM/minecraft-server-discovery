# Minecraft Server Discovery

[![CI](https://github.com/SieBRUM/minecraft-server-discovery/actions/workflows/ci.yml/badge.svg)](https://github.com/SieBRUM/minecraft-server-discovery/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

A **research tool** that finds Minecraft servers in IP ranges (up to the whole IPv4 internet), records
everything they publicly report, and shows the results on a website: a searchable server list, server
details with player/uptime history, a world map, player search and statistics.

- **Scanner** (C#/.NET 10): stateless SYN scanning with [masscan](https://github.com/robertdavidgraham/masscan)
  (with a built-in connect-scanner fallback), a stateless UDP sweep for Bedrock, and its own Minecraft
  protocol client for the actual probing.
- **API** (C#/ASP.NET Core): read-only HTTP API, localhost only by default, IP allowlist, rate limiting,
  strict security headers.
- **Website** (Angular 22 + Angular Material): talks only to the API.
- **Database**: one SQLite file (EF Core, WAL mode) on a persistent Docker volume.
- **One `docker compose up`** runs everything; the scanner can be restarted or re-run on its own without
  touching the API or website.

Runs on Linux (e.g. Ubuntu Server) and Windows, with or without Docker; CI builds and tests on both.

> [!IMPORTANT]
> Scanning networks you don't own may be against your provider's terms or the law where you live. Only
> scan what you are allowed to, start with low rates, honour opt-out requests (`config/exclude.txt`) and
> consider disabling the login probe (see [What is collected](#what-is-collected)). You are responsible for how
> you use this tool.

---

## Contents

- [Quick start (Docker)](#quick-start-docker)
- [What is collected](#what-is-collected)
- [How it works](#how-it-works)
- [Using the scanner](#using-the-scanner)
- [Configuration](#configuration)
- [Exposing the website to the internet](#exposing-the-website-to-the-internet)
- [Running without Docker (Linux and Windows)](#running-without-docker-linux-and-windows)
- [Data, backups and resetting](#data-backups-and-resetting)
- [Development](#development)
- [Troubleshooting](#troubleshooting)
- [Credits](#credits)
- [License](#license)

---

## Quick start (Docker)

Requirements: Docker with Compose v2 (Docker Engine on Linux, Docker Desktop on Windows/macOS) and git.

```bash
git clone https://github.com/SieBRUM/minecraft-server-discovery.git && cd minecraft-server-discovery
cp .env.example .env          # optional: every setting has a default
nano config/targets.txt       # add the ranges you want to scan (empty = the scanner does nothing)
docker compose up -d --build
```

Open <http://localhost:8080>. Follow the scanner with `docker compose logs -f scanner`.

| Task | Command |
| --- | --- |
| Start / update everything | `docker compose up -d --build` |
| Scanner logs | `docker compose logs -f scanner` |
| Restart the scan (e.g. after editing `config/targets.txt`) | `docker compose restart scanner` |
| Stop only the scanner | `docker compose stop scanner` |
| Re-probe all known servers once (API keeps running) | `docker compose run --rm scanner rescan` |
| Keep known servers fresh every hour | `docker compose --profile rescan up -d` |
| One-off scan of a range | `docker compose run --rm scanner scan -t 192.0.2.0/24 --loop=false` |
| Back up the database | `docker compose run --rm scanner backup` (see [Data, backups and resetting](#data-backups-and-resetting)) |
| Stop everything (data is kept) | `docker compose down` |

On Windows use the same commands in PowerShell (`copy .env.example .env`, `notepad config\targets.txt`).

The first scan downloads the free DB-IP Lite GeoIP databases (around a hundred MB) into the data volume;
they are refreshed monthly.

## What is collected

Everything comes from what the servers send to any client, plus an offline GeoIP lookup:

| Source | Data |
| --- | --- |
| Server list ping (Java 1.7+) | version name and protocol, MOTD with colours/formatting, players online/max, player sample (names + UUIDs), server icon, `enforcesSecureChat`, `previewsChat`, `preventsChatReports`, Forge/NeoForge mod lists (incl. the packed 1.18+ format), latency |
| Legacy ping (pre-1.7) | version, MOTD, players, for old servers |
| Query protocol (UDP, `enable-query=true`) | server software, plugin list, map name, game type, **full** player list |
| Login probe (optional) | online mode (premium only) vs. offline mode ("cracked"), whitelist, kick messages |
| Bedrock (RakNet unconnected ping) | MOTD, version, protocol, players, level name, game mode, server GUID, Education Edition |
| GeoIP (offline, DB-IP Lite) | continent, country, region, city, approximate coordinates, ASN and network name |
| Scanner bookkeeping | first seen, last seen online, last scanned, online/offline, consecutive failures, peak players, per-probe history (player count, latency, up/down), which players were seen on which servers and when, scan runs |

Detected software (Paper, Purpur, Spigot, Velocity, BungeeCord, Forge, Fabric, Geyser, …) and mod loader are
derived from the version string and query data.

**About the login probe:** to tell online mode from offline mode, the scanner starts a login with the name
`McDiscoveryBot` and disconnects as soon as the server answers. It never joins, but the attempt is
visible in the server's log, and on offline-mode servers older than 1.20.2 it can briefly show up as a
join. Disable it with `SCAN_LOGIN_PROBE=false` in `.env` (or `--login-probe=false`). Change the name with
`--login-name`.

## How it works

```
                  ┌─────────────── scanner (mcd-scanner) ────────────────┐
config/targets ─▶ │ masscan SYN scan ─┐                                   │
                  │ connect fallback ─┼─▶ probe workers ─▶ batch writer ──┼─▶ SQLite (WAL)  ◀── API (read-only) ◀── website
                  │ Bedrock UDP sweep ┘   (status, legacy,   (+ GeoIP)    │   /data volume       :8080 (localhost)
                  └───────────────────    query, login)  ─────────────────┘
```

- **Discovery.** masscan sends SYNs statelessly at the configured rate and streams open ports back. Without
  masscan the built-in connect scanner is used (thousands of parallel connects, much slower for big ranges).
  Bedrock servers are found with a stateless UDP sweep built into the scanner: one socket sends RakNet pings
  to every target while a receiver collects pongs, so it needs no privileges. Targets are walked in a keyed
  pseudo-random order (Feistel permutation, like masscan's) so no single network gets hammered.
- **Probing.** Each open port is probed by a pool of workers (`--concurrency`, default 512): status ping,
  then legacy ping as fallback, then query and login probe in parallel.
- **Storage.** Results are written in batches (one short transaction per 250 results), so the API keeps
  serving while the scanner writes. Players are deduplicated by UUID; fake "players" that servers use as
  hover text are filtered out. Server icons are stored once per unique image.
- **Why no message queue?** SQLite in WAL mode allows one writer plus many readers across processes, which
  is exactly this setup. A broker would add a service without adding anything at this scale.

## Using the scanner

```
mcd-scanner scan     [targets/options]   discover and probe servers in IP ranges
mcd-scanner rescan   [filters/options]   re-probe servers already in the database (no port scan)
mcd-scanner geoip-update [--force]       download the DB-IP Lite databases
mcd-scanner backup [--out <file>]        consistent copy of the database while everything runs
```

In Docker: `docker compose run --rm scanner <command> ...`. Natively: `dotnet run --project src/McDiscovery.Scanner -- <command> ...`
(or the published `mcd-scanner` binary). `--help` works on every command.

**Targets** are IPv4 addresses, CIDRs or ranges: `--targets 192.0.2.0/24,198.51.100.7` or
`--targets-file config/targets.txt` (one per line, `#` comments). `--exclude` / `--exclude-file` remove ranges.
`0.0.0.0/8`, `127.0.0.0/8`, `169.254.0.0/16`, multicast and `240.0.0.0/4` are always excluded;
`config/exclude.txt` also excludes private, CGNAT and documentation ranges by default.

Most useful options (`scan`):

| Option | Default | Meaning |
| --- | --- | --- |
| `-p, --ports` | `25565` | Java TCP ports, e.g. `25565,25566` or `25560-25570`; `none` for Bedrock only |
| `--bedrock` | `false` | also sweep for Bedrock servers |
| `--bedrock-ports` | `19132` | Bedrock UDP ports |
| `-r, --rate` | `10000` | packets per second (masscan, connect scan and UDP sweep each) |
| `--engine` | `auto` | `auto` (masscan if found), `masscan`, `connect` |
| `--concurrency` | `512` | servers probed in parallel |
| `--timeout` | `5s` | timeout per status ping / login probe |
| `--login-probe` / `--query` / `--legacy-ping` | `true` | individual probes |
| `--loop` / `--interval` | `false` / `24h` | repeat the scan |
| `--masscan-adapter`, `--masscan-arg` | | passed to masscan (e.g. `--masscan-arg=--router-mac --masscan-arg=00-11-22-33-44-55`) |
| `--history-days` | `180` | delete history snapshots older than this |
| `--dry-run` | | print the number of addresses and an estimated duration |

`rescan` filters: `--edition java|bedrock`, `--online-only`, `--seen-within 7d`, `--max-failures 10`.

**Speed.** At `--rate 10000` the whole IPv4 space (3.7 billion addresses) takes about 4 days per port; at
100k pps about 10 hours. Probing keeps up easily because only open ports are probed. Higher rates need a
good uplink and an understanding provider. In Docker the scanner uses **host networking** so masscan's raw
packets bypass Docker's NAT (on a bridge network the connection-tracking table fills up within seconds at
these rates and packets are silently dropped). It opens no ports.

**Scanning your own LAN or Docker networks:** remove the private ranges from `config/exclude.txt`. masscan
sends through the default route, so for directly connected networks use `--engine connect` (or pass
`--masscan-adapter` and `--masscan-arg=--router-mac ...`).

## Configuration

All configuration is through `.env` (Docker) or environment variables / `appsettings.json` (native). See
[`.env.example`](.env.example) for the full list with explanations.

| Variable | Default | Used by |
| --- | --- | --- |
| `MCD_BIND_ADDRESS` | `127.0.0.1` | host interface the website/API is published on (Docker) |
| `MCD_PORT` | `8080` | published port (Docker) |
| `MCD_ALLOWED_NETWORKS` | loopback (native); loopback + private ranges (Docker) | client networks allowed to use the site/API; everyone else gets `403` |
| `MCD_TRUSTED_PROXIES` | empty | reverse proxies whose `X-Forwarded-For` is trusted |
| `MCD_RATE_LIMIT_PER_MINUTE` | `600` | requests per minute per client IP (`0` = off) |
| `MCD_DB_PATH` | `data/mcdiscovery.db` (native), `/data/mcdiscovery.db` (Docker) | SQLite database file (API and scanner) |
| `MCD_GEOIP_DIR` | `geoip` next to the database | DB-IP Lite `.mmdb` files (scanner) |
| `SCAN_*`, `RESCAN_*` | see `.env.example` | scanner settings used by `docker-compose.yml` |

No credentials are needed anywhere: SQLite has no password, DB-IP Lite needs no key, and the API has nothing
to log in to.

## Exposing the website to the internet

By default nothing is reachable from outside the machine: the port is published on `127.0.0.1` only, and the
API only answers loopback/private clients. When you want to open it up:

1. Put a TLS-terminating reverse proxy in front (Caddy, nginx, Traefik). Point it at `127.0.0.1:8080` and set
   `MCD_TRUSTED_PROXIES` to the proxy's address so the allowlist and rate limit see real client IPs.
   Alternatively set `MCD_BIND_ADDRESS=0.0.0.0` to publish the port directly (plain HTTP).
2. Add the clients that may connect to `MCD_ALLOWED_NETWORKS`, e.g. `203.0.113.7/32`. Use `0.0.0.0/0,::/0`
   only if you really want it public.
3. `docker compose up -d`.

Security design:

- The API is **read-only**: only `GET`/`HEAD` are accepted, there are no accounts and nothing is writable over HTTP.
- All database access goes through EF Core with parameters; search input is escaped for `LIKE`.
- Server-provided text (MOTDs, kick messages, player names, plugin names) is never rendered as HTML. MOTD
  colours are validated `#rrggbb` values (checked by both the scanner and the API), and icons are only stored if they are real PNGs
  and are served as `image/png` with `nosniff`.
- Strict `Content-Security-Policy` (no inline or third-party scripts; images only from the site itself and
  the OpenStreetMap tile server), `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`.
- All input is validated and clamped (page sizes, search length, unknown filters ignored), with per-IP rate limiting
  and request size limits.
- Containers run as an unprivileged user with read-only root filesystems and all capabilities dropped; the
  scanner gets only `NET_RAW` and `NET_ADMIN`, which masscan needs.
- CI runs CodeQL (security-extended) and `npm audit`; Dependabot keeps dependencies current.

The browser loads map tiles from `tile.openstreetmap.org`; that's the only third-party request the website makes.

## Running without Docker (Linux and Windows)

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 24 LTS](https://nodejs.org)
(only to build the website), and for fast scanning masscan:

- **Ubuntu/Debian:** `sudo apt install masscan libpcap0.8t64` (the package name is `libpcap0.8` on older
  releases). masscan needs raw sockets: either run the scanner with `sudo`, or grant the capabilities once
  with `sudo setcap cap_net_raw,cap_net_admin+eip "$(command -v masscan)"`.
- **Windows:** install [Npcap](https://npcap.com) and put `masscan.exe` (from the masscan releases or built
  yourself) on the `PATH`, or pass `--masscan-path C:\tools\masscan.exe`. Run the scanner from an elevated terminal.
- Without masscan everything still works with the built-in connect scanner, just slower for large ranges.

```bash
# 1. Website: build once into the API's wwwroot
cd web && npm ci && npx ng build && cd ..
cp -r web/dist/web/browser/. src/McDiscovery.Api/wwwroot/          # Windows: xcopy /E /I web\dist\web\browser src\McDiscovery.Api\wwwroot

# 2. API + website on http://127.0.0.1:8080 (database: ./data/mcdiscovery.db)
dotnet run --project src/McDiscovery.Api -c Release

# 3. Scanner, in another terminal (same working directory, so it uses the same database)
dotnet run --project src/McDiscovery.Scanner -c Release -- scan --targets-file config/targets.txt --exclude-file config/exclude.txt
```

Use `MCD_DB_PATH` (or `--db`) to put the database elsewhere; the API and scanner must point at the same file.
Change the API's listen address with `--urls http://0.0.0.0:8080` or the `Urls` setting in
`src/McDiscovery.Api/appsettings.json`, and the allowlist with `MCD_ALLOWED_NETWORKS` or the `Security` section there.

## Data, backups and resetting

- Docker keeps everything in the named volume `mcdiscovery_mcd-data` (database + GeoIP files). It survives
  `docker compose down`, image rebuilds and updates. A named volume is used instead of a bind mount because
  SQLite's file locking doesn't work reliably on bind mounts on Docker Desktop.
- Backup while everything keeps running (uses SQLite's online backup; copying the database file directly is
  not safe in WAL mode):
  ```bash
  docker compose run --rm scanner backup            # writes /data/backups/mcdiscovery-<timestamp>.db
  docker compose cp api:/data/backups/. ./backups/  # copy the backups out of the volume
  ```
  Natively: `dotnet run --project src/McDiscovery.Scanner -- backup --out backups/mcdiscovery.db`.
- Reset all data: `docker compose down -v` (deletes the volume).
- Schema changes are EF Core migrations, applied automatically by whichever process starts first.
- History snapshots are pruned after `--history-days` (default 180).

## Development

```
src/McDiscovery.Core       EF Core model, SQLite setup, migrations (shared by API and scanner)
src/McDiscovery.Protocol   Minecraft protocol clients: Java status/legacy/login/query, Bedrock ping, MOTD parser
src/McDiscovery.Scanner    CLI: target sets, discovery engines, probing, GeoIP, batched ingest
src/McDiscovery.Api        Minimal API, security middleware, serves the built website from wwwroot
web/                       Angular app (standalone components, signals, Vitest)
tests/McDiscovery.Tests    xUnit tests (protocol, scanner pipeline, API) against in-process fake servers
tests/McDiscovery.FakeServer  fake Java + query + Bedrock server, also runnable on its own
docker/                    Dockerfiles; docker-compose.yml at the root
config/                    targets.txt / exclude.txt mounted into the scanner container
```

```bash
dotnet build && dotnet test                         # backend (no network access needed beyond loopback)
cd web && npm ci && npx ng test && npm run format:check && npx ng build
cd web && npx ng serve                              # dev server on :4200, proxies /api to 127.0.0.1:8080
dotnet run --project tests/McDiscovery.FakeServer -- --java-port 25565 --bedrock-port 19132   # something to scan
dotnet run --project src/McDiscovery.Scanner -- scan -t 192.0.2.1 --dry-run
```

Changing the data model: edit `src/McDiscovery.Core/Entities`, then
`dotnet tool restore && dotnet ef migrations add <Name> --project src/McDiscovery.Core --startup-project src/McDiscovery.Core --output-dir Data/Migrations`.

CI (GitHub Actions) builds and tests on Ubuntu and Windows, tests and builds the website, builds both Docker
images and smoke-tests them, and runs CodeQL. It never scans anything.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Website shows "Forbidden" | Your client IP isn't in `MCD_ALLOWED_NETWORKS`. The API log line "Blocked request from …" shows the address it saw. |
| Scanner exits right away with "Nothing to scan" | Add targets to `config/targets.txt`, then `docker compose restart scanner`. |
| `masscan exited with code 1 … libpcap` / `Operation not permitted` | Natively: install libpcap and grant capabilities (see above). The scanner falls back to the connect scanner automatically in `auto` mode. |
| masscan finds nothing on a LAN / Docker network | Use `--engine connect` for directly connected networks (see [Using the scanner](#using-the-scanner)). |
| masscan finds nothing on Docker Desktop | Enable host networking in Docker Desktop settings, or set `SCAN_ENGINE=connect`. |
| No locations | The GeoIP download failed (no internet?). Run `docker compose run --rm scanner geoip-update` and check its output. |
| `database is locked` warnings | Harmless at low rates (the writer retries); make sure only one full scan runs at a time. |

## Credits

- [masscan](https://github.com/robertdavidgraham/masscan) by Robert David Graham, the fast port scanner doing the heavy lifting.
- [DB-IP](https://db-ip.com): **IP Geolocation by DB-IP**, DB-IP Lite databases licensed under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- [MineStat](https://github.com/FragLand/minestat), used by the original version of this project and the
  inspiration for its server probing (this rewrite has its own implementation).
- The [Minecraft Wiki protocol documentation](https://minecraft.wiki/w/Java_Edition_protocol) (formerly wiki.vg),
  including its query and Bedrock/RakNet pages, which the protocol code is based on.
- [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors (map data) and [Leaflet](https://leafletjs.com).
- [Angular](https://angular.dev), [Angular Material](https://material.angular.dev), [Chart.js](https://www.chartjs.org),
  [.NET / ASP.NET Core / EF Core](https://dotnet.microsoft.com), [MaxMind DB reader](https://github.com/maxmind/MaxMind-DB-Reader-dotnet),
  [System.CommandLine](https://github.com/dotnet/command-line-api), [xUnit](https://xunit.net).
- Original project and idea: [SieBRUM](https://github.com/SieBRUM).

Minecraft is a trademark of Mojang Synergies AB; this project is not affiliated with Mojang or Microsoft.

## License

[Apache License 2.0](LICENSE). If you redistribute this project or build on it, you must keep the
[NOTICE](NOTICE) file, which credits this project (Apache-2.0 section 4(d)).
