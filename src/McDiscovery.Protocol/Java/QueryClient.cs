using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using McDiscovery.Protocol.Text;

namespace McDiscovery.Protocol.Java;

/// <summary>Result of a GameSpy4 "full stat" query (servers with <c>enable-query=true</c>).</summary>
public sealed record QueryResult
{
    public string? Motd { get; init; }
    public string? GameType { get; init; }
    public string? GameId { get; init; }
    public string? Version { get; init; }

    /// <summary>Server software, e.g. "CraftBukkit on Bukkit 1.20.4" (the part before the colon of "plugins").</summary>
    public string? Software { get; init; }

    public IReadOnlyList<string> Plugins { get; init; } = [];
    public string? Map { get; init; }
    public int? PlayersOnline { get; init; }
    public int? PlayersMax { get; init; }
    public IReadOnlyList<string> Players { get; init; } = [];
}

/// <summary>Minecraft Java query protocol over UDP.</summary>
public static class QueryClient
{
    private const int MaxListEntries = 512;

    public static async Task<QueryResult> QueryAsync(IPEndPoint endpoint, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(endpoint, cts.Token);

        var sessionId = Random.Shared.Next() & 0x0F0F0F0F;
        var buffer = new byte[65536];

        // 1. Handshake: get a challenge token
        await socket.SendAsync(BuildHandshake(sessionId), cts.Token);
        var received = await socket.ReceiveAsync(buffer, cts.Token);
        var token = ParseChallenge(buffer.AsSpan(0, received), sessionId);

        // 2. Full stat
        await socket.SendAsync(BuildFullStatRequest(sessionId, token), cts.Token);
        received = await socket.ReceiveAsync(buffer, cts.Token);
        return ParseFullStat(buffer.AsSpan(0, received), sessionId);
    }

    internal static byte[] BuildHandshake(int sessionId)
    {
        var packet = new byte[7];
        packet[0] = 0xFE;
        packet[1] = 0xFD;
        packet[2] = 0x09;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(3), sessionId);
        return packet;
    }

    internal static byte[] BuildFullStatRequest(int sessionId, int token)
    {
        var packet = new byte[15];
        packet[0] = 0xFE;
        packet[1] = 0xFD;
        packet[2] = 0x00;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(3), sessionId);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(7), token);
        // 4 bytes of padding request the full stat instead of the basic one.
        return packet;
    }

    internal static int ParseChallenge(ReadOnlySpan<byte> data, int sessionId)
    {
        if (data.Length < 6 || data[0] != 0x09 || BinaryPrimitives.ReadInt32BigEndian(data[1..]) != sessionId)
        {
            throw new ProtocolException("Bad query handshake response");
        }

        var text = Encoding.ASCII.GetString(data[5..]).TrimEnd('\0');
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var token))
        {
            throw new ProtocolException("Bad query challenge token");
        }

        return unchecked((int)token);
    }

    internal static QueryResult ParseFullStat(ReadOnlySpan<byte> data, int sessionId)
    {
        // type(1) session(4) padding(11) k\0v\0 ... \0 padding(10) player\0 ... \0
        if (data.Length < 16 || data[0] != 0x00 || BinaryPrimitives.ReadInt32BigEndian(data[1..]) != sessionId)
        {
            throw new ProtocolException("Bad query response");
        }

        var pos = 16;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (pos < data.Length)
        {
            var key = ReadCString(data, ref pos);
            if (key.Length == 0)
            {
                break;
            }

            var value = ReadCString(data, ref pos);
            if (values.Count < 64)
            {
                values.TryAdd(key, value);
            }
        }

        var players = new List<string>();
        pos += 10; // "\x01player_\0\0"
        while (pos < data.Length && players.Count < MaxListEntries)
        {
            var name = ReadCString(data, ref pos);
            if (name.Length == 0)
            {
                break;
            }

            players.Add(MotdParser.Sanitize(name, 64));
        }

        var (software, plugins) = ParsePlugins(values.GetValueOrDefault("plugins"));

        return new QueryResult
        {
            Motd = Clean(values.GetValueOrDefault("hostname"), 1024),
            GameType = Clean(values.GetValueOrDefault("gametype"), 64),
            GameId = Clean(values.GetValueOrDefault("game_id"), 64),
            Version = Clean(values.GetValueOrDefault("version"), 128),
            Software = software,
            Plugins = plugins,
            Map = Clean(values.GetValueOrDefault("map"), 128),
            PlayersOnline = int.TryParse(values.GetValueOrDefault("numplayers"), CultureInfo.InvariantCulture, out var n) ? n : null,
            PlayersMax = int.TryParse(values.GetValueOrDefault("maxplayers"), CultureInfo.InvariantCulture, out var m) ? m : null,
            Players = players,
        };
    }

    /// <summary>"CraftBukkit on Bukkit 1.2.5-R4.0: WorldEdit 5.3; CommandBook 2.1" → software + plugin list.</summary>
    internal static (string? Software, IReadOnlyList<string> Plugins) ParsePlugins(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, []);
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        var software = Clean(colon >= 0 ? value[..colon] : value, 128);
        var plugins = colon >= 0
            ? value[(colon + 1)..]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => MotdParser.Sanitize(p, 128))
                .Where(p => p.Length > 0)
                .Take(MaxListEntries)
                .ToList()
            : [];
        return (software, plugins);
    }

    private static string? Clean(string? value, int max)
    {
        var plain = value is null ? null : MotdParser.FromLegacy(value).Plain;
        return string.IsNullOrWhiteSpace(plain) ? null : MotdParser.Sanitize(plain, max);
    }

    private static string ReadCString(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length)
        {
            return "";
        }

        var rest = data[pos..];
        var end = rest.IndexOf((byte)0);
        if (end < 0)
        {
            end = rest.Length;
        }

        pos += end + 1;
        // Vanilla encodes as ISO-8859-1 in old versions and UTF-8 in newer ones; UTF-8 is the better guess.
        return Encoding.UTF8.GetString(rest[..end]);
    }
}
