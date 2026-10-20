using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace McDiscovery.FakeServer;

/// <summary>Shared receive loop for the UDP fakes.</summary>
public abstract class FakeUdpServer : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    protected FakeUdpServer(IPAddress address, int port)
    {
        _udp = new UdpClient(new IPEndPoint(address, port));
        _loop = LoopAsync();
    }

    public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public int Requests;

    protected abstract byte[]? Answer(byte[] request);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            Interlocked.Increment(ref Requests);
            if (Answer(received.Buffer) is { } reply)
            {
                await _udp.SendAsync(reply, received.RemoteEndPoint, _cts.Token);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _udp.Dispose();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Answers RakNet Unconnected Pings like a Bedrock Dedicated Server.</summary>
public sealed class FakeBedrockServer(IPAddress address, int port = 0) : FakeUdpServer(address, port)
{
    private static readonly byte[] Magic =
        [0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE, 0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78];

    public string Motd { get; set; } = "Dedicated Server";
    public string LevelName { get; set; } = "Bedrock level";

    protected override byte[]? Answer(byte[] request)
    {
        if (request.Length < 33 || request[0] != 0x01 || !request.AsSpan(9, 16).SequenceEqual(Magic))
        {
            return null;
        }

        var text = $"MCPE;{Motd};766;1.21.50;4;10;13253860892328930865;{LevelName};Survival;1;{Port};{Port + 1};";
        var payload = Encoding.UTF8.GetBytes(text);
        var reply = new byte[35 + payload.Length];
        reply[0] = 0x1C;
        request.AsSpan(1, 8).CopyTo(reply.AsSpan(1)); // echo the timestamp
        BinaryPrimitives.WriteInt64BigEndian(reply.AsSpan(9), 0x1122334455667788);
        Magic.CopyTo(reply, 17);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(33), (ushort)payload.Length);
        payload.CopyTo(reply, 35);
        return reply;
    }
}

/// <summary>Answers the GameSpy4 query protocol (enable-query=true).</summary>
public sealed class FakeQueryServer(IPAddress address, int port) : FakeUdpServer(address, port)
{
    private const int Token = 9513307;

    public string[] Players { get; set; } = ["Alice", "Bob_2"];

    protected override byte[]? Answer(byte[] request)
    {
        if (request.Length < 7 || request[0] != 0xFE || request[1] != 0xFD)
        {
            return null;
        }

        var session = request.AsSpan(3, 4).ToArray();
        if (request[2] == 0x09)
        {
            return [0x09, .. session, .. Encoding.ASCII.GetBytes(Token + "\0")];
        }

        if (request[2] != 0x00 || request.Length < 15 || BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(7)) != Token)
        {
            return null;
        }

        var body = new List<byte> { 0x00 };
        body.AddRange(session);
        body.AddRange([.. "splitnum\0"u8, 0x80, 0x00]); // 11 bytes of padding
        void Kv(string k, string v) => body.AddRange(Encoding.UTF8.GetBytes(k + "\0" + v + "\0"));
        Kv("hostname", "A Minecraft Server");
        Kv("gametype", "SMP");
        Kv("game_id", "MINECRAFT");
        Kv("version", "1.21.4");
        Kv("plugins", "Paper on 1.21.4-R0.1-SNAPSHOT: WorldEdit 7.3.0; LuckPerms 5.4.102");
        Kv("map", "world");
        Kv("numplayers", Players.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Kv("maxplayers", "20");
        Kv("hostport", Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Kv("hostip", "0.0.0.0");
        body.Add(0);
        body.AddRange("\u0001player_\0\0"u8.ToArray());
        foreach (var p in Players)
        {
            body.AddRange(Encoding.UTF8.GetBytes(p + "\0"));
        }

        body.Add(0);
        return [.. body];
    }
}
