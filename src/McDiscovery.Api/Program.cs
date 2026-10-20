using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using McDiscovery.Api.Endpoints;
using McDiscovery.Api.Security;
using McDiscovery.Core.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------------------------------------
var security = builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();
security.ApplyEnvironment(Environment.GetEnvironmentVariable);
var allowlist = new IpAllowlist(security.AllowedNetworks); // throws on invalid entries: fail fast at startup
var dbPath = Database.ResolvePath(builder.Configuration["Database:Path"]);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 16 * 1024; // the API is read-only
    o.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
});

builder.Services.AddSingleton(allowlist);
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseAppSqlite(dbPath)
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
builder.Services.AddMemoryCache();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

if (security.TrustedProxies.Count > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
        foreach (var proxy in security.TrustedProxies)
        {
            o.KnownIPNetworks.Add(SecurityOptions.ParseNetwork(proxy));
        }
    });
}

if (security.RateLimitPerMinute > 0)
{
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = security.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
    });
}

var app = builder.Build();

// ---- Database ------------------------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    await Database.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
}

app.Logger.LogInformation("Database: {Path}. Allowed client networks: {Networks}", dbPath, string.Join(", ", security.AllowedNetworks));

// ---- Pipeline ------------------------------------------------------------------------------------------
if (security.TrustedProxies.Count > 0)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<IpAllowlistMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

if (security.RateLimitPerMinute > 0)
{
    app.UseRateLimiter();
}

app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-cache";
    }

    return next(context);
});

// The Angular build is copied to wwwroot (see docker/api.Dockerfile).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapServerEndpoints();
app.MapPlayerEndpoints();
app.MapStatsEndpoints();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.RunAsync();
