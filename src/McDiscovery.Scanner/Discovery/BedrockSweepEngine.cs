using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using McDiscovery.Core.Entities;
using McDiscovery.Protocol.Bedrock;
using McDiscovery.Scanner.Targets;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner.Discovery;

/// <summary>
/// Stateless UDP sweep for Bedrock servers: one socket fires RakNet Unconnected Pings at every target while a
/// receive loop collects the pongs. Like masscan it keeps no per-target state, so it scales to large ranges, and
/// it needs no special privileges because it uses ordinary UDP sockets.
/// </summary>
public sealed class BedrockSweepEngine(DiscoveryOptions options, ILogger<BedrockSweepEngine> logger) : IDiscoveryEngine
{
    public string Name => "bedrock-udp";

    public async Task RunAsync(TargetSet targets, IReadOnlyList<ushort> ports, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        var total = targets.Count * ports.Count;
        if (total == 0)
        {
            return;
        }

        progress.AddTotal(total);
        logger.LogInformation("Bedrock UDP sweep of {Count:N0} endpoints at {Rate:N0} packets/s", total, options.Rate);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveBufferSize = 8 * 1024 * 1024;
        socket.SendBufferSize = 4 * 1024 * 1024;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Stop ICMP "port unreachable" replies from surfacing as errors on the receive side (SIO_UDP_CONNRESET).
            socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        }

        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        var guid = Random.Shared.NextInt64();
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receiver = ReceiveLoopAsync(socket, targets, ports, onFound, progress, receiveCts.Token);

        try
        {
            var limiter = new RateLimiter(options.Rate);
            var packet = BedrockPing.BuildPing(Environment.TickCount64, guid);
            foreach (var (address, port) in TargetOrder.Enumerate(targets, ports, options.Seed))
            {
                await limiter.WaitAsync(ct);
                await SendAsync(socket, packet, new IPEndPoint(IpV4.ToAddress(address), port), ct);
                progress.AddSent();
            }

            await Task.Delay(options.UdpWait, ct);
        }
        finally
        {
            await receiveCts.CancelAsync();
            await receiver;
        }
    }

    private static async ValueTask SendAsync(Socket socket, byte[] packet, IPEndPoint endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await socket.SendToAsync(packet, SocketFlags.None, endpoint, ct);
                return;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
            {
                await Task.Delay(5, ct); // kernel queue full: back off briefly
            }
            catch (SocketException)
            {
                return; // unreachable network, firewall rejection ...: skip this target
            }
        }
    }

    private async Task ReceiveLoopAsync(Socket socket, TargetSet targets, IReadOnlyList<ushort> ports,
        TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        var buffer = new byte[2048];
        var seen = new HashSet<(uint, ushort)>();
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException e)
            {
                logger.LogDebug("UDP receive error: {Error}", e.SocketErrorCode);
                continue;
            }

            if (result.RemoteEndPoint is not IPEndPoint remote || BedrockPing.TryParsePong(buffer.AsSpan(0, result.ReceivedBytes)) is null)
            {
                continue;
            }

            uint address;
            try
            {
                address = IpV4.ToUInt(remote.Address);
            }
            catch (FormatException)
            {
                continue;
            }

            // Only accept answers from endpoints we actually pinged.
            var port = (ushort)remote.Port;
            if (!targets.Contains(address) || !ports.Contains(port) || !seen.Add((address, port)))
            {
                continue;
            }

            progress.AddFound();
            try
            {
                await onFound(new ProbeTarget(address, port, Edition.Bedrock));
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
