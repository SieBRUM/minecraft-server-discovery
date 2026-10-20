using McDiscovery.Core.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Api.Endpoints;

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:long}", GetAsync);
    }

    internal static async Task<Results<Ok<PagedResult<PlayerDto>>, BadRequest<string>>> ListAsync(
        string? q, int? page, int? pageSize, AppDbContext db, CancellationToken ct)
    {
        if (q is { Length: > 36 })
        {
            return TypedResults.BadRequest("q may be at most 36 characters");
        }

        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 25, 1, ServerEndpoints.MaxPageSize);
        if ((long)p * size > 10_000_000)
        {
            return TypedResults.BadRequest("page is out of range");
        }

        var players = db.Players.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (Guid.TryParse(term, out var uuid))
            {
                var dashed = uuid.ToString("D");
                players = players.Where(x => x.Uuid == dashed);
            }
            else
            {
                var pattern = ServerEndpoints.EscapeLike(term) + "%";
                players = players.Where(x => EF.Functions.Like(x.Name, pattern, "\\"));
            }
        }

        var total = await players.CountAsync(ct);
        var items = await players
            .OrderByDescending(x => x.LastSeenUtc)
            .ThenBy(x => x.Id)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(x => new PlayerDto(x.Id, x.Name, x.Uuid, x.FirstSeenUtc, x.LastSeenUtc, x.Servers.Count))
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<PlayerDto>(items, total, p, size));
    }

    internal static async Task<Results<Ok<PlayerDetailDto>, NotFound>> GetAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new PlayerDto(x.Id, x.Name, x.Uuid, x.FirstSeenUtc, x.LastSeenUtc, x.Servers.Count))
            .FirstOrDefaultAsync(ct);
        if (player is null)
        {
            return TypedResults.NotFound();
        }

        var servers = await db.ServerPlayers.AsNoTracking()
            .Where(sp => sp.PlayerId == id)
            .OrderByDescending(sp => sp.LastSeenUtc)
            .Take(500)
            .Select(sp => new PlayerServerDto(sp.ServerId, sp.Server!.Address, sp.Server.Port, sp.Server.Edition, sp.Server.MotdPlain,
                sp.Server.IsOnline, sp.FirstSeenUtc, sp.LastSeenUtc, sp.TimesSeen))
            .ToListAsync(ct);
        return TypedResults.Ok(new PlayerDetailDto(player, servers));
    }
}
