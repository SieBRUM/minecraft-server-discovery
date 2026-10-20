namespace McDiscovery.Core.Entities;

/// <summary>One probe result for a server, used for player-count and uptime history.</summary>
public class ServerSnapshot
{
    public long Id { get; set; }
    public long ServerId { get; set; }
    public Server? Server { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool IsOnline { get; set; }
    public int? PlayersOnline { get; set; }
    public int? LatencyMs { get; set; }
}

/// <summary>A server icon. Stored once per unique PNG (many servers share the same icon).</summary>
public class Favicon
{
    public long Id { get; set; }

    /// <summary>Lower-case hex SHA-256 of <see cref="Data"/>.</summary>
    public required string Sha256 { get; set; }

    public required byte[] Data { get; set; }
}

/// <summary>Bookkeeping for one scanner run.</summary>
public class ScanRun
{
    public long Id { get; set; }
    public ScanKind Kind { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }

    /// <summary>Short human readable description of what was scanned.</summary>
    public string Description { get; set; } = "";

    public string Engine { get; set; } = "";
    public long TargetCount { get; set; }
    public long OpenPorts { get; set; }
    public long ServersOnline { get; set; }
    public long NewServers { get; set; }
    public bool Completed { get; set; }
}
