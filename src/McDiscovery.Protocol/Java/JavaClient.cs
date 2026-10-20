using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace McDiscovery.Protocol.Java;

/// <summary>Java Edition server list ping (1.7+) and the legacy ping used by older servers.</summary>
public static class JavaClient
{
    /// <summary>Largest status response accepted (modded servers with big icons can reach a few hundred KB).</summary>
    public const int MaxStatusPacket = 4 * 1024 * 1024;

    /// <summary>Connects with the cancellation token as the timeout. The caller owns the returned socket.</summary>
    public static async Task<Socket> ConnectAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs the modern status handshake (handshake, status request, ping). Returns the parsed status.
    /// Throws on network errors, timeouts (<see cref="OperationCanceledException"/>) and protocol errors.
    /// </summary>
    public static async Task<JavaStatus> GetStatusAsync(IPEndPoint endpoint, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var socket = await ConnectAsync(endpoint, cts.Token);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var handshake = new PacketWriter()
            .WriteVarInt(-1) // "unknown" protocol: the convention for pings
            .WriteString(endpoint.Address.ToString())
            .WriteUShort((ushort)endpoint.Port)
            .WriteVarInt(1) // next state: status
            .ToArray();

        var sw = Stopwatch.StartNew();
        await stream.WriteAsync(Concat(PacketWriter.Frame(0x00, handshake), PacketWriter.Frame(0x00, [])), cts.Token);

        var response = await PacketStream.ReadPacketAsync(stream, MaxStatusPacket, compressed: false, cts.Token);
        var statusLatency = (int)sw.ElapsedMilliseconds;
        if (response.Id != 0x00)
        {
            throw new ProtocolException($"Unexpected status packet id {response.Id}");
        }

        var json = ReadStatusJson(response.Body);

        // The ping/pong gives a cleaner latency measurement. Many servers don't bother answering it.
        var latency = statusLatency;
        try
        {
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            pingCts.CancelAfter(TimeSpan.FromSeconds(2));
            var payload = Environment.TickCount64;
            sw.Restart();
            await stream.WriteAsync(PacketWriter.Frame(0x01, new PacketWriter().WriteLong(payload).ToArray()), pingCts.Token);
            var pong = await PacketStream.ReadPacketAsync(stream, 64, compressed: false, pingCts.Token);
            if (pong.Id == 0x01)
            {
                latency = (int)sw.ElapsedMilliseconds;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ProtocolException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }

        return JavaStatusParser.Parse(json, latency);
    }

    private static string ReadStatusJson(byte[] body)
    {
        var reader = new PacketReader(body);
        return reader.ReadString(MaxStatusPacket);
    }

    /// <summary>
    /// Legacy server list ping (Beta 1.8 - 1.6). Also answered by many modern servers and proxies, so it's
    /// used as a fallback when the modern ping fails.
    /// </summary>
    public static async Task<JavaStatus> GetLegacyStatusAsync(IPEndPoint endpoint, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var socket = await ConnectAsync(endpoint, cts.Token);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var sw = Stopwatch.StartNew();
        await stream.WriteAsync(BuildLegacyPing(endpoint), cts.Token);

        var header = new byte[3];
        await stream.ReadExactlyAsync(header, cts.Token);
        var latency = (int)sw.ElapsedMilliseconds;
        if (header[0] != 0xFF)
        {
            throw new ProtocolException("Not a legacy kick packet");
        }

        var chars = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));
        var data = new byte[chars * 2];
        await stream.ReadExactlyAsync(data, cts.Token);
        return ParseLegacyResponse(Encoding.BigEndianUnicode.GetString(data), latency);
    }

    internal static byte[] BuildLegacyPing(IPEndPoint endpoint)
    {
        var host = endpoint.Address.ToString();
        var channel = Encoding.BigEndianUnicode.GetBytes("MC|PingHost");
        var hostBytes = Encoding.BigEndianUnicode.GetBytes(host);

        using var ms = new MemoryStream();
        ms.Write([0xFE, 0x01, 0xFA]);
        WriteShort(ms, (ushort)"MC|PingHost".Length);
        ms.Write(channel);
        WriteShort(ms, (ushort)(7 + hostBytes.Length));
        ms.WriteByte(74); // protocol of 1.6.2
        WriteShort(ms, (ushort)host.Length);
        ms.Write(hostBytes);
        Span<byte> port = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(port, endpoint.Port);
        ms.Write(port);
        return ms.ToArray();

        static void WriteShort(Stream s, ushort v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            s.Write(b);
        }
    }

    internal static JavaStatus ParseLegacyResponse(string text, int latencyMs)
    {
        // 1.4+: "§1\0<protocol>\0<version>\0<motd>\0<online>\0<max>"
        if (text.StartsWith("§1\0", StringComparison.Ordinal))
        {
            var parts = text.Split('\0');
            if (parts.Length < 6)
            {
                throw new ProtocolException("Malformed legacy response");
            }

            return new JavaStatus
            {
                ProtocolVersion = int.TryParse(parts[1], out var p) ? p : null,
                VersionName = Text.MotdParser.Sanitize(Text.MotdParser.FromLegacy(parts[2]).Plain, 256),
                Motd = Text.MotdParser.FromLegacy(parts[3]),
                PlayersOnline = int.TryParse(parts[4], out var online) ? online : null,
                PlayersMax = int.TryParse(parts[5], out var max) ? max : null,
                LatencyMs = latencyMs,
                IsLegacy = true,
            };
        }

        // Beta 1.8 - 1.3: "<motd>§<online>§<max>"
        var fields = text.Split('§');
        if (fields.Length < 3)
        {
            throw new ProtocolException("Malformed legacy response");
        }

        return new JavaStatus
        {
            Motd = Text.MotdParser.FromLegacy(string.Join('§', fields[..^2])),
            PlayersOnline = int.TryParse(fields[^2], out var o) ? o : null,
            PlayersMax = int.TryParse(fields[^1], out var m) ? m : null,
            LatencyMs = latencyMs,
            IsLegacy = true,
        };
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
