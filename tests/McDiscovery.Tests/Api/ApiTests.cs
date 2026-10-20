using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using McDiscovery.Api.Security;
using McDiscovery.Core.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace McDiscovery.Tests.Api;

/// <summary>Runs the real API in-process against a temporary database.</summary>
public sealed class ApiTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateAsync();
        await using var ctx = _db.NewContext();
        var now = DateTime.UtcNow;
        var icon = new Favicon { Sha256 = new string('a', 64), Data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] };
        var notch = new Player { Name = "Notch", Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5", FirstSeenUtc = now, LastSeenUtc = now };
        var s1 = new Server
        {
            Address = "1.2.3.4", Port = 25565, Edition = Edition.Java, IsOnline = true, FirstSeenUtc = now.AddDays(-3), LastSeenUtc = now,
            LastScannedUtc = now, VersionName = "Paper 1.21.4", Software = "Paper", PlayersOnline = 10, PlayersMax = 50, CountryCode = "NL",
            CountryName = "Netherlands", Latitude = 52.37, Longitude = 4.89, MotdPlain = "100% fun_server",
            MotdSegmentsJson = """[{"text":"100% fun","color":"#ff0000"},{"text":"_server","color":"javascript:alert(1)"}]""",
            OnlineMode = true, Favicon = icon,
        };
        var s2 = new Server
        {
            Address = "5.6.7.8", Port = 19132, Edition = Edition.Bedrock, IsOnline = false, FirstSeenUtc = now.AddDays(-1), LastSeenUtc = now.AddHours(-2),
            LastScannedUtc = now, VersionName = "1.21.50", PlayersOnline = 0, CountryCode = "DE", CountryName = "Germany", MotdPlain = "<img src=x onerror=alert(1)>",
        };
        ctx.Servers.AddRange(s1, s2);
        ctx.ServerPlayers.Add(new ServerPlayer { Server = s1, Player = notch, FirstSeenUtc = now, LastSeenUtc = now, TimesSeen = 1 });
        ctx.Snapshots.Add(new ServerSnapshot { Server = s1, TimestampUtc = now.AddHours(-1), IsOnline = true, PlayersOnline = 8 });
        await ctx.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private WebApplicationFactory<McDiscovery.Api.ApiAssembly> Factory(string clientIp = "127.0.0.1", string? allowed = null, int? rateLimit = null) =>
        new WebApplicationFactory<McDiscovery.Api.ApiAssembly>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Logging:LogLevel:Default", "Warning");
            b.UseSetting("Database:Path", _db.Path);
            if (allowed is not null)
            {
                b.UseSetting("Security:AllowedNetworks:0", allowed);
            }

            if (rateLimit is not null)
            {
                b.UseSetting("Security:RateLimitPerMinute", rateLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // TestServer has no real socket: pretend the request comes from clientIp.
            b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse(clientIp))));
        });

    private sealed class RemoteIpStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) =>
            {
                ctx.Connection.RemoteIpAddress = address;
                return n(ctx);
            });
            next(app);
        };
    }

    [Fact]
    public async Task Loopback_is_allowed_by_default_and_others_are_not()
    {
        await using var local = Factory();
        Assert.Equal(HttpStatusCode.OK, (await local.CreateClient().GetAsync("/api/health", Ct)).StatusCode);

        await using var remote = Factory(clientIp: "8.8.8.8");
        var response = await remote.CreateClient().GetAsync("/api/servers", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.CreateClient().GetAsync("/", Ct)).StatusCode);

        await using var mapped = Factory(clientIp: "::ffff:127.0.0.1");
        Assert.Equal(HttpStatusCode.OK, (await mapped.CreateClient().GetAsync("/api/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Allowlist_can_be_configured()
    {
        await using var factory = Factory(clientIp: "203.0.113.9", allowed: "203.0.113.0/24");
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/api/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().GetAsync("/api/stats", Ct);
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Only_get_is_supported()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().PostAsync("/api/servers", new StringContent("{}"), Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var delete = await factory.CreateClient().DeleteAsync("/api/servers/1", Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        var spa = await factory.CreateClient().PutAsync("/servers", new StringContent(""), Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, spa.StatusCode);
        Assert.Equal("nosniff", spa.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Unknown_api_paths_are_404()
    {
        await using var factory = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/does-not-exist", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/servers/999", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/servers/abc", Ct)).StatusCode);
    }

    [Fact]
    public async Task Lists_filters_and_sorts_servers()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        var all = await client.GetFromJsonAsync<JsonElement>("/api/servers", Ct);
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal("1.2.3.4", all.GetProperty("items")[0].GetProperty("address").GetString()); // newest lastSeen first

        var java = await client.GetFromJsonAsync<JsonElement>("/api/servers?edition=java&online=true&country=nl&minPlayers=5", Ct);
        Assert.Equal(1, java.GetProperty("total").GetInt32());
        var item = java.GetProperty("items")[0];
        Assert.Equal("Java", item.GetProperty("edition").GetString());
        Assert.True(item.GetProperty("hasFavicon").GetBoolean());

        var asc = await client.GetFromJsonAsync<JsonElement>("/api/servers?sort=players&dir=asc", Ct);
        Assert.Equal("5.6.7.8", asc.GetProperty("items")[0].GetProperty("address").GetString());

        var byIp = await client.GetFromJsonAsync<JsonElement>("/api/servers?q=5.6.7.8", Ct);
        Assert.Equal(1, byIp.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("%", 1)] // literal %, not a wildcard (only "100% fun_server" contains it)
    [InlineData("_", 1)]
    [InlineData("' OR 1=1 --", 0)]
    [InlineData("\"; DROP TABLE Servers; --", 0)]
    [InlineData("<img", 1)]
    public async Task Search_treats_input_as_text(string q, int expected)
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/servers?q=" + Uri.EscapeDataString(q), Ct);
        Assert.Equal(expected, result.GetProperty("total").GetInt32());

        // The table still exists.
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/api/servers", Ct)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Rejects_bad_paging_and_long_search()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/servers?q=" + new string('a', 101), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/servers?page=2000000000&pageSize=100", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/servers?page=abc", Ct)).StatusCode);

        var clamped = await client.GetFromJsonAsync<JsonElement>("/api/servers?pageSize=100000&page=-5", Ct);
        Assert.Equal(100, clamped.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, clamped.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Detail_history_players_and_favicon()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/servers?edition=java", Ct);
        var id = list.GetProperty("items")[0].GetProperty("id").GetInt64();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/servers/{id}", Ct);
        var motd = detail.GetProperty("summary").GetProperty("motd");
        Assert.Equal("#ff0000", motd[0].GetProperty("color").GetString());
        Assert.Equal(JsonValueKind.Null, motd[1].GetProperty("color").ValueKind); // invalid colour stripped by the API too
        Assert.Equal("Notch", detail.GetProperty("players")[0].GetProperty("name").GetString());
        Assert.Equal("NotProbed", detail.GetProperty("loginResult").GetString());

        var history = await client.GetFromJsonAsync<JsonElement>($"/api/servers/{id}/history?days=7", Ct);
        Assert.Equal(8, history[0].GetProperty("players").GetInt32());

        var favicon = await client.GetAsync($"/api/servers/{id}/favicon", Ct);
        Assert.Equal(HttpStatusCode.OK, favicon.StatusCode);
        Assert.Equal("image/png", favicon.Content.Headers.ContentType?.MediaType);

        var players = await client.GetFromJsonAsync<JsonElement>("/api/players?q=not", Ct);
        Assert.Equal(1, players.GetProperty("total").GetInt32());
        var playerId = players.GetProperty("items")[0].GetProperty("id").GetInt64();
        var player = await client.GetFromJsonAsync<JsonElement>($"/api/players/{playerId}", Ct);
        Assert.Equal("1.2.3.4", player.GetProperty("servers")[0].GetProperty("address").GetString());

        var byUuid = await client.GetFromJsonAsync<JsonElement>("/api/players?q=069A79F444E94726A5BEFCA90E38AAF5", Ct);
        Assert.Equal(1, byUuid.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Stats_map_and_facets()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        var stats = await client.GetFromJsonAsync<JsonElement>("/api/stats", Ct);
        Assert.Equal(2, stats.GetProperty("servers").GetInt32());
        Assert.Equal(1, stats.GetProperty("online").GetInt32());
        Assert.Equal(10, stats.GetProperty("playersOnline").GetInt64());
        Assert.Equal(1, stats.GetProperty("knownPlayers").GetInt32());
        Assert.Equal(2, stats.GetProperty("countries").GetArrayLength());

        var map = await client.GetFromJsonAsync<JsonElement>("/api/map", Ct);
        var point = Assert.Single(map.EnumerateArray());
        Assert.Equal(52.4, point.GetProperty("lat").GetDouble());
        Assert.Equal(1, point.GetProperty("count").GetInt32());

        var facets = await client.GetFromJsonAsync<JsonElement>("/api/facets", Ct);
        Assert.Equal("Paper", facets.GetProperty("software")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task Rate_limit_applies_per_client()
    {
        await using var factory = Factory(rateLimit: 3);
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/health", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("10.1.2.3/8", "10.200.0.1", true)]
    [InlineData("10.1.2.3/8", "11.0.0.1", false)]
    [InlineData("1.2.3.4", "1.2.3.4", true)]
    [InlineData("1.2.3.4", "1.2.3.5", false)]
    [InlineData("::1", "::1", true)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    [InlineData("fd00::/8", "fd12::1", true)]
    public void Allowlist_matching(string network, string client, bool allowed)
    {
        Assert.Equal(allowed, new IpAllowlist([network]).IsAllowed(IPAddress.Parse(client)));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/abc")]
    public void Invalid_allowlist_entries_fail_fast(string network)
    {
        Assert.ThrowsAny<FormatException>(() => new IpAllowlist([network]));
    }
}
