using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace McDiscovery.FakeServer;

public enum LoginBehavior
{
    /// <summary>Encryption Request (online mode).</summary>
    Encryption,

    /// <summary>Encryption Request with "should authenticate" = false (1.20.5+ offline mode with encryption).</summary>
    EncryptionNoAuth,

    /// <summary>Login Success (offline mode).</summary>
    Success,

    /// <summary>Set Compression followed by a compressed Login Success.</summary>
    CompressionThenSuccess,

    /// <summary>Disconnect with a whitelist message.</summary>
    Whitelist,

    /// <summary>Disconnect with an arbitrary message.</summary>
    Kick,

    /// <summary>Close the connection without answering.</summary>
    Close,
}

/// <summary>
/// Minimal Java Edition server: status + ping, legacy ping, login start. Implemented independently of the
/// scanner's protocol code on purpose, so the tests don't just check the code against itself.
/// </summary>
public sealed class FakeJavaServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public FakeJavaServer(IPAddress address, int port = 0)
    {
        _listener = new TcpListener(address, port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string StatusJson { get; set; } = DefaultStatusJson("A Minecraft Server", 3, 20);

    public bool ModernPing { get; set; } = true;
    public bool LegacyPing { get; set; } = true;
    public bool AnswerPing { get; set; } = true;
    public LoginBehavior Login { get; set; } = LoginBehavior.Encryption;
    public string KickMessage { get; set; } = "{\"text\":\"You are not white-listed on this server!\"}";
    public string LegacyResponse { get; set; } = "§1\u000047\u00001.8.9\u0000A Legacy Server\u00005\u000050";

    public int StatusRequests;
    public int LoginRequests;
    public int LastLoginProtocol;
    public string? LastLoginName;

    public static string DefaultStatusJson(string motd, int online, int max) =>
        $$"""
        {"version":{"name":"Paper 1.21.4","protocol":769},
         "players":{"max":{{max}},"online":{{online}},"sample":[
            {"name":"Notch","id":"069a79f4-44e9-4726-a5be-fca90e38aaf5"},
            {"name":"jeb_","id":"853c80ef-3c37-49fd-aa49-938b674adae6"},
            {"name":"§aWelcome!","id":"00000000-0000-0000-0000-000000000000"}]},
         "description":{"text":"{{motd}}","color":"gold","extra":[{"text":" §lbold","color":"#ff0000"}]},
         "enforcesSecureChat":true}
        """;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var ct = timeout.Token;
                var stream = client.GetStream();

                var first = new byte[1];
                await stream.ReadExactlyAsync(first, ct);
                if (first[0] == 0xFE)
                {
                    if (LegacyPing)
                    {
                        await SendLegacyAsync(stream, ct);
                    }

                    return;
                }

                if (!ModernPing)
                {
                    return; // pretend to be a pre-1.7 server: ignore modern handshakes
                }

                // first[0] is the first byte of the handshake length VarInt
                var length = await ReadVarIntAsync(stream, first[0], ct);
                var handshake = new byte[length];
                await stream.ReadExactlyAsync(handshake, ct);
                var pos = 0;
                ReadVarInt(handshake, ref pos); // packet id 0
                var protocol = ReadVarInt(handshake, ref pos);
                ReadString(handshake, ref pos); // address
                pos += 2; // port
                var nextState = ReadVarInt(handshake, ref pos);

                if (nextState == 1)
                {
                    await HandleStatusAsync(stream, ct);
                }
                else if (nextState == 2)
                {
                    await HandleLoginAsync(stream, protocol, ct);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or EndOfStreamException)
            {
                // client went away
            }
        }
    }

    private async Task HandleStatusAsync(NetworkStream stream, CancellationToken ct)
    {
        await ReadPacketAsync(stream, ct); // status request
        Interlocked.Increment(ref StatusRequests);
        await WritePacketAsync(stream, 0x00, StringBytes(StatusJson), ct);
        if (!AnswerPing)
        {
            return;
        }

        var ping = await ReadPacketAsync(stream, ct);
        await WritePacketAsync(stream, 0x01, ping.AsSpan(1).ToArray(), ct);
    }

    private async Task HandleLoginAsync(NetworkStream stream, int protocol, CancellationToken ct)
    {
        var loginStart = await ReadPacketAsync(stream, ct);
        var pos = 1;
        LastLoginName = ReadString(loginStart, ref pos);
        LastLoginProtocol = protocol;
        Interlocked.Increment(ref LoginRequests);

        switch (Login)
        {
            case LoginBehavior.Encryption:
            case LoginBehavior.EncryptionNoAuth:
                var body = new List<byte>();
                body.AddRange(StringBytes(""));
                body.AddRange(VarInt(4));
                body.AddRange([1, 2, 3, 4]);
                body.AddRange(VarInt(4));
                body.AddRange([5, 6, 7, 8]);
                if (protocol >= 766)
                {
                    body.Add(Login == LoginBehavior.Encryption ? (byte)1 : (byte)0);
                }

                await WritePacketAsync(stream, 0x01, [.. body], ct);
                break;
            case LoginBehavior.Success:
                await WritePacketAsync(stream, 0x02, LoginSuccessBody(), ct);
                break;
            case LoginBehavior.CompressionThenSuccess:
                await WritePacketAsync(stream, 0x03, VarInt(16), ct);
                await WriteCompressedPacketAsync(stream, 0x02, LoginSuccessBody(), ct);
                break;
            case LoginBehavior.Whitelist:
            case LoginBehavior.Kick:
                await WritePacketAsync(stream, 0x00, StringBytes(KickMessage), ct);
                break;
            case LoginBehavior.Close:
                return;
        }

        // Give the client a moment to read before the socket closes.
        await Task.Delay(200, ct);
    }

    private static byte[] LoginSuccessBody()
    {
        var body = new List<byte>();
        body.AddRange(new byte[16]); // uuid
        body.AddRange(StringBytes("Probe"));
        body.AddRange(VarInt(0)); // properties
        return [.. body];
    }

    private async Task SendLegacyAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read whatever else the client sent (the 1.6 ping has more bytes after 0xFE 0x01), so closing the
        // socket doesn't turn into a reset that discards our answer.
        await Task.Delay(50, ct);
        var drain = new byte[1024];
        while (stream.DataAvailable)
        {
            _ = await stream.ReadAsync(drain, ct);
        }

        var text = Encoding.BigEndianUnicode.GetBytes(LegacyResponse);
        var packet = new byte[3 + text.Length];
        packet[0] = 0xFF;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), (ushort)LegacyResponse.Length);
        text.CopyTo(packet, 3);
        await stream.WriteAsync(packet, ct);
        await Task.Delay(200, ct);
    }

    // ---- framing ----

    private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
    {
        var length = await ReadVarIntAsync(stream, null, ct);
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, ct);
        return data;
    }

    private static async Task WritePacketAsync(NetworkStream stream, int id, byte[] body, CancellationToken ct)
    {
        var inner = VarInt(id).Concat(body).ToArray();
        await stream.WriteAsync(VarInt(inner.Length).Concat(inner).ToArray(), ct);
    }

    private static async Task WriteCompressedPacketAsync(NetworkStream stream, int id, byte[] body, CancellationToken ct)
    {
        var inner = VarInt(id).Concat(body).ToArray();
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(inner);
        }

        var payload = VarInt(inner.Length).Concat(ms.ToArray()).ToArray();
        await stream.WriteAsync(VarInt(payload.Length).Concat(payload).ToArray(), ct);
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, byte? firstByte, CancellationToken ct)
    {
        var result = 0;
        var shift = 0;
        var b = new byte[1];
        while (true)
        {
            byte value;
            if (firstByte is { } f)
            {
                value = f;
                firstByte = null;
            }
            else
            {
                await stream.ReadExactlyAsync(b, ct);
                value = b[0];
            }

            result |= (value & 0x7F) << shift;
            if ((value & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 28)
            {
                throw new IOException("VarInt too long");
            }
        }
    }

    private static int ReadVarInt(byte[] data, ref int pos)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        var length = ReadVarInt(data, ref pos);
        var s = Encoding.UTF8.GetString(data, pos, length);
        pos += length;
        return s;
    }

    internal static byte[] VarInt(int value)
    {
        var bytes = new List<byte>();
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (v != 0);

        return [.. bytes];
    }

    private static byte[] StringBytes(string s)
    {
        var data = Encoding.UTF8.GetBytes(s);
        return [.. VarInt(data.Length), .. data];
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}
