using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using McDiscovery.Core.Entities;
using McDiscovery.Scanner.Targets;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner.Discovery;

/// <summary>
/// Runs masscan (https://github.com/robertdavidgraham/masscan) for stateless SYN scanning and streams the open
/// ports it reports. masscan needs raw socket access: root/CAP_NET_RAW on Linux, Npcap on Windows.
/// </summary>
public sealed partial class MasscanEngine(DiscoveryOptions options, ILogger<MasscanEngine> logger) : IDiscoveryEngine
{
    public string Name => "masscan";

    /// <summary>Finds the masscan binary: the configured path, or <c>masscan</c> on the PATH.</summary>
    public static string? Locate(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        }

        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "masscan.exe" : "masscan";
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in paths.Prepend(AppContext.BaseDirectory))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task RunAsync(TargetSet targets, IReadOnlyList<ushort> ports, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        if (targets.Count == 0 || ports.Count == 0)
        {
            return;
        }

        var path = Locate(options.MasscanPath) ?? throw new InvalidOperationException("masscan was not found");
        progress.AddTotal(targets.Count * ports.Count);

        var workDir = Directory.CreateTempSubdirectory("mcd-masscan-");
        try
        {
            var includeFile = Path.Combine(workDir.FullName, "targets.txt");
            await File.WriteAllLinesAsync(includeFile, targets.Ranges.Select(r => r.ToString()), ct);

            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                // masscan writes paused.conf into its working directory when interrupted.
                WorkingDirectory = workDir.FullName,
            };

            // ArgumentList: every value is passed as a separate argv entry, no shell involved.
            foreach (var arg in BuildArguments(includeFile, ports))
            {
                psi.ArgumentList.Add(arg);
            }

            logger.LogInformation("Starting masscan: {Path} {Args}", path, string.Join(' ', psi.ArgumentList));

            using var process = new Process { StartInfo = psi };
            process.Start();
            process.StandardInput.Close();

            var stderr = PumpStderrAsync(process.StandardError, progress);
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
                {
                    if (TryParseDiscovered(line, out var target))
                    {
                        progress.AddFound();
                        await onFound(target);
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        logger.LogDebug("masscan: {Line}", line);
                    }
                }

                await process.WaitForExitAsync(ct);
            }
            finally
            {
                if (!process.HasExited)
                {
                    logger.LogInformation("Stopping masscan");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }

            var errors = await stderr;
            if (process.ExitCode != 0 && !ct.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"masscan exited with code {process.ExitCode}. Last output: {string.Join(" | ", errors)}. " +
                    "It needs raw socket access (run as root / setcap cap_net_raw,cap_net_admin+eip, or Npcap on Windows).");
            }
        }
        finally
        {
            try
            {
                workDir.Delete(recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    internal List<string> BuildArguments(string includeFile, IReadOnlyList<ushort> ports)
    {
        var args = new List<string>
        {
            "-iL", includeFile,
            "-p", string.Join(',', ports),
            "--rate", options.Rate.ToString(CultureInfo.InvariantCulture),
            "--wait", options.MasscanWaitSeconds.ToString(CultureInfo.InvariantCulture),
            // masscan refuses very large ranges without an exclude; the excludes are already applied.
            "--exclude", "255.255.255.255",
            "--seed", (options.Seed & long.MaxValue).ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(options.MasscanAdapter))
        {
            args.Add("--adapter");
            args.Add(options.MasscanAdapter);
        }

        args.AddRange(options.MasscanExtraArgs);
        return args;
    }

    /// <summary>Parses "Discovered open port 25565/tcp on 1.2.3.4".</summary>
    internal static bool TryParseDiscovered(string line, out ProbeTarget target)
    {
        var match = DiscoveredRegex().Match(line);
        if (match.Success &&
            ushort.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
            IpV4.TryParse(match.Groups[2].ValueSpan, out var address))
        {
            target = new ProbeTarget(address, port, Edition.Java);
            return true;
        }

        target = default;
        return false;
    }

    private async Task<List<string>> PumpStderrAsync(StreamReader reader, DiscoveryProgress progress)
    {
        var tail = new Queue<string>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            // masscan redraws its status line with \r; keep the latest one for progress reports.
            foreach (var part in line.Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.StartsWith("rate:", StringComparison.Ordinal))
                {
                    progress.ExternalStatus = part;
                    continue;
                }

                logger.LogInformation("masscan: {Line}", part);
                tail.Enqueue(part);
                if (tail.Count > 5)
                {
                    tail.Dequeue();
                }
            }
        }

        return [.. tail];
    }

    [GeneratedRegex(@"^Discovered open port (\d{1,5})/tcp on (\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s*$")]
    private static partial Regex DiscoveredRegex();
}
