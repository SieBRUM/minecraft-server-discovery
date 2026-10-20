using System.Net;
using System.Net.Sockets;
using McDiscovery.Core.Entities;
using McDiscovery.Scanner.Targets;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner.Discovery;

/// <summary>
/// Portable fallback when masscan isn't available: plain TCP connects, many in parallel, in a randomised order.
/// Needs no privileges and works everywhere, but is much slower than masscan for large ranges.
/// </summary>
public sealed class ConnectScanEngine(DiscoveryOptions options, ILogger<ConnectScanEngine> logger) : IDiscoveryEngine
{
    public string Name => "connect";

    public async Task RunAsync(TargetSet targets, IReadOnlyList<ushort> ports, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        progress.AddTotal(targets.Count * ports.Count);
        logger.LogInformation("Connect scan of {Count:N0} endpoints at up to {Rate:N0}/s with {Concurrency} parallel connects",
            targets.Count * ports.Count, options.Rate, options.ConnectConcurrency);

        var limiter = new RateLimiter(options.Rate);
        using var slots = new SemaphoreSlim(options.ConnectConcurrency);
        var running = new List<Task>();

        foreach (var (address, port) in TargetOrder.Enumerate(targets, ports, options.Seed))
        {
            await limiter.WaitAsync(ct);
            await slots.WaitAsync(ct);
            progress.AddSent();
            var task = ProbeAsync(new ProbeTarget(address, port, Edition.Java), onFound, progress, slots, ct);
            running.Add(task);
            if (running.Count >= options.ConnectConcurrency * 2)
            {
                running.RemoveAll(t => t.IsCompleted);
            }
        }

        await Task.WhenAll(running);
    }

    private async Task ProbeAsync(ProbeTarget target, TargetFound onFound, DiscoveryProgress progress, SemaphoreSlim slots, CancellationToken ct)
    {
        var open = false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(options.ConnectTimeout);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.LingerState = new LingerOption(true, 0); // reset instead of TIME_WAIT: we reconnect later anyway
            await socket.ConnectAsync(new IPEndPoint(IpV4.ToAddress(target.Address), target.Port), cts.Token);
            open = true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            // closed, filtered or timed out
        }
        finally
        {
            slots.Release();
        }

        if (open && !ct.IsCancellationRequested)
        {
            progress.AddFound();
            await onFound(target);
        }
    }
}
