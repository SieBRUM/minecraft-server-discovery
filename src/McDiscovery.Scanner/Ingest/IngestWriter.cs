using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using McDiscovery.Protocol.Java;
using McDiscovery.Protocol.Text;
using McDiscovery.Scanner.Geo;
using McDiscovery.Scanner.Probing;
using McDiscovery.Scanner.Targets;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Scanner.Ingest;

public sealed record IngestStats(int Online, int Offline, int New);

/// <summary>
/// Writes probe results to the database in batches (one transaction per batch keeps the SQLite write lock
/// short, so the API stays responsive while a scan runs).
/// </summary>
public sealed partial class IngestWriter(DbContextOptions<AppDbContext> dbOptions, GeoIpService? geo)
{
    private readonly Dictionary<string, long> _faviconIds = new(StringComparer.Ordinal);

    public async Task<IngestStats> WriteAsync(IReadOnlyList<ProbeResult> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return new IngestStats(0, 0, 0);
        }

        // Last result wins when the same endpoint shows up twice in a batch.
        var results = batch
            .GroupBy(r => (r.Target.Address, r.Target.Port, r.Target.Edition))
            .Select(g => g.Last())
            .ToList();

        await using var db = new AppDbContext(dbOptions);

        var addresses = results.Select(r => IpV4.Format(r.Target.Address)).Distinct().ToList();
        var existing = (await db.Servers.Where(s => addresses.Contains(s.Address)).ToListAsync(ct))
            .ToDictionary(s => (s.Address, s.Port, s.Edition));

        var players = new PlayerResolver(db);
        var newFavicons = new Dictionary<string, Favicon>(StringComparer.Ordinal);
        int online = 0, offline = 0, created = 0;

        foreach (var result in results)
        {
            var address = IpV4.Format(result.Target.Address);
            var key = (address, (int)result.Target.Port, result.Target.Edition);
            existing.TryGetValue(key, out var server);

            if (!result.Online)
            {
                if (server is not null)
                {
                    server.IsOnline = false;
                    server.LastScannedUtc = result.TimestampUtc;
                    server.ConsecutiveFailures++;
                    db.Snapshots.Add(new ServerSnapshot { Server = server, TimestampUtc = result.TimestampUtc, IsOnline = false });
                    offline++;
                }

                continue;
            }

            if (server is null)
            {
                server = new Server
                {
                    Address = address,
                    Port = result.Target.Port,
                    Edition = result.Target.Edition,
                    FirstSeenUtc = result.TimestampUtc,
                };
                db.Servers.Add(server);
                existing[key] = server;
                created++;
            }

            online++;
            server.IsOnline = true;
            server.LastSeenUtc = result.TimestampUtc;
            server.LastScannedUtc = result.TimestampUtc;
            server.ConsecutiveFailures = 0;

            if (result.Java is { } java)
            {
                ApplyJava(server, java, result.Query, result.Login);
                await ApplyFaviconAsync(db, server, java.Favicon, newFavicons, ct);
            }
            else if (result.Bedrock is { } bedrock)
            {
                ApplyBedrock(server, bedrock);
            }

            server.PeakPlayers = Math.Max(server.PeakPlayers, server.PlayersOnline ?? 0);
            ApplyGeo(server, result);

            db.Snapshots.Add(new ServerSnapshot
            {
                Server = server,
                TimestampUtc = result.TimestampUtc,
                IsOnline = true,
                PlayersOnline = server.PlayersOnline,
                LatencyMs = server.LatencyMs,
            });

            foreach (var (name, uuid) in CollectPlayers(result))
            {
                players.Add(server, name, uuid, result.TimestampUtc);
            }
        }

        await players.ResolveAsync(ct);

        await db.SaveChangesAsync(ct); // one implicit transaction for the whole batch

        foreach (var (sha, favicon) in newFavicons)
        {
            _faviconIds[sha] = favicon.Id;
        }

        if (_faviconIds.Count > 100_000)
        {
            _faviconIds.Clear();
        }

