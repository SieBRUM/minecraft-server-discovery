using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace McDiscovery.Api.Endpoints;

public static class StatsEndpoints
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", StatsAsync);
        app.MapGet("/api/facets", FacetsAsync);
        app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }));
    }

    // The aggregates scan the whole table, so they are cached briefly.
    internal static async Task<Ok<StatsDto>> StatsAsync(AppDbContext db, IMemoryCache cache, CancellationToken ct) =>
        TypedResults.Ok((await cache.GetOrCreateAsync("stats", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await ComputeStatsAsync(db, ct);
        }))!);

    internal static async Task<Ok<FacetsDto>> FacetsAsync(AppDbContext db, IMemoryCache cache, CancellationToken ct) =>
        TypedResults.Ok((await cache.GetOrCreateAsync("facets", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var servers = db.Servers.AsNoTracking();
            return new FacetsDto(
                await CountriesAsync(servers, 300, ct),
                await TopAsync(servers.Where(s => s.Software != null), s => s.Software!, 100, ct),
                await TopAsync(servers.Where(s => s.ModLoader != null), s => s.ModLoader!, 50, ct));
        }))!);

    private static async Task<StatsDto> ComputeStatsAsync(AppDbContext db, CancellationToken ct)
    {
        var servers = db.Servers.AsNoTracking();
        var dayAgo = DateTime.UtcNow.AddDays(-1);

        var totals = await servers
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Servers = g.Count(),
                Online = g.Count(s => s.IsOnline),
                Java = g.Count(s => s.Edition == Edition.Java),
                Bedrock = g.Count(s => s.Edition == Edition.Bedrock),
                New = g.Count(s => s.FirstSeenUtc >= dayAgo),
                Seen = g.Count(s => s.LastSeenUtc >= dayAgo),
                Players = g.Sum(s => s.IsOnline ? (long)(s.PlayersOnline ?? 0) : 0L),
                WithQuery = g.Count(s => s.QueryEnabled),
                Modded = g.Count(s => s.ModLoader != null),
            })
            .FirstOrDefaultAsync(ct);

        var loginResults = await servers
            .Where(s => s.Edition == Edition.Java)
            .GroupBy(s => s.LoginResult)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var scans = await db.ScanRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedUtc)
            .Take(10)
            .Select(r => new ScanRunDto(r.Id, r.Kind, r.StartedUtc, r.FinishedUtc, r.Description, r.Engine, r.TargetCount, r.OpenPorts,
                r.ServersOnline, r.NewServers, r.Completed))
            .ToListAsync(ct);

        var asns = await servers
            .Where(s => s.Asn != null)
            .GroupBy(s => s.Asn)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => new { g.Key, Label = g.Max(s => s.AsOrganization), Count = g.Count() })
            .ToListAsync(ct);

        return new StatsDto
        {
            Servers = totals?.Servers ?? 0,
            Online = totals?.Online ?? 0,
            Java = totals?.Java ?? 0,
            Bedrock = totals?.Bedrock ?? 0,
            NewLast24h = totals?.New ?? 0,
            SeenLast24h = totals?.Seen ?? 0,
            PlayersOnline = totals?.Players ?? 0,
            WithQuery = totals?.WithQuery ?? 0,
            Modded = totals?.Modded ?? 0,
            KnownPlayers = await db.Players.CountAsync(ct),
            Countries = await CountriesAsync(servers, 25, ct),
            Versions = await TopAsync(servers.Where(s => s.VersionName != null), s => s.VersionName!, 20, ct),
            Software = await TopAsync(servers.Where(s => s.Software != null), s => s.Software!, 15, ct),
            ModLoaders = await TopAsync(servers.Where(s => s.ModLoader != null), s => s.ModLoader!, 10, ct),
            LoginResults = [.. loginResults.OrderByDescending(x => x.Count).Select(x => new CountDto(x.Key.ToString(), null, x.Count))],
            Asns = [.. asns.Select(x => new CountDto("AS" + x.Key, x.Label, x.Count))],
            RecentScans = scans,
            GeneratedUtc = DateTime.UtcNow,
        };
    }

    private static async Task<List<CountDto>> CountriesAsync(IQueryable<Server> servers, int take, CancellationToken ct) =>
        await servers
            .Where(s => s.CountryCode != null)
            .GroupBy(s => s.CountryCode!)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .Select(g => new CountDto(g.Key, g.Max(s => s.CountryName), g.Count()))
            .ToListAsync(ct);

    private static async Task<List<CountDto>> TopAsync(IQueryable<Server> servers, System.Linq.Expressions.Expression<Func<Server, string>> key,
        int take, CancellationToken ct) =>
        await servers
            .GroupBy(key)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .Select(g => new CountDto(g.Key, null, g.Count()))
            .ToListAsync(ct);
}
