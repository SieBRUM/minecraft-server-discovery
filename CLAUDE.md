# CLAUDE.md

Notes for AI assistants (and humans) working on this repository. Read README.md for the user-facing view.

## What this is

A research tool: a C# scanner finds Minecraft servers in IPv4 ranges and writes what they report into SQLite;
a read-only C# API serves it; an Angular site shows it. One `docker compose up` runs everything.
Owner preferences: lightweight over production-grade, but secure (no XSS/SQLi/RCE), localhost-only by default,
works on Linux and Windows, no credentials in the repo, credit third parties.

## Layout

| Path | Contents |
| --- | --- |
| `src/McDiscovery.Core` | EF Core entities (`Entities/`), `AppDbContext`, `Database` (SQLite path/connection, init + WAL, backup), migrations in `Data/Migrations` |
| `src/McDiscovery.Protocol` | No dependencies. `Java/` (status ping, legacy ping, login probe, query, Forge `forgeData.d` decoder, packet IO), `Bedrock/` (RakNet ping/pong), `Text/MotdParser` (chat components + § codes → sanitised segments) |
| `src/McDiscovery.Scanner` | CLI (`Program.cs`, System.CommandLine 2.0): `scan`, `rescan`, `geoip-update`, `backup`. `Targets/` (parsing, `TargetSet` range math, Feistel `Permutation`), `Discovery/` (masscan, connect scan, Bedrock UDP sweep, rate limiter), `Probing/ServerProber`, `Geo/` (DB-IP Lite via MaxMind.Db), `Ingest/IngestWriter` (batched upserts), `ScanPipeline` (channels wiring it together) |
| `src/McDiscovery.Api` | Minimal API. `Endpoints/` (servers, players, stats, DTOs), `Security/` (allowlist, headers + GET-only, options). Serves the Angular build from `wwwroot` (copied in by the Dockerfile; gitignored) |
| `web/` | Angular 22, standalone components, signals, zoneless, Angular Material, Leaflet, Chart.js, Vitest |
| `tests/McDiscovery.Tests` | xUnit v3 on Microsoft.Testing.Platform. Protocol, scanner (pipeline end-to-end on loopback), API (WebApplicationFactory) |
| `tests/McDiscovery.FakeServer` | Fake Java/query/Bedrock server, implemented independently of `Protocol` on purpose. Also a runnable exe |
| `build/package.sh` | Release packaging for one runtime id (used by the Release workflow) |
| `docker/` | `api.Dockerfile` (node build → dotnet publish → aspnet), `scanner.Dockerfile` (runtime + masscan with file caps) |
| `config/` | `targets.txt` (empty by default), `exclude.txt`; mounted read-only into the scanner |

## Commands

```bash
dotnet build                      # warnings are errors (Directory.Build.props, latest-recommended analyzers)
dotnet test                       # MTP runner (global.json "test.runner"); filter: dotnet test -- --filter-method "*Name*"
dotnet run --project src/McDiscovery.Scanner -- scan -t 192.0.2.1 --dry-run
dotnet run --project src/McDiscovery.Api                 # http://127.0.0.1:8080
cd web && npm ci && npx ng test && npm run format:check && npx ng build   # ng serve proxies /api → :8080
dotnet tool restore && dotnet ef migrations add <Name> --project src/McDiscovery.Core --startup-project src/McDiscovery.Core --output-dir Data/Migrations
dotnet ef migrations has-pending-model-changes --project src/McDiscovery.Core --startup-project src/McDiscovery.Core   # CI runs this
docker compose up -d --build
```

Toolchain: .NET 10 SDK (`global.json`), Node ≥ 22.22.3 / 24 LTS (Angular CLI 22 refuses older), central package
versions in `Directory.Packages.props`.

## Design decisions (keep them unless asked)

- **SQLite + WAL, no message queue.** Scanner and API are separate processes on the same file. Only the scanner
  writes (short batched transactions, `IngestWriter`); the API is read-only. Both call `Database.InitializeAsync`
  (EF Core migration lock makes that safe). Docker uses a *named* volume because SQLite locking is unreliable on
  Docker Desktop bind mounts. Containers run as uid 1654 (`app` from the .NET images); keep file ownership consistent.
- **Discovery engines**: masscan when available (`auto`), else connect scanner; Bedrock always via the stateless
  UDP sweep. masscan output is parsed from stdout lines `Discovered open port N/tcp on IP`; progress from stderr
  `rate: ... X% done`. Arguments go through `ProcessStartInfo.ArgumentList` (never a shell).
