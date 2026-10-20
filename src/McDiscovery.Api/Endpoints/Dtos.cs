using System.Text.Json;
using McDiscovery.Core.Entities;

namespace McDiscovery.Api.Endpoints;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>A styled piece of MOTD text. Rendered by the website as plain text, never as HTML.</summary>
public sealed record MotdSegmentDto(string Text, string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough, bool Obfuscated);

public sealed record ServerSummaryDto
{
    public long Id { get; init; }
    public required string Address { get; init; }
    public int Port { get; init; }
    public Edition Edition { get; init; }
    public bool IsOnline { get; init; }
    public DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; init; }
    public string? VersionName { get; init; }
    public int? ProtocolVersion { get; init; }
    public string? MotdPlain { get; init; }
    public IReadOnlyList<MotdSegmentDto> Motd { get; init; } = [];
    public int? PlayersOnline { get; init; }
    public int? PlayersMax { get; init; }
    public int? LatencyMs { get; init; }
    public string? Software { get; init; }
    public string? ModLoader { get; init; }
    public bool? OnlineMode { get; init; }
    public bool? Whitelisted { get; init; }
    public string? CountryCode { get; init; }
    public string? CountryName { get; init; }
    public string? City { get; init; }
    public bool HasFavicon { get; init; }
}

public sealed record ModDto(string Id, string? Version);

public sealed record ServerPlayerDto(long PlayerId, string Name, string? Uuid, DateTime FirstSeenUtc, DateTime LastSeenUtc, int TimesSeen);

public sealed record ServerDetailDto
{
    public required ServerSummaryDto Summary { get; init; }
    public DateTime LastScannedUtc { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int PeakPlayers { get; init; }
    public bool? EnforcesSecureChat { get; init; }
    public bool? PreviewsChat { get; init; }
    public bool? PreventsChatReports { get; init; }
    public IReadOnlyList<ModDto> Mods { get; init; } = [];
    public LoginProbeResult LoginResult { get; init; }
    public string? LoginMessage { get; init; }
    public bool QueryEnabled { get; init; }
    public string? QueryGameType { get; init; }
    public string? QueryMap { get; init; }
    public string? QuerySoftware { get; init; }
    public IReadOnlyList<string> Plugins { get; init; } = [];
    public string? BedrockLevelName { get; init; }
    public string? BedrockGameMode { get; init; }
    public string? BedrockServerGuid { get; init; }
    public string? ContinentCode { get; init; }
    public string? Region { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public long? Asn { get; init; }
    public string? AsOrganization { get; init; }
    public int PlayerCount { get; init; }
    public IReadOnlyList<ServerPlayerDto> Players { get; init; } = [];
}

public sealed record SnapshotDto(DateTime T, bool Online, int? Players, int? LatencyMs);

public sealed record MapPointDto(double Lat, double Lon, int Count, int Online, int Players);

public sealed record PlayerDto(long Id, string Name, string? Uuid, DateTime FirstSeenUtc, DateTime LastSeenUtc, int ServerCount);

public sealed record PlayerServerDto(long ServerId, string Address, int Port, Edition Edition, string? MotdPlain, bool IsOnline,
    DateTime FirstSeenUtc, DateTime LastSeenUtc, int TimesSeen);

public sealed record PlayerDetailDto(PlayerDto Player, IReadOnlyList<PlayerServerDto> Servers);

public sealed record CountDto(string Key, string? Label, int Count);

public sealed record ScanRunDto(long Id, ScanKind Kind, DateTime StartedUtc, DateTime? FinishedUtc, string Description, string Engine,
    long TargetCount, long OpenPorts, long ServersOnline, long NewServers, bool Completed);

public sealed record StatsDto
{
    public int Servers { get; init; }
    public int Online { get; init; }
    public int Java { get; init; }
    public int Bedrock { get; init; }
    public int NewLast24h { get; init; }
    public int SeenLast24h { get; init; }
    public long PlayersOnline { get; init; }
    public int KnownPlayers { get; init; }
    public int WithQuery { get; init; }
    public int Modded { get; init; }
    public IReadOnlyList<CountDto> Countries { get; init; } = [];
    public IReadOnlyList<CountDto> Versions { get; init; } = [];
    public IReadOnlyList<CountDto> Software { get; init; } = [];
    public IReadOnlyList<CountDto> ModLoaders { get; init; } = [];
    public IReadOnlyList<CountDto> LoginResults { get; init; } = [];
    public IReadOnlyList<CountDto> Asns { get; init; } = [];
    public IReadOnlyList<ScanRunDto> RecentScans { get; init; } = [];
    public DateTime GeneratedUtc { get; init; }
}

public sealed record FacetsDto(IReadOnlyList<CountDto> Countries, IReadOnlyList<CountDto> Software, IReadOnlyList<CountDto> ModLoaders);

internal static partial class DtoMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Defence in depth: the scanner already validates colours, but never pass anything else to the browser.</summary>
    public static IReadOnlyList<MotdSegmentDto> ParseMotd(string? json) =>
        [.. Deserialize<MotdSegmentDto>(json)
            .Where(s => s is not null && s.Text is not null)
            .Select(s => s.Color is null || ColorRegex().IsMatch(s.Color) ? s : s with { Color = null })];

    [System.Text.RegularExpressions.GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial System.Text.RegularExpressions.Regex ColorRegex();

    public static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
