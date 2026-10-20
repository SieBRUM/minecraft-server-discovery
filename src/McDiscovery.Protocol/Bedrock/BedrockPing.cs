using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using McDiscovery.Protocol.Text;

namespace McDiscovery.Protocol.Bedrock;

/// <summary>Information from a RakNet Unconnected Pong sent by a Bedrock Edition server.</summary>
public sealed record BedrockStatus
{
    /// <summary>"MCPE" (Bedrock) or "MCEE" (Education Edition).</summary>
    public required string EditionTag { get; init; }

    public required ParsedText Motd { get; init; }
    public int? ProtocolVersion { get; init; }
    public string? Version { get; init; }
    public int? PlayersOnline { get; init; }
    public int? PlayersMax { get; init; }
    public string? ServerGuid { get; init; }
    public string? LevelName { get; init; }
    public string? GameMode { get; init; }
    public int? Port { get; init; }
    public int? LatencyMs { get; init; }
}

/// <summary>Builds and parses RakNet unconnected ping/pong packets.</summary>
public static class BedrockPing
{
    public const int DefaultPort = 19132;

    private const byte UnconnectedPing = 0x01;
    private const byte UnconnectedPong = 0x1C;

    internal static readonly byte[] Magic =
        [0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE, 0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78];

    /// <summary>Builds an Unconnected Ping (33 bytes).</summary>
    public static byte[] BuildPing(long timestamp, long clientGuid)
    {
        var packet = new byte[1 + 8 + 16 + 8];
        packet[0] = UnconnectedPing;
        BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(1), timestamp);
        Magic.CopyTo(packet, 9);
        BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(25), clientGuid);
        return packet;
    }

    /// <summary>Parses an Unconnected Pong. Returns <c>null</c> for anything that isn't a Minecraft pong.</summary>
    public static BedrockStatus? TryParsePong(ReadOnlySpan<byte> data, int? latencyMs = null)
    {
        // id(1) time(8) serverGuid(8) magic(16) length(2) string
        if (data.Length < 35 || data[0] != UnconnectedPong || !data.Slice(17, 16).SequenceEqual(Magic))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(data[33..]);
        if (length > data.Length - 35)
        {
            return null;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data.Slice(35, length));
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.UTF8.GetString(data.Slice(35, length));
        }

        // MCPE;motd;protocol;version;online;max;serverId;levelName;gameMode;gameModeId;portV4;portV6;
        var f = text.Split(';');
        if (f.Length < 6 || (f[0] != "MCPE" && f[0] != "MCEE"))
        {
            return null;
        }

        return new BedrockStatus
        {
            EditionTag = f[0],
            Motd = MotdParser.FromLegacy(f[1]),
            ProtocolVersion = ParseInt(f, 2),
            Version = Field(f, 3, 64),
            PlayersOnline = ParseInt(f, 4),
            PlayersMax = ParseInt(f, 5),
            ServerGuid = Field(f, 6, 32),
            LevelName = f.Length > 7 ? NullIfEmpty(MotdParser.Sanitize(MotdParser.FromLegacy(f[7]).Plain, 128)) : null,
            GameMode = Field(f, 8, 32),
            Port = ParseInt(f, 10),
            LatencyMs = latencyMs,
        };
    }

    /// <summary>Pings one Bedrock server.</summary>
    public static async Task<BedrockStatus> PingAsync(IPEndPoint endpoint, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(endpoint, cts.Token);
        var buffer = new byte[2048];

        // UDP is lossy: retry a couple of times within the timeout.
        var attempt = TimeSpan.FromTicks(Math.Max(timeout.Ticks / 3, TimeSpan.FromMilliseconds(300).Ticks));
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await socket.SendAsync(BuildPing(Environment.TickCount64, Random.Shared.NextInt64()), cts.Token);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            attemptCts.CancelAfter(attempt);
            try
            {
                while (true)
                {
                    var received = await socket.ReceiveAsync(buffer, attemptCts.Token);
                    if (TryParsePong(buffer.AsSpan(0, received), (int)sw.ElapsedMilliseconds) is { } status)
                    {
                        return status;
                    }
                }
            }
            catch (OperationCanceledException) when (!cts.IsCancellationRequested)
            {
                // try again
            }
        }
    }

    private static string? Field(string[] f, int index, int max) =>
        f.Length > index ? NullIfEmpty(MotdParser.Sanitize(f[index], max)) : null;

    private static int? ParseInt(string[] f, int index) =>
        f.Length > index && int.TryParse(f[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