- **Scanner container uses `network_mode: host`** (Docker NAT/conntrack drops masscan traffic at real rates) and
  `cap_drop: ALL` + `NET_RAW`/`NET_ADMIN`; masscan has file capabilities, so the scanner runs non-root. Don't add
  `no-new-privileges` to the scanner (it disables file capabilities). The image must install `libpcap0.8t64`
  explicitly (masscan dlopens it; it's only a "recommends").
- **Mandatory excludes** (`TargetSet.MandatoryExcludes`) are always applied; tests use the internal
  `TargetSet.Create(..., applyMandatoryExcludes: false)` to scan loopback. IPv4 only.
- **Untrusted input everywhere**: everything from servers is size-capped and sanitised (`MotdParser.Sanitize`,
  max lengths, max packet sizes, PNG signature check for favicons, colours must be `#rrggbb`). The API re-validates
  MOTD colours. The website renders server text only through interpolation, never `innerHTML`/`bypassSecurityTrust*`;
  Leaflet popups are built with DOM APIs + `textContent`.
- **CSP** (`SecurityHeadersMiddleware`): `script-src 'self'`, `base-uri 'self'` (Angular needs `<base href="/">`).
  The Angular production build must not emit inline scripts/handlers: `inlineCritical: false` and **no
  `subresourceIntegrity`** (it adds an inline import map). If you change `angular.json`, check `dist/web/browser/index.html`.
- **Allowlist**: default loopback only natively; docker-compose adds private ranges because published ports reach
  the container from the Docker bridge. Config via `MCD_ALLOWED_NETWORKS` (env beats appsettings).
  `MCD_TRUSTED_PROXIES` enables `X-Forwarded-For`.
- **API listen address**: appsettings `Urls` = `http://127.0.0.1:8080`. `ASPNETCORE_URLS` does *not* override it;
  the Docker image sets unprefixed `URLS=http://0.0.0.0:8080`, which does.
- **Login probe** is on by default (owner's choice) but documented as intrusive; Login Start layout depends on the
  protocol version (`LoginProbe.BuildLoginStart`).
- **GeoIP**: DB-IP Lite (CC BY 4.0, attribution in the site footer, About page, README, NOTICE). Read as generic
  dictionaries via MaxMind.Db (`GeoIpService.FromRecords`) so database-type checks don't matter.
- **License**: Apache-2.0 with a NOTICE file that downstream users must keep (attribution requirement).

- **Theming**: `ThemeService` (system/light/dark, stored in localStorage) sets `color-scheme` and `data-theme`
  on `<html>`. Design tokens in `web/src/styles.scss` use `light-dark()`. Canvas code (Chart.js, Leaflet) must
  resolve colours with `cssVar()` (it computes them on a probe element; raw `light-dark(...)` strings render black)
  and redraw when `theme.dark()` changes.
- **Map**: the base map is bundled Natural Earth outlines (`world-atlas`, lazy-loaded); OSM tiles are opt-in.
  Rings crossing ±180° are unwrapped (`unwrapAntimeridian`) and Antarctica is dropped, otherwise Leaflet draws stripes.
- Footer must always show "Made by SieBRUM" linking to https://github.com/SieBRUM (owner's request; covered by `app.spec.ts`).
- README screenshots live in `docs/screenshots/` and use fictional data on documentation IP ranges only.

- **Releases**: `.github/workflows/release.yml` runs after CI succeeds on a push to `main` (`workflow_run`) or by
  hand. Version `MAJOR_MINOR.<run number>`. `build/package.sh <version> <rid> <dir>` makes self-contained single-file
  packages (API ships `wwwroot` + appsettings next to the exe; the API uses the exe folder as content root when a
  `wwwroot` is there). osx-* must build on macOS runners (ad-hoc code signing). Docker images go to
  `ghcr.io/<owner>/minecraft-server-discovery-{api,scanner}` for amd64 + arm64.

## Gotchas

- EF Core on SQLite can't `OrderBy` a projected record member after `GroupBy`; order by `g.Count()` before `Select`.
- Keep `Contains` lists under SQLite's parameter limit; `IngestWriter.QueryChunkedAsync` chunks by 500.
- `DateTime` values are stored as text; `AppDbContext` forces `DateTimeKind.Utc` on read.
- System.CommandLine 2.0: command `Validators` don't see default values, so validation lives in `Validate()` in
  `Program.cs`. Options accept `--opt=value` (docker-compose uses that form).
- `Program` exists in both exe projects; API tests use `WebApplicationFactory<McDiscovery.Api.ApiAssembly>`.
- TestServer has no remote IP; API tests inject one with an `IStartupFilter`.
- Never scan real networks in tests or CI. The fake servers bind to 127.0.0.1.
- Ubuntu's apt mirror is plain HTTP; the Docker builds need network access to nuget.org, registry.npmjs.org and
  archive.ubuntu.com.

## When changing things

- New stored field: entity → `IngestWriter` mapping → migration → DTO (`Endpoints/Dtos.cs`) → `web/src/app/core/models.ts` → UI.
- New API filter: `ServerQuery` + `ServerEndpoints.Filter` + `ServerFilter` (web) + `filter-params.ts` + test in `ApiTests`.
- Run the .NET tests, the web tests, `npm run format:check`, and the production build before committing.
