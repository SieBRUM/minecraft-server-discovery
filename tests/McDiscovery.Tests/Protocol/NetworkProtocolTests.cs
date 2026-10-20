using System.Net;
using System.Net.Sockets;
using McDiscovery.FakeServer;
using McDiscovery.Protocol.Bedrock;
using McDiscovery.Protocol.Java;

namespace McDiscovery.Tests.Protocol;

/// <summary>Talks to the in-process fake servers over real loopback sockets.</summary>
public class NetworkProtocolTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Status_ping()
    {
        await using var server = new FakeJavaServer(IPAddress.Loopback);
        var status = await JavaClient.GetStatusAsync(new IPEndPoint(IPAddress.Loopback, server.Port), Timeout, Ct);

        Assert.Equal("Paper 1.21.4", status.VersionName);
        Assert.Equal(769, status.ProtocolVersion);
        Assert.Equal(3, status.PlayersOnline);
        Assert.Equal(20, status.PlayersMax);
        Assert.Equal("A Minecraft Server bold", status.Motd.Plain);
        Assert.Equal(3, status.Sample.Count);
        Assert.True(status.EnforcesSecureChat);
        Assert.NotNull(status.LatencyMs);
        Assert.False(status.IsLegacy);
    }

    [Fact]
    public async Task Status_ping_without_pong_still_works()
    {
        await using var server = new FakeJavaServer(IPAddress.Loopback) { AnswerPing = false };
        var status = await JavaClient.GetStatusAsync(new IPEndPoint(IPAddress.Loopback, server.Port), Timeout, Ct);
        Assert.Equal(3, status.PlayersOnline);
    }

    [Fact]
    public async Task Legacy_ping()
    {
        await using var server = new FakeJavaServer(IPAddress.Loopback) { ModernPing = false };
        var endpoint = new IPEndPoint(IPAddress.Loopback, server.Port);

        await Assert.ThrowsAnyAsync<Exception>(() => JavaClient.GetStatusAsync(endpoint, TimeSpan.FromSeconds(1), Ct));
        var status = await JavaClient.GetLegacyStatusAsync(endpoint, Timeout, Ct);
        Assert.Equal("1.8.9", status.VersionName);
        Assert.Equal("A Legacy Server", status.Motd.Plain);
        Assert.Equal(5, status.PlayersOnline);
        Assert.Equal(50, status.PlayersMax);
    }

    [Fact]
    public async Task Status_ping_times_out_on_silent_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptSocketAsync(Ct); // accept but never answer

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JavaClient.GetStatusAsync(new IPEndPoint(IPAddress.Loopback, port), TimeSpan.FromMilliseconds(500), Ct));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        (await accept).Dispose();
    }

    [Fact]
    public async Task Status_ping_rejects_oversized_packets()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(Ct);
            await Task.Delay(100, Ct);
            await socket.SendAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07 }, Ct); // length = int.MaxValue
            await Task.Delay(500, Ct);
        }, Ct);

        await Assert.ThrowsAsync<ProtocolException>(() =>
            JavaClient.GetStatusAsync(new IPEndPoint(IPAddress.Loopback, port), Timeout, Ct));
        await serve;
    }

    [Theory]
    [InlineData(LoginBehavior.Encryption, 769, LoginOutcome.OnlineMode)]
    [InlineData(LoginBehavior.Encryption, 340, LoginOutcome.OnlineMode)]
    [InlineData(LoginBehavior.EncryptionNoAuth, 769, LoginOutcome.OfflineMode)]
    [InlineData(LoginBehavior.Success, 769, LoginOutcome.OfflineMode)]
    [InlineData(LoginBehavior.Success, 760, LoginOutcome.OfflineMode)]
    [InlineData(LoginBehavior.Success, 47, LoginOutcome.OfflineMode)]
    [InlineData(LoginBehavior.CompressionThenSuccess, 769, LoginOutcome.OfflineMode)]
    [InlineData(LoginBehavior.Whitelist, 769, LoginOutcome.Whitelisted)]
    public async Task Login_probe(LoginBehavior behavior, int protocol, LoginOutcome expected)
    {
        await using var server = new FakeJavaServer(IPAddress.Loopback) { Login = behavior };
        var result = await LoginProbe.ProbeAsync(new IPEndPoint(IPAddress.Loopback, server.Port), protocol, "Probe_1", Timeout, Ct);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal("Probe_1", server.LastLoginName);
        Assert.Equal(protocol, server.LastLoginProtocol);
    }

    [Fact]
    public async Task Login_probe_kick_message()
    {
        await using var server = new FakeJavaServer(IPAddress.Loopback)
        {
            Login = LoginBehavior.Kick,
            KickMessage = """{"text":"Outdated client! Please use ","extra":[{"text":"1.21.4","color":"red"}]}""",
        };
        var result = await LoginProbe.ProbeAsync(new IPEndPoint(IPAddress.Loopback, server.Port), 769, "Probe", Timeout, Ct);
        Assert.Equal(LoginOutcome.Kicked, result.Outcome);
        Assert.Equal("Outdated client! Please use 1.21.4", result.Message);
    }

    [Fact]
    public async Task Query_full_stat()
    {
        await using var java = new FakeJavaServer(IPAddress.Loopback);
        await using var query = new FakeQueryServer(IPAddress.Loopback, java.Port);
        var result = await QueryClient.QueryAsync(new IPEndPoint(IPAddress.Loopback, query.Port), Timeout, Ct);

        Assert.Equal("A Minecraft Server", result.Motd);
        Assert.Equal("SMP", result.GameType);
        Assert.Equal("1.21.4", result.Version);
        Assert.Equal("Paper on 1.21.4-R0.1-SNAPSHOT", result.Software);
        Assert.Equal(["WorldEdit 7.3.0", "LuckPerms 5.4.102"], result.Plugins);
        Assert.Equal("world", result.Map);
        Assert.Equal(2, result.PlayersOnline);
        Assert.Equal(20, result.PlayersMax);
        Assert.Equal(["Alice", "Bob_2"], result.Players);
    }

    [Fact]
    public async Task Query_times_out_when_disabled()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        await Assert.ThrowsAnyAsync<Exception>(() => QueryClient.QueryAsync(new IPEndPoint(IPAddress.Loopback, port), TimeSpan.FromMilliseconds(300), Ct));
    }

    [Fact]
    public async Task Bedrock_ping()
    {
        await using var server = new FakeBedrockServer(IPAddress.Loopback);
        var status = await BedrockPing.PingAsync(new IPEndPoint(IPAddress.Loopback, server.Port), Timeout, Ct);

        Assert.Equal("MCPE", status.EditionTag);
        Assert.Equal("Dedicated Server", status.Motd.Plain);
        Assert.Equal(766, status.ProtocolVersion);
        Assert.Equal("1.21.50", status.Version);
        Assert.Equal(4, status.PlayersOnline);
        Assert.Equal(10, status.PlayersMax);
        Assert.Equal("Bedrock level", status.LevelName);
        Assert.Equal("Survival", status.GameMode);
        Assert.Equal("13253860892328930865", status.ServerGuid);
    }

    [Fact]
    public void Bedrock_pong_parser_rejects_garbage()
    {
        Assert.Null(BedrockPing.TryParsePong(new byte[10]));
        Assert.Null(BedrockPing.TryParsePong(new byte[64]));

        var ping = BedrockPing.BuildPing(1, 2);
        Assert.Equal(33, ping.Length);
        Assert.Equal(0x01, ping[0]);
    }
}
