using System.Text;
using McDiscovery.Protocol.Java;

namespace McDiscovery.Tests.Protocol;

public class JavaStatusParserTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void Parses_a_full_status()
    {
        var json = $$"""
            {"version":{"name":"Paper 1.21.4","protocol":769},
             "players":{"max":100,"online":5,"sample":[{"name":"Notch","id":"069a79f4-44e9-4726-a5be-fca90e38aaf5"}]},
             "description":{"text":"Hello","color":"aqua"},
             "favicon":"data:image/png;base64,{{Convert.ToBase64String(TinyPng)}}",
             "enforcesSecureChat":false,"previewsChat":true,"preventsChatReports":true}
            """;

        var status = JavaStatusParser.Parse(json, 42);

        Assert.Equal("Paper 1.21.4", status.VersionName);
        Assert.Equal(769, status.ProtocolVersion);
        Assert.Equal(100, status.PlayersMax);
        Assert.Equal(5, status.PlayersOnline);
        Assert.Equal("Notch", Assert.Single(status.Sample).Name);
        Assert.Equal("Hello", status.Motd.Plain);
        Assert.Equal("#55ffff", status.Motd.Segments[0].Color);
        Assert.Equal(TinyPng, status.Favicon);
        Assert.False(status.EnforcesSecureChat);
        Assert.True(status.PreviewsChat);
        Assert.True(status.PreventsChatReports);
        Assert.Equal(42, status.LatencyMs);
        Assert.Null(status.ModLoader);
    }

    [Fact]
    public void Tolerates_missing_and_odd_fields()
    {
        var status = JavaStatusParser.Parse("""{"version":{"name":"§cMaintenance","protocol":"x"},"players":{"online":1e20,"max":"7"},"description":"Hi"}""");
        Assert.Equal("Maintenance", status.VersionName);
        Assert.Null(status.ProtocolVersion);
        Assert.Equal(int.MaxValue, status.PlayersOnline);
        Assert.Equal(7, status.PlayersMax);
        Assert.Equal("Hi", status.Motd.Plain);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("\"string\"")]
    public void Rejects_non_objects(string json)
    {
        Assert.Throws<ProtocolException>(() => JavaStatusParser.Parse(json));
    }

    [Fact]
    public void Favicon_must_be_a_png()
    {
        Assert.Null(JavaStatusParser.DecodeFavicon("data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg onload=alert(1)>"))));
        Assert.Null(JavaStatusParser.DecodeFavicon("data:image/png;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg onload=alert(1)>"))));
        Assert.Null(JavaStatusParser.DecodeFavicon("data:image/png;base64,!!!notbase64"));
        Assert.Null(JavaStatusParser.DecodeFavicon("https://example.com/icon.png"));
        Assert.Equal(TinyPng, JavaStatusParser.DecodeFavicon("data:image/png;base64,\n" + Convert.ToBase64String(TinyPng)));
    }

    [Fact]
    public void Reads_old_forge_modinfo()
    {
        var status = JavaStatusParser.Parse("""
            {"version":{"name":"1.12.2","protocol":340},"description":"",
             "modinfo":{"type":"FML","modList":[{"modid":"minecraft","version":"1.12.2"},{"modid":"jei","version":"4.16"}]}}
            """);
        Assert.Equal("Forge", status.ModLoader);
        Assert.Equal([new ModInfo("minecraft", "1.12.2"), new ModInfo("jei", "4.16")], status.Mods);
    }

    [Fact]
    public void Reads_forgedata_mod_list()
    {
        var status = JavaStatusParser.Parse("""
            {"version":{"name":"1.16.5","protocol":754},"description":"",
             "forgeData":{"fmlNetworkVersion":2,"mods":[{"modId":"forge","modmarker":"36.2.0"},{"modId":"serveronly","modmarker":"OHNOES😱"}]}}
            """);
        Assert.Equal("Forge", status.ModLoader);
        Assert.Equal([new ModInfo("forge", "36.2.0"), new ModInfo("serveronly", null)], status.Mods);
    }

    [Fact]
    public void Reads_packed_forgedata()
    {
        // Build the payload exactly like Forge's serializeOptimized does.
        using var ms = new MemoryStream();
        ms.WriteByte(0); // not truncated
        ms.Write([0, 2]); // two mods
        WriteVarInt(ms, (1 << 1) | 0); // one channel, has version
        WriteString(ms, "jei");
        WriteString(ms, "19.0.1");
        WriteString(ms, "main");
        WriteString(ms, "1");
        ms.WriteByte(1);
        WriteVarInt(ms, (0 << 1) | 1); // no channels, server-only (no version)
        WriteString(ms, "secret");
        WriteVarInt(ms, 0); // no non-mod channels
        var encoded = ForgeDataDecoder.EncodeBytes(ms.ToArray());

        Assert.Equal(ms.ToArray(), ForgeDataDecoder.DecodeBytes(encoded));

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = new { name = "1.20.1", protocol = 763 },
            description = "",
            forgeData = new { channels = Array.Empty<object>(), mods = Array.Empty<object>(), fmlNetworkVersion = 3, d = encoded },
        });
        var status = JavaStatusParser.Parse(json);
        Assert.Equal([new ModInfo("jei", "19.0.1"), new ModInfo("secret", null)], status.Mods);
    }

    [Fact]
    public void Broken_forgedata_is_ignored()
    {
        Assert.Empty(ForgeDataDecoder.TryDecode("翿翿\u0001"));
        Assert.Empty(ForgeDataDecoder.TryDecode(""));
    }

    [Fact]
    public void Neoforge_is_detected()
    {
        var status = JavaStatusParser.Parse("""{"version":{"name":"1.21.1","protocol":767},"description":"","isModded":true}""");
        Assert.Equal("NeoForge", status.ModLoader);
    }

    [Fact]
    public void Legacy_responses()
    {
        var modern = JavaClient.ParseLegacyResponse("§1\u000047\u00001.4.2\u0000§aHello\u00003\u000020", 9);
        Assert.Equal(47, modern.ProtocolVersion);
        Assert.Equal("1.4.2", modern.VersionName);
        Assert.Equal("Hello", modern.Motd.Plain);
        Assert.Equal(3, modern.PlayersOnline);
        Assert.Equal(20, modern.PlayersMax);
        Assert.True(modern.IsLegacy);

        var beta = JavaClient.ParseLegacyResponse("Old server§1§10", 9);
        Assert.Equal("Old server", beta.Motd.Plain);
        Assert.Equal(1, beta.PlayersOnline);
        Assert.Equal(10, beta.PlayersMax);

        Assert.Throws<ProtocolException>(() => JavaClient.ParseLegacyResponse("garbage", 0));
    }

    private static void WriteVarInt(Stream s, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes);
    }
}
