namespace McDiscovery.Protocol.Java;

/// <summary>
/// Decodes the packed <c>forgeData.d</c> string Forge (1.18+) puts in its status response. Each UTF-16 char
/// carries 15 bits; the first two chars hold the byte length. The bytes contain:
/// <c>bool truncated, ushort modCount, mods[varint flags, string id, string version?, channels...], ...</c>.
/// See Forge's <c>ServerStatusPing.serializeOptimized</c>.
/// </summary>
public static class ForgeDataDecoder
{
    private const int MaxBytes = 1 << 20;

    /// <summary>Forge replaces the version of server-side-only mods with this marker.</summary>
    public static bool IsServerOnlyMarker(string version) => version.StartsWith("OHNOES", StringComparison.Ordinal);

    public static IReadOnlyList<ModInfo> TryDecode(string encoded)
    {
        try
        {
            return Decode(DecodeBytes(encoded));
        }
        catch (ProtocolException)
        {
            return [];
        }
    }

    internal static byte[] DecodeBytes(string s)
    {
        if (s.Length < 2)
        {
            throw new ProtocolException("forgeData too short");
        }

        var size = (s[0] & 0x7FFF) | ((s[1] & 0x7FFF) << 15);
        if (size < 0 || size > MaxBytes)
        {
            throw new ProtocolException("forgeData size out of range");
        }

        var bytes = new byte[size];
        var written = 0;
        long buffer = 0;
        var bitsInBuffer = 0;
        for (var i = 2; i < s.Length && written < size; i++)
        {
            buffer |= (long)(s[i] & 0x7FFF) << bitsInBuffer;
            bitsInBuffer += 15;
            while (bitsInBuffer >= 8 && written < size)
            {
                bytes[written++] = (byte)buffer;
                buffer >>= 8;
                bitsInBuffer -= 8;
            }
        }

        if (written < size && bitsInBuffer > 0)
        {
            bytes[written++] = (byte)buffer;
        }

        if (written < size)
        {
            throw new ProtocolException("forgeData truncated");
        }

        return bytes;
    }

    /// <summary>Encodes bytes the same way Forge does (used by tests).</summary>
    internal static string EncodeBytes(ReadOnlySpan<byte> data)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((char)(data.Length & 0x7FFF));
        sb.Append((char)((data.Length >> 15) & 0x7FFF));
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            if (bits >= 15)
            {
                sb.Append((char)(buffer & 0x7FFF));
                buffer >>= 15;
                bits -= 15;
            }

            buffer |= b << bits;
            bits += 8;
        }

        if (bits > 0)
        {
            sb.Append((char)(buffer & 0x7FFF));
        }

        return sb.ToString();
    }

    private static List<ModInfo> Decode(byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        _ = reader.ReadBool(); // truncated
        int modCount = reader.ReadUShort();
        var mods = new List<ModInfo>(Math.Min(modCount, JavaStatusParser.MaxMods));
        for (var i = 0; i < modCount; i++)
        {
            var flags = reader.ReadVarInt();
            var channelCount = flags >>> 1;
            var serverOnly = (flags & 1) != 0;
            var id = reader.ReadString(1024);
            var version = serverOnly ? null : reader.ReadString(1024);
            for (var c = 0; c < channelCount; c++)
            {
                reader.ReadString(1024); // channel name
                reader.ReadString(1024); // channel version
                reader.ReadBool(); // required on client
            }

            if (mods.Count < JavaStatusParser.MaxMods)
            {
                mods.Add(new ModInfo(id, version));
            }
        }

        return mods;
    }
}
