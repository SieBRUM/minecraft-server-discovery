using System.Net;
using McDiscovery.Core.Entities;
using McDiscovery.FakeServer;
using McDiscovery.Scanner;
using McDiscovery.Scanner.Discovery;
using McDiscovery.Scanner.Probing;
using McDiscovery.Scanner.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace McDiscovery.Tests.Scanner;

/// <summary>End-to-end runs against fake servers on loopback (no traffic leaves the machine).</summary>
public class ScanPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TargetSet Loopback() => TargetSet.Create([TargetParser.Parse("127.0.0.1")], [], applyMandatoryExcludes: false);

    private static ScanSettings Settings(TempDatabase db, IReadOnlyList<ushort> javaPorts, IReadOnlyList<ushort> bedrockPorts) => new()
    {
        DatabasePath = db.Path,
        GeoIpDirectory = Path.Combine(db.Directory, "geoip"),
        GeoIpAutoUpdate = false,
        Engine = "connect",
        JavaPorts = javaPorts,
        BedrockPorts = bedrockPorts,
        Concurrency = 8,
        FlushInterval = TimeSpan.FromMilliseconds(200),
        Discovery = new DiscoveryOptions { Rate = 1000, UdpWait = TimeSpan.FromSeconds(1), ConnectTimeout = TimeSpan.FromSeconds(1) },
        Probe = new ProbeOptions { StatusTimeout = TimeSpan.FromSeconds(3), QueryTimeout = TimeSpan.FromSeconds(1) },
    };

    private static ushort FreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return (ushort)port;
    }

    [Fact]
    public async Task Scan_discovers_java_and_bedrock_servers()
    {
        await using var db = await TempDatabase.CreateAsync();
        await using var java = new FakeJavaServer(IPAddress.Loopback);
        await using var query = new FakeQueryServer(IPAddress.Loopback, java.Port);
        await using var bedrock = new FakeBedrockServer(IPAddress.Loopback);
        var closed = FreeTcpPort();

        var pipeline = new ScanPipeline(Settings(db, [(ushort)java.Port, closed], [(ushort)bedrock.Port]), NullLoggerFactory.Instance);
        Assert.True(await pipeline.RunScanAsync(Loopback(), "test", Ct));

        await using var ctx = db.NewContext();
        var servers = await ctx.Servers.OrderBy(s => s.Edition).ToListAsync(Ct);
        Assert.Equal(2, servers.Count);

        var j = servers[0];
        Assert.Equal(Edition.Java, j.Edition);
        Assert.Equal(java.Port, j.Port);
        Assert.Equal("Paper 1.21.4", j.VersionName);
        Assert.True(j.QueryEnabled);
        Assert.Equal(LoginProbeResult.OnlineMode, j.LoginResult);
        Assert.Equal(2 + 2, await ctx.Players.CountAsync(Ct)); // Notch, jeb_ from the sample + Alice, Bob_2 from query

        Assert.Equal(Edition.Bedrock, servers[1].Edition);
        Assert.Equal("Dedicated Server", servers[1].MotdPlain);

        var run = await ctx.ScanRuns.SingleAsync(Ct);
        Assert.True(run.Completed);
        Assert.Equal(2, run.OpenPorts);
        Assert.Equal(2, run.NewServers);
        Assert.Equal("connect+bedrock-udp", run.Engine);
    }

    [Fact]
    public async Task Rescan_updates_known_servers_and_marks_dead_ones_offline()
    {
        await using var db = await TempDatabase.CreateAsync();
        await using var java = new FakeJavaServer(IPAddress.Loopback) { Login = LoginBehavior.Success };
        var dead = FreeTcpPort();

        await using (var ctx = db.NewContext())
        {
            var old = DateTime.UtcNow.AddDays(-1);
            ctx.Servers.AddRange(
                new Server { Address = "127.0.0.1", Port = java.Port, Edition = Edition.Java, FirstSeenUtc = old, LastSeenUtc = old, LastScannedUtc = old },
                new Server { Address = "127.0.0.1", Port = dead, Edition = Edition.Java, FirstSeenUtc = old, LastSeenUtc = old, LastScannedUtc = old, IsOnline = true });
            await ctx.SaveChangesAsync(Ct);
        }

        var pipeline = new ScanPipeline(Settings(db, [], []), NullLoggerFactory.Instance);
        Assert.True(await pipeline.RunRescanAsync(new RescanFilter(), Ct));

        await using var check = db.NewContext();
        var alive = await check.Servers.SingleAsync(s => s.Port == java.Port, Ct);
        Assert.True(alive.IsOnline);
        Assert.False(alive.OnlineMode);
        Assert.Equal(LoginProbeResult.OfflineMode, alive.LoginResult);
        Assert.Equal(3, alive.PlayersOnline);

        var gone = await check.Servers.SingleAsync(s => s.Port == dead, Ct);
        Assert.False(gone.IsOnline);
        Assert.Equal(1, gone.ConsecutiveFailures);
    }

    [Fact]
    public async Task Connect_engine_reports_only_open_ports()
    {
        await using var java = new FakeJavaServer(IPAddress.Loopback);
        var closed = FreeTcpPort();
        var engine = new ConnectScanEngine(new DiscoveryOptions { Rate = 1000 }, NullLogger<ConnectScanEngine>.Instance);
        var found = new List<ProbeTarget>();
        var progress = new DiscoveryProgress();

        await engine.RunAsync(Loopback(), [(ushort)java.Port, closed], t =>
        {
            lock (found)
            {
                found.Add(t);
            }

            return ValueTask.CompletedTask;
        }, progress, Ct);

        Assert.Equal((ushort)java.Port, Assert.Single(found).Port);
        Assert.Equal(2, progress.Sent);
        Assert.Equal(1, progress.Found);
    }

    [Fact]
    public void Masscan_output_and_arguments()
    {
        Assert.True(MasscanEngine.TryParseDiscovered("Discovered open port 25565/tcp on 93.184.216.34", out var target));
        Assert.Equal("93.184.216.34", IpV4.Format(target.Address));
        Assert.Equal(25565, target.Port);
        Assert.True(MasscanEngine.TryParseDiscovered("Discovered open port 25565/tcp on 1.2.3.4      ", out _));
        Assert.False(MasscanEngine.TryParseDiscovered("Discovered open port 19132/udp on 1.2.3.4", out _));
        Assert.False(MasscanEngine.TryParseDiscovered("rate:  0.00-kpps, 100.00% done", out _));
        Assert.Equal(12.34, MasscanEngine.ParsePercentDone("rate:  9.98-kpps, 12.34% done,   1:02:03 remaining, found=5"));
        Assert.Null(MasscanEngine.ParsePercentDone("waiting 3-secs"));
        Assert.False(MasscanEngine.TryParseDiscovered("Discovered open port 25565/tcp on 1.2.3.4; rm -rf /", out _));

        var engine = new MasscanEngine(new DiscoveryOptions { Rate = 5000, Seed = 7, MasscanAdapter = "eth0", MasscanExtraArgs = ["--ttl", "64"] },
            NullLogger<MasscanEngine>.Instance);
        var args = engine.BuildArguments("/tmp/targets.txt", [25565, 25566]);
        Assert.Equal(["-iL", "/tmp/targets.txt", "-p", "25565,25566", "--rate", "5000", "--wait", "10", "--exclude", "255.255.255.255",
            "--seed", "7", "--adapter", "eth0", "--ttl", "64"], args);
    }
}
