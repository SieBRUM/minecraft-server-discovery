namespace McDiscovery.Core.Entities;

/// <summary>
/// A player name seen in a server's player sample or query response. Players are keyed by UUID when the
/// server reports one; offline-mode servers use name-based UUIDs, so the same name can appear more than once.
/// </summary>
public class Player
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Dashed lower-case UUID, or <c>null</c> when only the name is known (query protocol).</summary>
    public string? Uuid { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public ICollection<ServerPlayer> Servers { get; set; } = [];
}

/// <summary>Links a player to a server they were seen on.</summary>
public class ServerPlayer
{
    public long ServerId { get; set; }
    public Server? Server { get; set; }

    public long PlayerId { get; set; }
    public Player? Player { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int TimesSeen { get; set; }
}
