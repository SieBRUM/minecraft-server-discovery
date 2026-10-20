using System.Buffers.Binary;
using System.Text;
using McDiscovery.Protocol.Java;

namespace McDiscovery.Tests.Protocol;

public class PacketTests
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(127, new byte[] { 0x7f })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(25565, new byte[] { 0xdd, 0xc7, 0x01 })]
    [InlineData(2147483647, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x07 })]
    [InlineData(-1, new byte[] { 0xff, 0xff, 0xff, 0xff, 0x0f })]
    public void VarInt_round_trip(int value, byte[] expected)
    {
        var bytes = new PacketWriter().WriteVarInt(value).ToArray();
        Assert.Equal(expected, bytes);
        var reader = new PacketReader(bytes);
        Assert.Equal(value, reader.ReadVarInt());
    }

    [Fact]
    public void Reader_rejects_truncated_and_oversized_data()
    {
        Assert.Throws<ProtocolException>(() => new PacketReader([0x80]).ReadVarInt());
        Assert.Throws<ProtocolException>(() => new PacketReader([0xff, 0xff, 0xff, 0xff, 0xff, 0x01]).ReadVarInt());
        Assert.Throws<ProtocolException>(() => new PacketReader([0x05, 0x41]).ReadString());
        Assert.Throws<ProtocolException>(() => new PacketReader([0x05, 0x41, 0x41, 0x41, 0x41, 0x41]).ReadString(maxBytes: 4));
    }

    [Fact]
    public void Offline_uuid_matches_minecraft()
    {
        // Known value: offline-mode UUID of "Notch".
        Assert.Equal(Guid.Parse("b50ad385-829d-3141-a216-7e7d7539ba7f"), LoginProbe.OfflineUuid("Notch"));
    }

    [Theory]
    [InlineData(47, 1 + 5)] // name only
    [InlineData(759, 1 + 5 + 1)] // + has signature (false)
    [InlineData(760, 1 + 5 + 1 + 1 + 16)] // + has signature + has uuid + uuid
    [InlineData(763, 1 + 5 + 1 + 16)] // has uuid + uuid
    [InlineData(764, 1 + 5 + 16)] // uuid
    [InlineData(769, 1 + 5 + 16)]
    public void Login_start_layout_depends_on_protocol(int protocol, int expectedLength)
    {
        Assert.Equal(expectedLength, LoginProbe.BuildLoginStart(protocol, "Probe").Length);
    }

    [Fact]
    public void Whitelist_kick_is_classified()
    {
        Assert.Equal(LoginOutcome.Whitelisted, LoginProbe.Classify("You are not white-listed on this server!").Outcome);
        Assert.Equal(LoginOutcome.Whitelisted, LoginProbe.Classify("You are not whitelisted").Outcome);
        Assert.Equal(LoginOutcome.Kicked, LoginProbe.Classify("Server is restarting").Outcome);
    }

    [Fact]
    public void Legacy_ping_packet_layout()
    {
        var packet = JavaClient.BuildLegacyPing(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("1.2.3.4"), 25565));
        Assert.Equal([0xFE, 0x01, 0xFA], packet[..3]);
        Assert.Equal(11, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(3)));
        Assert.Equal("MC|PingHost", Encoding.BigEndianUnicode.GetString(packet, 5, 22));
        var rest = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(27));
        Assert.Equal(packet.Length - 29, rest);
        Assert.Equal(25565, BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(packet.Length - 4)));
    }

    [Fact]
    public void Query_parsing_rejects_wrong_session()
    {
        var response = new byte[20];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(1), 1234);
        Assert.Throws<ProtocolException>(() => QueryClient.ParseFullStat(response, 4321));
        Assert.Throws<ProtocolException>(() => QueryClient.ParseChallenge([0x09, 0, 0, 0, 1, (byte)'x', 0], 1));
        Assert.Equal(-12345, QueryClient.ParseChallenge([0x09, 0, 0, 0, 1, .. Encoding.ASCII.GetBytes("-12345\0")], 1));
    }

    [Fact]
    public void Query_plugins_without_list()
    {
        var (software, plugins) = QueryClient.ParsePlugins("Vanilla");
        Assert.Equal("Vanilla", software);
        Assert.Empty(plugins);
    }
}
