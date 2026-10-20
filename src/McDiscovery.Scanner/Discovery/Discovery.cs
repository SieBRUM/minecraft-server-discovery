using System.Net;
using McDiscovery.Core.Entities;
using McDiscovery.Scanner.Targets;

namespace McDiscovery.Scanner.Discovery;

/// <summary>An endpoint to probe. IPv4 only (the scanner does not do IPv6).</summary>
public readonly record struct ProbeTarget(uint Address, ushort Port, Edition Edition)
{
    public IPEndPoint ToEndPoint() => new(IpV4.ToAddress(Address), Port);

    public override string ToString() => $"{IpV4.Format(Address)}:{Port} ({Edition})";
}

public sealed record DiscoveryOptions
{
    /// <summary>Packets (or connection attempts) per second.</summary>
    public int Rate { get; init; } = 10_000;

    public ulong Seed { get; init; } = (ulong)Random.Shared.NextInt64();

    // Connect scanner
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);
    public int ConnectConcurrency { get; init; } = 2048;

    // masscan
    public string? MasscanPath { get; init; }
    public string? MasscanAdapter { get; init; }
    public int MasscanWaitSeconds { get; init; } = 10;
    public IReadOnlyList<string> MasscanExtraArgs { get; init; } = [];

    // Bedrock UDP sweep: how long to keep listening after the last ping was sent.
    public TimeSpan UdpWait { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Live counters shared between the discovery engines and the progress reporter.</summary>
public sealed class DiscoveryProgress
{
    private long _sent;
    private long _total;
    private long _found;

    public long Sent => Interlocked.Read(ref _sent);
    public long Total => Interlocked.Read(ref _total);
    public long Found => Interlocked.Read(ref _found);

    /// <summary>Last status line reported by masscan, if masscan is in use.</summary>
    public string? ExternalStatus { get; set; }

    public void AddTotal(long count) => Interlocked.Add(ref _total, count);
    public void AddSent(long count = 1) => Interlocked.Add(ref _sent, count);
    public void AddFound() => Interlocked.Increment(ref _found);
}

public delegate ValueTask TargetFound(ProbeTarget target);

/// <summary>Finds open ports. Implementations: masscan, TCP connect scan, Bedrock UDP sweep.</summary>
public interface IDiscoveryEngine
{
    string Name { get; }

    Task RunAsync(TargetSet targets, IReadOnlyList<ushort> ports, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct);
}

/// <summary>Iterates (address, port) pairs of a target set in a pseudo-random order.</summary>
internal static class TargetOrder
{
    public static IEnumerable<(uint Address, ushort Port)> Enumerate(TargetSet targets, IReadOnlyList<ushort> ports, ulong seed)
    {
        if (targets.Count == 0 || ports.Count == 0)
        {
            yield break;
        }

        var size = targets.Count * ports.Count;
        var permutation = new Permutation(size, seed);
        for (long i = 0; i < size; i++)
        {
            var index = permutation.Map(i);
            yield return (targets[index / ports.Count], ports[(int)(index % ports.Count)]);
        }
    }
}
