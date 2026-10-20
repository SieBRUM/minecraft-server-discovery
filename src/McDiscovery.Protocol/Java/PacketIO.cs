using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace McDiscovery.Protocol.Java;

/// <summary>Thrown when a server sends data that does not follow the protocol.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Builds a Java Edition packet body.</summary>
internal sealed class PacketWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new(256);

    public PacketWriter WriteVarInt(int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        WriteByte((byte)v);
        return this;
    }

    public PacketWriter WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(bytes.Length);
        _buffer.Write(bytes);
        return this;
    }

    public PacketWriter WriteUShort(ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        _buffer.Write(b);
        return this;
    }

    public PacketWriter WriteLong(long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, value);
        _buffer.Write(b);
        return this;
    }

    public PacketWriter WriteBool(bool value)
    {
        WriteByte(value ? (byte)1 : (byte)0);
        return this;
    }

    public PacketWriter WriteUuid(Guid uuid)
    {
        _buffer.Write(uuid.ToByteArray(bigEndian: true));
        return this;
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    private void WriteByte(byte b)
    {
        _buffer.GetSpan(1)[0] = b;
        _buffer.Advance(1);
    }

    /// <summary>Wraps a packet id and body into a length-prefixed (uncompressed) frame.</summary>
    public static byte[] Frame(int packetId, ReadOnlySpan<byte> body)
    {
        var inner = new PacketWriter().WriteVarInt(packetId);
        inner._buffer.Write(body);
        var innerBytes = inner.ToArray();
        var frame = new PacketWriter().WriteVarInt(innerBytes.Length);
        frame._buffer.Write(innerBytes);
        return frame.ToArray();
    }
}

/// <summary>Reads fields from a packet body. All reads are bounds checked.</summary>
internal ref struct PacketReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos;

    public readonly int Remaining => _data.Length - _pos;

    public int ReadVarInt()
    {
        var result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new ProtocolException("VarInt is too long");
    }

    public byte ReadByte()
    {
        if (_pos >= _data.Length)
        {
            throw new ProtocolException("Unexpected end of packet");
        }

        return _data[_pos++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUShort() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));

    public long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new ProtocolException("Unexpected end of packet");
        }

        var slice = _data.Slice(_pos, count);
        _pos += count;
        return slice;
    }

    public string ReadString(int maxBytes = 1 << 21)
    {
        var length = ReadVarInt();
        if (length < 0 || length > maxBytes)
        {
            throw new ProtocolException($"String length {length} out of range");
        }

        return Encoding.UTF8.GetString(ReadBytes(length));
    }

    public ReadOnlySpan<byte> ReadRest() => ReadBytes(Remaining);
}

/// <summary>A received packet: id plus body.</summary>
internal sealed record Packet(int Id, byte[] Body);

internal static class PacketStream
{
    /// <summary>Reads one length-prefixed packet. Supports the compressed format after Set Compression.</summary>
    public static async Task<Packet> ReadPacketAsync(Stream stream, int maxLength, bool compressed, CancellationToken ct)
    {
        var length = await ReadVarIntAsync(stream, ct);
        if (length <= 0 || length > maxLength)
        {
            throw new ProtocolException($"Packet length {length} out of range");
        }

        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, ct);

        byte[] payload = buffer;
        if (compressed)
        {
            var reader = new PacketReader(buffer);
            var dataLength = reader.ReadVarInt();
            var headerSize = length - reader.Remaining;
            if (dataLength == 0)
            {
                payload = buffer[headerSize..];
            }
            else
            {
                if (dataLength < 0 || dataLength > maxLength)
                {
                    throw new ProtocolException($"Uncompressed length {dataLength} out of range");
                }

                payload = new byte[dataLength];
                using var zlib = new ZLibStream(new MemoryStream(buffer, headerSize, length - headerSize), CompressionMode.Decompress);
                zlib.ReadExactly(payload);
            }
        }

        var body = new PacketReader(payload);
        var id = body.ReadVarInt();
        var idSize = payload.Length - body.Remaining;
        return new Packet(id, payload[idSize..]);
    }

    public static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var result = 0;
        var one = new byte[1];
        for (var shift = 0; shift < 35; shift += 7)
        {
            await stream.ReadExactlyAsync(one, ct);
            result |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
            {
                return result;
            }
        }

        throw new ProtocolException("VarInt is too long");
    }
}