        return new IngestStats(online, offline, created);
    }

    private static void ApplyJava(Server server, JavaStatus java, QueryResult? query, LoginProbeResponse? login)
    {
        server.VersionName = java.VersionName;
        server.ProtocolVersion = java.ProtocolVersion;
        SetMotd(server, java.Motd);
        server.PlayersOnline = java.PlayersOnline;
        server.PlayersMax = java.PlayersMax;
        server.LatencyMs = java.LatencyMs;
        server.EnforcesSecureChat = java.EnforcesSecureChat;
        server.PreviewsChat = java.PreviewsChat;
        server.PreventsChatReports = java.PreventsChatReports;
        server.ModsJson = java.Mods.Count > 0 ? JsonSerializer.Serialize(java.Mods.Select(m => new { id = m.Id, version = m.Version })) : null;
        server.ModCount = java.Mods.Count;

        server.QueryEnabled = query is not null;
        server.QueryGameType = query?.GameType;
        server.QueryMap = query?.Map;
        server.QuerySoftware = query?.Software;
        server.PluginsJson = query is { Plugins.Count: > 0 } ? JsonSerializer.Serialize(query.Plugins) : null;

        server.Software = SoftwareDetector.Detect(query?.Software, java.VersionName, java.ModLoader);
        server.ModLoader = java.ModLoader ?? (server.Software is "Forge" or "NeoForge" or "Fabric" or "Quilt" ? server.Software : null);

        if (login is not null)
        {
            (server.LoginResult, server.OnlineMode, server.Whitelisted) = login.Outcome switch
            {
                LoginOutcome.OnlineMode => (LoginProbeResult.OnlineMode, (bool?)true, server.Whitelisted),
                LoginOutcome.OfflineMode => (LoginProbeResult.OfflineMode, false, (bool?)false),
                LoginOutcome.Whitelisted => (LoginProbeResult.Whitelisted, server.OnlineMode, true),
                LoginOutcome.Kicked => (LoginProbeResult.Kicked, server.OnlineMode, server.Whitelisted),
                LoginOutcome.PluginRequest => (LoginProbeResult.PluginRequest, server.OnlineMode, server.Whitelisted),
                _ => (LoginProbeResult.Failed, server.OnlineMode, server.Whitelisted),
            };
            server.LoginMessage = string.IsNullOrEmpty(login.Message) ? null : MotdParser.Sanitize(login.Message, 512);
        }
    }

    private static void ApplyBedrock(Server server, Protocol.Bedrock.BedrockStatus bedrock)
    {
        server.VersionName = bedrock.Version;
        server.ProtocolVersion = bedrock.ProtocolVersion;
        SetMotd(server, bedrock.Motd);
        server.PlayersOnline = bedrock.PlayersOnline;
        server.PlayersMax = bedrock.PlayersMax;
        server.LatencyMs = bedrock.LatencyMs;
        server.BedrockLevelName = bedrock.LevelName;
        server.BedrockGameMode = bedrock.GameMode;
        server.BedrockServerGuid = bedrock.ServerGuid;
        server.Software = SoftwareDetector.Detect(bedrock.Version, bedrock.Motd.Plain, bedrock.LevelName)
            ?? (bedrock.EditionTag == "MCEE" ? "Education Edition" : null);
    }

    private static void SetMotd(Server server, ParsedText motd)
    {
        server.MotdPlain = motd.Plain.Length > 0 ? motd.Plain : null;
        server.MotdSegmentsJson = motd.Segments.Count > 0 ? motd.SegmentsToJson() : null;
    }

    private void ApplyGeo(Server server, ProbeResult result)
    {
        if (geo?.Lookup(IpV4.ToAddress(result.Target.Address)) is not { } info)
        {
            return;
        }

        server.ContinentCode = info.ContinentCode;
        server.CountryCode = info.CountryCode;
        server.CountryName = info.CountryName;
        server.Region = info.Region;
        server.City = info.City;
        server.Latitude = info.Latitude;
        server.Longitude = info.Longitude;
        server.Asn = info.Asn;
        server.AsOrganization = info.AsOrganization;
    }

    private async Task ApplyFaviconAsync(AppDbContext db, Server server, byte[]? png, Dictionary<string, Favicon> newFavicons, CancellationToken ct)
    {
        if (png is null)
        {
            server.FaviconId = null;
            server.Favicon = null;
            return;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(png));
        if (newFavicons.TryGetValue(sha, out var pending))
        {
            server.Favicon = pending;
            return;
        }

        if (!_faviconIds.TryGetValue(sha, out var id))
        {
            id = await db.Favicons.Where(f => f.Sha256 == sha).Select(f => f.Id).FirstOrDefaultAsync(ct);
        }

        if (id != 0)
        {
            _faviconIds[sha] = id;
            server.Favicon = null;
            server.FaviconId = id;
            return;
        }

        var favicon = new Favicon { Sha256 = sha, Data = png };
        db.Favicons.Add(favicon);
        newFavicons[sha] = favicon;
        server.Favicon = favicon;
    }

    /// <summary>Players from the status sample and the query list, minus the fake entries servers use for hover text.</summary>
    internal static IEnumerable<(string Name, string? Uuid)> CollectPlayers(ProbeResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Java?.Sample ?? [])
        {
            if (IsValidName(p.Name) && NormalizeUuid(p.Uuid) is { } uuid && seen.Add(p.Name))
            {
                yield return (p.Name, uuid);
            }
        }

        foreach (var name in result.Query?.Players ?? [])
        {
            if (IsValidName(name) && seen.Add(name))
            {
                yield return (name, null);
            }
        }
    }

    internal static bool IsValidName(string name) => PlayerNameRegex().IsMatch(name);

    /// <summary>Returns a lower-case dashed UUID, or <c>null</c> for missing, invalid and all-zero UUIDs.</summary>
    internal static string? NormalizeUuid(string? uuid) =>
        Guid.TryParse(uuid, out var g) && g != Guid.Empty ? g.ToString("D") : null;

    // Java names are 3-16 word characters (some old accounts are shorter); Floodgate prefixes Bedrock players with '.' or '*'.
    [GeneratedRegex("^[.*]?[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerNameRegex();

    /// <summary>Runs a <c>Contains</c> query in chunks to stay far below SQLite's bound-parameter limit.</summary>
    private static async Task<List<T>> QueryChunkedAsync<TKey, T>(List<TKey> keys, Func<TKey[], IQueryable<T>> query, CancellationToken ct)
    {
        var result = new List<T>();
        foreach (var chunk in keys.Chunk(500))
        {
            result.AddRange(await query(chunk).ToListAsync(ct));
        }

        return result;
    }

    /// <summary>Finds or creates players and their server links for one batch with a handful of queries.</summary>
    private sealed class PlayerResolver(AppDbContext db)
    {
        private readonly List<(Server Server, string Name, string? Uuid, DateTime Seen)> _pending = [];

        public void Add(Server server, string name, string? uuid, DateTime seen) => _pending.Add((server, name, uuid, seen));

        public async Task ResolveAsync(CancellationToken ct)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var uuids = _pending.Where(p => p.Uuid is not null).Select(p => p.Uuid!).Distinct().ToList();
            var names = _pending.Where(p => p.Uuid is null).Select(p => p.Name).Distinct(StringComparer.Ordinal).ToList();

            var byUuid = (await QueryChunkedAsync(uuids, chunk => db.Players.Where(p => p.Uuid != null && chunk.Contains(p.Uuid)), ct))
                .ToDictionary(p => p.Uuid!, StringComparer.Ordinal);
            var byName = (await QueryChunkedAsync(names, chunk => db.Players.Where(p => p.Uuid == null && chunk.Contains(p.Name)), ct))
                .ToDictionary(p => p.Name, StringComparer.Ordinal);

            var serverIds = _pending.Select(p => p.Server.Id).Where(id => id != 0).Distinct().ToList();
            var playerIds = byUuid.Values.Concat(byName.Values).Select(p => p.Id).ToList();
            var links = serverIds.Count == 0
                ? []
                : (await QueryChunkedAsync(playerIds, chunk => db.ServerPlayers.Where(sp => serverIds.Contains(sp.ServerId) && chunk.Contains(sp.PlayerId)), ct))
                    .ToDictionary(sp => (sp.ServerId, sp.PlayerId));
            var newLinks = new Dictionary<(Server, Player), ServerPlayer>();

            foreach (var (server, name, uuid, seen) in _pending)
            {
                Player? player;
                if (uuid is not null)
                {
                    if (!byUuid.TryGetValue(uuid, out player))
                    {
                        player = new Player { Name = name, Uuid = uuid, FirstSeenUtc = seen };
                        db.Players.Add(player);
                        byUuid[uuid] = player;
                    }
                }
                else if (!byName.TryGetValue(name, out player))
                {
                    player = new Player { Name = name, FirstSeenUtc = seen };
                    db.Players.Add(player);
                    byName[name] = player;
                }

                player.Name = name; // names can change for a UUID
                player.LastSeenUtc = seen;

                if (server.Id != 0 && player.Id != 0 && links.TryGetValue((server.Id, player.Id), out var link))
                {
                    link.LastSeenUtc = seen;
                    link.TimesSeen++;
                }
                else if (newLinks.TryGetValue((server, player), out var pendingLink))
                {
                    pendingLink.LastSeenUtc = seen;
                }
                else
                {
                    var created = new ServerPlayer { Server = server, Player = player, FirstSeenUtc = seen, LastSeenUtc = seen, TimesSeen = 1 };
                    db.ServerPlayers.Add(created);
                    newLinks[(server, player)] = created;
                }
            }
        }
    }
}
