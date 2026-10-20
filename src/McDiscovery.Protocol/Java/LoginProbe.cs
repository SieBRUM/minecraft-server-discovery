using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using McDiscovery.Protocol.Text;

namespace McDiscovery.Protocol.Java;

public enum LoginOutcome
{
    OnlineMode,
    OfflineMode,
    Whitelisted,
    Kicked,
    PluginRequest,
    Failed,
}

public sealed record LoginProbeResponse(LoginOutcome Outcome, string? Message = null);

/// <summary>
/// Starts a login to find out whether the server checks accounts with Mojang (online mode). The probe never
/// finishes joining: it disconnects as soon as the server answers the Login Start packet.
/// Note that on offline-mode servers before 1.20.2 the server already treats the probe as joined at that point,
/// so it shows up in the server log (and may be announced in chat).
/// </summary>
public static class LoginProbe
{
    private static readonly string[] WhitelistHints =
    [
        "whitelist", "white-list", "white list", "whitelisted", "not on the list", "allowlist",
    ];

    public static async Task<LoginProbeResponse> ProbeAsync(
        IPEndPoint endpoint, int protocolVersion, string username, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var socket = await JavaClient.ConnectAsync(endpoint, cts.Token);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var handshake = new PacketWriter()
            .WriteVarInt(protocolVersion)
            .WriteString(endpoint.Address.ToString())
            .WriteUShort((ushort)endpoint.Port)
            .WriteVarInt(2) // next state: login
            .ToArray();

        var frames = new MemoryStream();
        frames.Write(PacketWriter.Frame(0x00, handshake));
        frames.Write(PacketWriter.Frame(0x00, BuildLoginStart(protocolVersion, username)));
        await stream.WriteAsync(frames.ToArray(), cts.Token);

        var compressed = false;
        for (var i = 0; i < 4; i++)
        {
            var packet = await PacketStream.ReadPacketAsync(stream, 1 << 20, compressed, cts.Token);
            switch (packet.Id)
            {
                case 0x00: // Disconnect (login)
                    return Classify(ReadDisconnect(packet.Body));
                case 0x01: // Encryption Request
                    return new LoginProbeResponse(ReadEncryptionRequestAuthenticates(packet.Body, protocolVersion)
                        ? LoginOutcome.OnlineMode
                        : LoginOutcome.OfflineMode);
                case 0x02: // Login Success
                    return new LoginProbeResponse(LoginOutcome.OfflineMode);
                case 0x03: // Set Compression: following packets use the compressed format
                    compressed = true;
                    continue;
                case 0x04: // Login Plugin Request (Velocity forwarding, Forge handshake ...)
                    return new LoginProbeResponse(LoginOutcome.PluginRequest);
                default:
                    return new LoginProbeResponse(LoginOutcome.Failed, $"Unexpected login packet 0x{packet.Id:x2}");
            }
        }

        return new LoginProbeResponse(LoginOutcome.Failed, "No conclusive login response");
    }

    /// <summary>The Login Start layout changed several times; build it for the server's protocol version.</summary>
    internal static byte[] BuildLoginStart(int protocol, string username)
    {
        var w = new PacketWriter().WriteString(username);
        var uuid = OfflineUuid(username);
        switch (protocol)
        {
            case >= 764: // 1.20.2+: UUID is mandatory
                w.WriteUuid(uuid);
                break;
            case >= 761: // 1.19.3 - 1.20.1: optional UUID
                w.WriteBool(true).WriteUuid(uuid);
                break;
            case 760: // 1.19.1 - 1.19.2: optional signature data, optional UUID
                w.WriteBool(false).WriteBool(true).WriteUuid(uuid);
                break;
            case 759: // 1.19: optional signature data
                w.WriteBool(false);
                break;
        }

        return w.ToArray();
    }

    /// <summary>The UUID an offline-mode server assigns to a name: v3 MD5 of "OfflinePlayer:name".</summary>
    internal static Guid OfflineUuid(string name)
    {
#pragma warning disable CA5351 // MD5 is what Minecraft uses for name-based UUIDs; not a security use.
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
#pragma warning restore CA5351
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, bigEndian: true);
    }

    private static string ReadDisconnect(byte[] body)
    {
        var reader = new PacketReader(body);
        return MotdParser.FromJsonString(reader.ReadString(262144)).Plain;
    }

    /// <summary>
    /// Since 1.20.5 the encryption request says whether the client must authenticate with Mojang; offline-mode
    /// servers that still use encryption (some proxies) send <c>false</c>.
    /// </summary>
    private static bool ReadEncryptionRequestAuthenticates(byte[] body, int protocol)
    {
        if (protocol < 766)
        {
            return true;
        }

        try
        {
            var reader = new PacketReader(body);
            reader.ReadString(64); // server id
            reader.ReadBytes(reader.ReadVarInt()); // public key
            reader.ReadBytes(reader.ReadVarInt()); // verify token
            return reader.Remaining == 0 || reader.ReadBool();
        }
        catch (ProtocolException)
        {
            return true;
        }
    }

    internal static LoginProbeResponse Classify(string message)
    {
        var clean = MotdParser.Sanitize(message, 512);
        foreach (var hint in WhitelistHints)
        {
            if (clean.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return new LoginProbeResponse(LoginOutcome.Whitelisted, clean);
            }
        }

        return new LoginProbeResponse(LoginOutcome.Kicked, clean);
    }
}
