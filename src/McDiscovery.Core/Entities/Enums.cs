namespace McDiscovery.Core.Entities;

public enum Edition
{
    Java = 0,
    Bedrock = 1,
}

public enum LoginProbeResult
{
    /// <summary>The login probe was not run (disabled, Bedrock, or the server is offline).</summary>
    NotProbed = 0,

    /// <summary>The server asked for encryption: premium (online-mode) accounts only.</summary>
    OnlineMode = 1,

    /// <summary>The server let an unauthenticated player in: offline mode ("cracked").</summary>
    OfflineMode = 2,

    /// <summary>The server kicked the probe with a whitelist message.</summary>
    Whitelisted = 3,

    /// <summary>The server kicked the probe for another reason (see the login message).</summary>
    Kicked = 4,

    /// <summary>The server sent a plugin request (proxies, Forge handshakes) so the mode can't be determined.</summary>
    PluginRequest = 5,

    /// <summary>The connection failed or the response could not be understood.</summary>
    Failed = 6,
}

public enum ScanKind
{
    Scan = 0,
    Rescan = 1,
}
