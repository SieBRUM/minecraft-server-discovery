using System.Net;
using System.Net.Sockets;

namespace McDiscovery.Api.Security;

public sealed class SecurityOptions
{
    /// <summary>
    /// Client networks allowed to use the site and API. Default: loopback only. Overridden by the
    /// comma separated <c>MCD_ALLOWED_NETWORKS</c> environment variable. Use <c>0.0.0.0/0,::/0</c> to allow everyone.
    /// </summary>
    public List<string> AllowedNetworks { get; set; } = [];

    /// <summary>
    /// Reverse proxies whose <c>X-Forwarded-For</c> header is trusted (env <c>MCD_TRUSTED_PROXIES</c>).
    /// Empty: the header is ignored and the TCP peer address is used.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>Requests per minute per client IP (env <c>MCD_RATE_LIMIT_PER_MINUTE</c>). 0 disables rate limiting.</summary>
    public int RateLimitPerMinute { get; set; } = 600;

    /// <summary>Applies the environment variable overrides.</summary>
    public void ApplyEnvironment(Func<string, string?> getVariable)
    {
        if (Split(getVariable("MCD_ALLOWED_NETWORKS")) is { } allowed)
        {
            AllowedNetworks = allowed;
        }

        if (Split(getVariable("MCD_TRUSTED_PROXIES")) is { } proxies)
        {
            TrustedProxies = proxies;
        }

        if (int.TryParse(getVariable("MCD_RATE_LIMIT_PER_MINUTE"), out var limit))
        {
            RateLimitPerMinute = limit;
        }
    }

    private static List<string>? Split(string? value) =>
        value is null ? null : [.. value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Parses "1.2.3.0/24", "::1/128" or a single address. Throws <see cref="FormatException"/>.</summary>
    public static IPNetwork ParseNetwork(string value)
    {
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        var address = IPAddress.Parse(slash >= 0 ? value[..slash] : value);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxPrefix;
        if (slash >= 0 && (!int.TryParse(value.AsSpan(slash + 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix > maxPrefix))
        {
            throw new FormatException($"Invalid network '{value}'");
        }

        // Accept "192.168.1.5/24" by clearing the host bits (IPNetwork requires a clean base address).
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new IPNetwork(new IPAddress(bytes), prefix);
    }
}

/// <summary>Answers "is this client allowed?" for the configured networks.</summary>
public sealed class IpAllowlist
{
    private readonly IPNetwork[] _networks;

    public IpAllowlist(IEnumerable<string> networks)
    {
        _networks = [.. networks.Select(SecurityOptions.ParseNetwork)];
    }

    public bool IsAllowed(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        // Dual-stack sockets report IPv4 clients as ::ffff:a.b.c.d.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (var network in _networks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Rejects clients outside the allowlist with 403 before anything else runs.</summary>
public sealed class IpAllowlistMiddleware(RequestDelegate next, IpAllowlist allowlist, ILogger<IpAllowlistMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (allowlist.IsAllowed(context.Connection.RemoteIpAddress))
        {
            return next(context);
        }

        logger.LogInformation("Blocked request from {Address} (not in the allowlist)", context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("Forbidden");
    }
}

/// <summary>Strict security headers for every response (the CSP only allows our own scripts) and GET/HEAD only.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        // Angular injects component styles at runtime.
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://tile.openstreetmap.org; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting: also applies to error responses (the exception handler clears headers set earlier).
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpResponse)state).Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            return Task.CompletedTask;
        }, context.Response);

        // Everything is read-only: refuse other methods before any endpoint or static file handling.
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";
            return Task.CompletedTask;
        }

        return next(context);
    }
}
