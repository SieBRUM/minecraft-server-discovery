using System.Linq.Expressions;
using System.Net;
using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Api.Endpoints;

/// <summary>Query string of <c>GET /api/servers</c>. Every value is validated and clamped.</summary>
public sealed record ServerQuery(
    string? Q,
    string? Edition,
    bool? Online,
    string? Country,
    string? Version,
    string? Software,
    string? ModLoader,
    bool? OnlineMode,
    bool? Whitelisted,
    int? MinPlayers,
    bool? Modded,
    bool? HasPlugins,
    string? Sort,
    string? Dir,
    int? Page,
    int? PageSize);

public static class ServerEndpoints
{
    public const int MaxPageSize = 100;
    private const int MaxSearchLength = 100;

    public static void MapServerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/servers");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:long}", GetAsync);
        group.MapGet("/{id:long}/history", HistoryAsync);
        group.MapGet("/{id:long}/favicon", FaviconAsync);
        app.MapGet("/api/map", MapAsync);
    }

    internal static async Task<Results<Ok<PagedResult<ServerSummaryDto>>, BadRequest<string>>> ListAsync(
        [AsParameters] ServerQuery query, AppDbContext db, CancellationToken ct)
    {
        if (query.Q is { Length: > MaxSearchLength })
        {
            return TypedResults.BadRequest($"q may be at most {MaxSearchLength} characters");
        }

        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 25, 1, MaxPageSize);
        if ((long)page * pageSize > 10_000_000)
        {
            return TypedResults.BadRequest("page is out of range");
        }

        var servers = Filter(db.Servers.AsNoTracking(), query);
        var total = await servers.CountAsync(ct);
        var items = await Sort(servers, query.Sort, query.Dir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(SummaryProjection)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ServerSummaryDto>(items.Select(Finish).ToList(), total, page, pageSize));
    }

    internal static IQueryable<Server> Filter(IQueryable<Server> servers, ServerQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            if (IPAddress.TryParse(q, out var ip) && q.Contains('.', StringComparison.Ordinal))
            {
                var address = ip.ToString();
                servers = servers.Where(s => s.Address == address);
            }
            else
            {
                var pattern = "%" + EscapeLike(q) + "%";
                var prefix = EscapeLike(q) + "%";
                servers = servers.Where(s =>
                    EF.Functions.Like(s.Address, prefix, "\\") ||
                    EF.Functions.Like(s.MotdPlain!, pattern, "\\") ||
                    EF.Functions.Like(s.VersionName!, pattern, "\\") ||
                    EF.Functions.Like(s.AsOrganization!, pattern, "\\"));
            }
        }

        switch (query.Edition?.ToLowerInvariant())
        {
            case "java":
                servers = servers.Where(s => s.Edition == Edition.Java);
                break;
            case "bedrock":
                servers = servers.Where(s => s.Edition == Edition.Bedrock);
                break;
        }

        if (query.Online is { } online)
        {
            servers = servers.Where(s => s.IsOnline == online);
        }

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            var country = query.Country.Trim().ToUpperInvariant();
            servers = servers.Where(s => s.CountryCode == country);
        }

        if (!string.IsNullOrWhiteSpace(query.Version))
        {
            var version = "%" + EscapeLike(query.Version.Trim()[..Math.Min(query.Version.Trim().Length, 64)]) + "%";
            servers = servers.Where(s => EF.Functions.Like(s.VersionName!, version, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.Software))
        {
            var software = query.Software.Trim();
            servers = software == "unknown"
                ? servers.Where(s => s.Software == null)
                : servers.Where(s => s.Software == software);
        }

        if (!string.IsNullOrWhiteSpace(query.ModLoader))
        {
            var loader = query.ModLoader.Trim();
            servers = servers.Where(s => s.ModLoader == loader);
        }

        if (query.OnlineMode is { } onlineMode)
        {
            servers = servers.Where(s => s.OnlineMode == onlineMode);
        }

        if (query.Whitelisted is { } whitelisted)
        {
            servers = whitelisted ? servers.Where(s => s.Whitelisted == true) : servers.Where(s => s.Whitelisted != true);
        }

        if (query.MinPlayers is { } minPlayers)
        {
            servers = servers.Where(s => s.PlayersOnline >= minPlayers);
        }

        if (query.Modded is { } modded)
        {
            servers = modded ? servers.Where(s => s.ModLoader != null) : servers.Where(s => s.ModLoader == null);
        }

        if (query.HasPlugins == true)
        {
            servers = servers.Where(s => s.PluginsJson != null);
        }

        return servers;
    }

    private static IQueryable<Server> Sort(IQueryable<Server> servers, string? sort, string? dir)
    {
        var descending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        return (sort?.ToLowerInvariant()) switch
        {
            "players" => Order(servers, s => s.PlayersOnline, descending),
            "maxplayers" => Order(servers, s => s.PlayersMax, descending),
            "peak" => Order(servers, s => s.PeakPlayers, descending),
            "latency" => Order(servers, s => s.LatencyMs, descending),
            "firstseen" => Order(servers, s => s.FirstSeenUtc, descending),
            "version" => Order(servers, s => s.VersionName, descending),
            "country" => Order(servers, s => s.CountryCode, descending),
            "address" => Order(servers, s => s.Address, descending),
            _ => Order(servers, s => s.LastSeenUtc, descending),
        };

        static IQueryable<Server> Order<TKey>(IQueryable<Server> q, Expression<Func<Server, TKey>> key, bool desc) =>
            desc ? q.OrderByDescending(key).ThenByDescending(s => s.Id) : q.OrderBy(key).ThenBy(s => s.Id);
    }

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Row shape read from SQL; the MOTD JSON is parsed afterwards in <see cref="Finish"/>.</summary>
    internal sealed record SummaryRow(ServerSummaryDto Dto, string? MotdJson);

    private static readonly Expression<Func<Server, SummaryRow>> SummaryProjection = s => new SummaryRow(new ServerSummaryDto
    {
        Id = s.Id,
        Address = s.Address,
        Port = s.Port,
        Edition = s.Edition,
        IsOnline = s.IsOnline,
        FirstSeenUtc = s.FirstSeenUtc,
        LastSeenUtc = s.LastSeenUtc,
        VersionName = s.VersionName,
        ProtocolVersion = s.ProtocolVersion,
        MotdPlain = s.MotdPlain,
        PlayersOnline = s.PlayersOnline,
        PlayersMax = s.PlayersMax,
        LatencyMs = s.LatencyMs,
        Software = s.Software,
        ModLoader = s.ModLoader,
        OnlineMode = s.OnlineMode,
        Whitelisted = s.Whitelisted,
        CountryCode = s.CountryCode,
        CountryName = s.CountryName,
        City = s.City,
        HasFavicon = s.FaviconId != null,
    }, s.MotdSegmentsJson);

    private static ServerSummaryDto Finish(SummaryRow row) => row.Dto with { Motd = DtoMapping.ParseMotd(row.MotdJson) };

    internal static async Task<Results<Ok<ServerDetailDto>, NotFound>> GetAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var row = await db.Servers.AsNoTracking().Where(s => s.Id == id).Select(SummaryProjection).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var s = await db.Servers.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var playerCount = await db.ServerPlayers.CountAsync(sp => sp.ServerId == id, ct);
        var players = await db.ServerPlayers.AsNoTracking()
            .Where(sp => sp.ServerId == id)
            .OrderByDescending(sp => sp.LastSeenUtc)
            .Take(200)
            .Select(sp => new ServerPlayerDto(sp.PlayerId, sp.Player!.Name, sp.Player.Uuid, sp.FirstSeenUtc, sp.LastSeenUtc, sp.TimesSeen))
            .ToListAsync(ct);

        return TypedResults.Ok(new ServerDetailDto
        {
            Summary = Finish(row),
            LastScannedUtc = s.LastScannedUtc,
            ConsecutiveFailures = s.ConsecutiveFailures,
            PeakPlayers = s.PeakPlayers,
            EnforcesSecureChat = s.EnforcesSecureChat,
            PreviewsChat = s.PreviewsChat,
            PreventsChatReports = s.PreventsChatReports,
            Mods = DtoMapping.Deserialize<ModDto>(s.ModsJson),
            LoginResult = s.LoginResult,
            LoginMessage = s.LoginMessage,
            QueryEnabled = s.QueryEnabled,
            QueryGameType = s.QueryGameType,
            QueryMap = s.QueryMap,
            QuerySoftware = s.QuerySoftware,
            Plugins = DtoMapping.Deserialize<string>(s.PluginsJson),
            BedrockLevelName = s.BedrockLevelName,
            BedrockGameMode = s.BedrockGameMode,
            BedrockServerGuid = s.BedrockServerGuid,
            ContinentCode = s.ContinentCode,
            Region = s.Region,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            Asn = s.Asn,
            AsOrganization = s.AsOrganization,
            PlayerCount = playerCount,
            Players = players,
        });
    }

    internal static async Task<Results<Ok<List<SnapshotDto>>, NotFound>> HistoryAsync(long id, int? days, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Servers.AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 3650));
        var points = await db.Snapshots.AsNoTracking()
            .Where(x => x.ServerId == id && x.TimestampUtc >= since)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(10_000)
            .Select(x => new SnapshotDto(x.TimestampUtc, x.IsOnline, x.PlayersOnline, x.LatencyMs))
            .ToListAsync(ct);
        points.Reverse();
        return TypedResults.Ok(points);
    }

    internal static async Task<IResult> FaviconAsync(long id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var favicon = await db.Servers.AsNoTracking()
            .Where(s => s.Id == id && s.Favicon != null)
            .Select(s => new { s.Favicon!.Sha256, s.Favicon.Data })
            .FirstOrDefaultAsync(ct);
        if (favicon is null)
        {
            return TypedResults.NotFound();
        }

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{favicon.Sha256}\"");
        http.Response.Headers.CacheControl = "private, max-age=3600";
        // Served as image/png only (the scanner verified the PNG signature); nosniff is set globally.
        return TypedResults.File(favicon.Data, "image/png", entityTag: etag);
    }

    internal static async Task<Ok<List<MapPointDto>>> MapAsync(bool? online, string? edition, AppDbContext db, CancellationToken ct)
    {
        var servers = db.Servers.AsNoTracking().Where(s => s.Latitude != null && s.Longitude != null);
        if (online is { } o)
        {
            servers = servers.Where(s => s.IsOnline == o);
        }

        servers = edition?.ToLowerInvariant() switch
        {
            "java" => servers.Where(s => s.Edition == Edition.Java),
            "bedrock" => servers.Where(s => s.Edition == Edition.Bedrock),
            _ => servers,
        };

        // Aggregate into ~10 km cells so the map stays fast with hundreds of thousands of servers.
        var points = await servers
            .GroupBy(s => new { Lat = Math.Round(s.Latitude!.Value, 1), Lon = Math.Round(s.Longitude!.Value, 1) })
            .Select(g => new MapPointDto(g.Key.Lat, g.Key.Lon, g.Count(), g.Count(s => s.IsOnline), g.Sum(s => s.PlayersOnline ?? 0)))
            .Take(100_000)
            .ToListAsync(ct);
        return TypedResults.Ok(points);
    }
}
