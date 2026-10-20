using McDiscovery.Core.Entities;
using McDiscovery.Protocol.Bedrock;
using McDiscovery.Protocol.Java;
using McDiscovery.Protocol.Text;
using McDiscovery.Scanner.Discovery;
using McDiscovery.Scanner.Ingest;
using McDiscovery.Scanner.Probing;
using McDiscovery.Scanner.Targets;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Tests.Scanner;

public class IngestWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static ProbeTarget Target(string ip, ushort port = 25565, Edition edition = Edition.Java) =>
        new(IpV4.TryParse(ip, out var v) ? v : 0, port, edition);

    private static ProbeResult Online(ProbeTarget target, int players = 2, params PlayerSample[] sample) =>
        new(target, DateTime.UtcNow)
        {
            Java = new JavaStatus
            {
                VersionName = "Paper 1.21.4",
                ProtocolVersion = 769,
                PlayersOnline = players,
                PlayersMax = 20,
                Motd = MotdParser.FromLegacy("§aHello"),
                Sample = sample,
                Favicon = Png,
                LatencyMs = 12,
            },
            Login = new LoginProbeResponse(LoginOutcome.OnlineMode),
        };

    [Fact]
    public async Task Inserts_then_updates_a_server()
    {
        await using var db = await TempDatabase.CreateAsync();
        var writer = new IngestWriter(db.Options, geo: null);
        var target = Target("1.2.3.4");

        var first = await writer.WriteAsync([Online(target, 2)], Ct);
        Assert.Equal(new IngestStats(1, 0, 1), first);

        var second = await writer.WriteAsync([Online(target, 7)], Ct);
        Assert.Equal(new IngestStats(1, 0, 0), second);

        await using var ctx = db.NewContext();
        var server = await ctx.Servers.SingleAsync(Ct);
        Assert.Equal("1.2.3.4", server.Address);
        Assert.Equal(25565, server.Port);
        Assert.Equal(7, server.PlayersOnline);
        Assert.Equal(7, server.PeakPlayers);
        Assert.Equal("Paper", server.Software);
        Assert.Equal("Hello", server.MotdPlain);
        Assert.Equal("""[{"text":"Hello","color":"#55ff55"}]""", server.MotdSegmentsJson);
        Assert.True(server.OnlineMode);
        Assert.Equal(LoginProbeResult.OnlineMode, server.LoginResult);
        Assert.True(server.IsOnline);
        Assert.NotNull(server.FaviconId);
        Assert.Equal(2, await ctx.Snapshots.CountAsync(Ct));
        Assert.Equal(1, await ctx.Favicons.CountAsync(Ct));
    }

    [Fact]
    public async Task Marks_known_servers_offline_and_ignores_unknown_ones()
    {
        await using var db = await TempDatabase.CreateAsync();
        var writer = new IngestWriter(db.Options, geo: null);
        var known = Target("1.2.3.4");
        await writer.WriteAsync([Online(known)], Ct);

        var stats = await writer.WriteAsync([new ProbeResult(known, DateTime.UtcNow), new ProbeResult(Target("5.6.7.8"), DateTime.UtcNow)], Ct);
        Assert.Equal(new IngestStats(0, 1, 0), stats);

        await using var ctx = db.NewContext();
        var server = await ctx.Servers.SingleAsync(Ct);
        Assert.False(server.IsOnline);
        Assert.Equal(1, server.ConsecutiveFailures);
        Assert.Equal(2, server.PlayersOnline); // last known value is kept
        Assert.Equal([true, false], await ctx.Snapshots.OrderBy(s => s.Id).Select(s => s.IsOnline).ToListAsync(Ct));
    }

    [Fact]
    public async Task Players_are_deduplicated_and_fake_entries_dropped()
    {
        await using var db = await TempDatabase.CreateAsync();
        var writer = new IngestWriter(db.Options, geo: null);
        PlayerSample[] sample =
        [
            new("Notch", "069a79f4-44e9-4726-a5be-fca90e38aaf5"),
            new("§aWelcome", "00000000-0000-0000-0000-000000000000"),
            new("NoUuid", null),
            new("has space", "853c80ef-3c37-49fd-aa49-938b674adae6"),
        ];

        // Same player on two servers, seen twice on the first; plus a duplicate result in one batch.
        await writer.WriteAsync([Online(Target("1.1.1.1"), 1, sample), Online(Target("1.1.1.1"), 1, sample), Online(Target("2.2.2.2"), 1, sample)], Ct);
        await writer.WriteAsync([Online(Target("1.1.1.1"), 1, sample)], Ct);

        await using var ctx = db.NewContext();
        var player = await ctx.Players.SingleAsync(Ct);
        Assert.Equal("Notch", player.Name);
        Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", player.Uuid);
        var links = await ctx.ServerPlayers.Include(sp => sp.Server).OrderBy(sp => sp.Server!.Address).ToListAsync(Ct);
        Assert.Equal(2, links.Count);
        Assert.Equal(2, links[0].TimesSeen);
        Assert.Equal(1, links[1].TimesSeen);
        Assert.Equal(1, await ctx.Favicons.CountAsync(Ct)); // shared icon stored once
    }

    [Fact]
    public async Task Query_players_without_uuid_are_stored_by_name()
    {
        await using var db = await TempDatabase.CreateAsync();
        var writer = new IngestWriter(db.Options, geo: null);
        var result = Online(Target("3.3.3.3")) with
        {
            Query = new QueryResult { Players = ["Alice", "alice", "Bad Name"], Plugins = ["WorldEdit 7"], Software = "Paper on 1.21.4", Map = "world" },
        };

        await writer.WriteAsync([result], Ct);
        await writer.WriteAsync([result], Ct);

        await using var ctx = db.NewContext();
        var players = await ctx.Players.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(Ct);
        Assert.Equal(["Alice"], players); // case-insensitive duplicate dropped
        var server = await ctx.Servers.SingleAsync(Ct);
        Assert.True(server.QueryEnabled);
        Assert.Equal("""["WorldEdit 7"]""", server.PluginsJson);
        Assert.Equal("world", server.QueryMap);
    }

    [Fact]
    public async Task Bedrock_servers_are_separate_from_java()
    {
        await using var db = await TempDatabase.CreateAsync();
        var writer = new IngestWriter(db.Options, geo: null);
        var bedrock = new ProbeResult(Target("4.4.4.4", 19132, Edition.Bedrock), DateTime.UtcNow)
        {
            Bedrock = new BedrockStatus
            {
                EditionTag = "MCPE",
                Motd = MotdParser.FromLegacy("Bedrock"),
                Version = "1.21.50",
                PlayersOnline = 3,
                PlayersMax = 10,
                LevelName = "level",
                GameMode = "Survival",
            },
        };

        await writer.WriteAsync([bedrock, Online(Target("4.4.4.4"))], Ct);

        await using var ctx = db.NewContext();
        var servers = await ctx.Servers.OrderBy(s => s.Edition).ToListAsync(Ct);
        Assert.Equal(2, servers.Count);
        Assert.Equal(Edition.Bedrock, servers[1].Edition);
        Assert.Equal("level", servers[1].BedrockLevelName);
        Assert.Equal(3, servers[1].PlayersOnline);
    }

    [Theory]
    [InlineData("Paper 1.21.4", "Paper")]
    [InlineData("Velocity 3.3.0-SNAPSHOT (git-abc)", "Velocity")]
    [InlineData("BungeeCord 1.8.x-1.21.x", "BungeeCord")]
    [InlineData("Purpur 1.20.4", "Purpur")]
    [InlineData("1.21.4", null)]
    [InlineData("Leaflet 1.0", null)]
    [InlineData("NeoForge 21.1", "NeoForge")]
    public void Software_detection(string version, string? expected)
    {
        Assert.Equal(expected, SoftwareDetector.Detect(version));
    }

    [Theory]
    [InlineData("Notch", true)]
    [InlineData("jeb_", true)]
    [InlineData(".BedrockPlayer", true)]
    [InlineData("§cRed", false)]
    [InlineData("has space", false)]
    [InlineData("waytoolongname_12345", false)]
    [InlineData("", false)]
    public void Player_names(string name, bool valid) => Assert.Equal(valid, IngestWriter.IsValidName(name));
}
