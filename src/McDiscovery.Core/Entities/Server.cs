namespace McDiscovery.Core.Entities;

/// <summary>A Minecraft server endpoint (address + port + edition) that responded at least once.</summary>
public class Server
{
    public long Id { get; set; }

    /// <summary>IP address in canonical text form (e.g. <c>1.2.3.4</c>).</summary>
    public required string Address { get; set; }

    public int Port { get; set; }

    public Edition Edition { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    /// <summary>Last time the server answered a status request.</summary>
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Last time the scanner probed the server, whether or not it answered.</summary>
    public DateTime LastScannedUtc { get; set; }

    public bool IsOnline { get; set; }

    /// <summary>How many probes in a row failed since the server was last seen online.</summary>
    public int ConsecutiveFailures { get; set; }

    // ---- Status (server list ping / Bedrock pong) ----

    public string? VersionName { get; set; }
    public int? ProtocolVersion { get; set; }

    /// <summary>MOTD with all formatting removed.</summary>
    public string? MotdPlain { get; set; }

    /// <summary>MOTD as a JSON array of MOTD segments (sanitised, safe to render as text).</summary>
    public string? MotdSegmentsJson { get; set; }

    public int? PlayersOnline { get; set; }
    public int? PlayersMax { get; set; }

    /// <summary>Highest player count ever observed.</summary>
    public int PeakPlayers { get; set; }

    public int? LatencyMs { get; set; }

    /// <summary>Detected server software, e.g. Paper, Velocity, Forge, Bedrock Dedicated Server.</summary>
    public string? Software { get; set; }

    /// <summary>Detected mod loader (Forge, NeoForge, Fabric ...), when the server reveals one.</summary>
    public string? ModLoader { get; set; }

    /// <summary>JSON array of <c>{"id":..,"version":..}</c> objects.</summary>
    public string? ModsJson { get; set; }

    public int ModCount { get; set; }

    public bool? EnforcesSecureChat { get; set; }
    public bool? PreviewsChat { get; set; }
    public bool? PreventsChatReports { get; set; }

    public long? FaviconId { get; set; }
    public Favicon? Favicon { get; set; }

    // ---- Login probe ----

    public LoginProbeResult LoginResult { get; set; } = LoginProbeResult.NotProbed;

    /// <summary><c>true</c> = premium accounts only, <c>false</c> = "cracked"/offline mode, <c>null</c> = unknown.</summary>
    public bool? OnlineMode { get; set; }

    public bool? Whitelisted { get; set; }

    /// <summary>Plain text of the kick message when the login probe was disconnected.</summary>
    public string? LoginMessage { get; set; }

    // ---- Query protocol (enable-query=true) ----

    public bool QueryEnabled { get; set; }
    public string? QueryGameType { get; set; }
    public string? QueryMap { get; set; }
    public string? QuerySoftware { get; set; }

    /// <summary>JSON array of plugin strings as reported by the query protocol.</summary>
    public string? PluginsJson { get; set; }

    // ---- Bedrock ----

    public string? BedrockLevelName { get; set; }
    public string? BedrockGameMode { get; set; }
    public string? BedrockServerGuid { get; set; }

    // ---- Location (offline GeoIP lookup) ----

    public string? ContinentCode { get; set; }
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public long? Asn { get; set; }
    public string? AsOrganization { get; set; }

    public ICollection<ServerPlayer> Players { get; set; } = [];
    public ICollection<ServerSnapshot> Snapshots { get; set; } = [];
}
