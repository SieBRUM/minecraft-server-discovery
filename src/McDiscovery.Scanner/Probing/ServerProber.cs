using System.Net.Sockets;
using McDiscovery.Core.Entities;
using McDiscovery.Protocol.Bedrock;
using McDiscovery.Protocol.Java;
using McDiscovery.Scanner.Discovery;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner.Probing;

public sealed record ProbeOptions
{
    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool LegacyFallback { get; init; } = true;
    public bool Query { get; init; } = true;
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public bool LoginProbe { get; init; } = true;

    /// <summary>Name used by the login probe. 3-16 characters of A-Z, a-z, 0-9 and _.</summary>
    public string LoginName { get; init; } = "McDiscoveryBot";
}

/// <summary>Everything learnt from probing one endpoint.</summary>
public sealed record ProbeResult(ProbeTarget Target, DateTime TimestampUtc)
{
    public bool Online => Java is not null || Bedrock is not null;
    public JavaStatus? Java { get; init; }
    public QueryResult? Query { get; init; }
    public LoginProbeResponse? Login { get; init; }
    public BedrockStatus? Bedrock { get; init; }
}

/// <summary>Talks to one server with every enabled probe.</summary>
public sealed class ServerProber(ProbeOptions options, ILogger<ServerProber> logger)
{
    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken ct)
    {
        var result = target.Edition == Edition.Bedrock
            ? await ProbeBedrockAsync(target, ct)
            : await ProbeJavaAsync(target, ct);
        return result;
    }

    private async Task<ProbeResult> ProbeBedrockAsync(ProbeTarget target, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            var status = await BedrockPing.PingAsync(target.ToEndPoint(), options.StatusTimeout, ct);
            return new ProbeResult(target, now) { Bedrock = status };
        }
        catch (Exception e) when (IsExpected(e, ct))
        {
            logger.LogDebug("{Target}: no Bedrock response ({Error})", target, e.Message);
            return new ProbeResult(target, now);
        }
    }

    private async Task<ProbeResult> ProbeJavaAsync(ProbeTarget target, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var endpoint = target.ToEndPoint();

        JavaStatus? status = null;
        try
        {
            status = await JavaClient.GetStatusAsync(endpoint, options.StatusTimeout, ct);
        }
        catch (Exception e) when (IsExpected(e, ct))
        {
            logger.LogDebug("{Target}: status ping failed ({Error})", target, e.Message);
        }

        if (status is null && options.LegacyFallback)
        {
            try
            {
                status = await JavaClient.GetLegacyStatusAsync(endpoint, options.StatusTimeout, ct);
            }
            catch (Exception e) when (IsExpected(e, ct))
            {
                logger.LogDebug("{Target}: legacy ping failed ({Error})", target, e.Message);
            }
        }

        if (status is null)
        {
            return new ProbeResult(target, now);
        }

        var queryTask = options.Query ? QueryAsync(target, ct) : Task.FromResult<QueryResult?>(null);
        var loginTask = options.LoginProbe && !status.IsLegacy && status.ProtocolVersion is > 0
            ? LoginAsync(target, status.ProtocolVersion.Value, ct)
            : Task.FromResult<LoginProbeResponse?>(null);
        await Task.WhenAll(queryTask, loginTask);

        return new ProbeResult(target, now) { Java = status, Query = await queryTask, Login = await loginTask };
    }

    private async Task<QueryResult?> QueryAsync(ProbeTarget target, CancellationToken ct)
    {
        try
        {
            return await QueryClient.QueryAsync(target.ToEndPoint(), options.QueryTimeout, ct);
        }
        catch (Exception e) when (IsExpected(e, ct))
        {
            return null; // query disabled on the server (the common case)
        }
    }

    private async Task<LoginProbeResponse?> LoginAsync(ProbeTarget target, int protocol, CancellationToken ct)
    {
        try
        {
            return await LoginProbe.ProbeAsync(target.ToEndPoint(), protocol, options.LoginName, options.StatusTimeout, ct);
        }
        catch (Exception e) when (IsExpected(e, ct))
        {
            logger.LogDebug("{Target}: login probe failed ({Error})", target, e.Message);
            return new LoginProbeResponse(LoginOutcome.Failed, e.Message);
        }
    }

    /// <summary>Network and protocol errors are normal while scanning; cancellation of the whole scan is not.</summary>
    private static bool IsExpected(Exception e, CancellationToken ct) =>
        !ct.IsCancellationRequested && e is SocketException or IOException or ProtocolException or OperationCanceledException
            or System.IO.InvalidDataException or EndOfStreamException;
}
