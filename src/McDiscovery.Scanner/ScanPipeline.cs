using System.Diagnostics;
using System.Threading.Channels;
using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using McDiscovery.Scanner.Discovery;
using McDiscovery.Scanner.Geo;
using McDiscovery.Scanner.Ingest;
using McDiscovery.Scanner.Probing;
using McDiscovery.Scanner.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner;

public sealed record ScanSettings
{
    public required string DatabasePath { get; init; }
    public required string GeoIpDirectory { get; init; }
    public bool GeoIpAutoUpdate { get; init; } = true;

    public DiscoveryOptions Discovery { get; init; } = new();
    public ProbeOptions Probe { get; init; } = new();

    /// <summary>"auto", "masscan" or "connect".</summary>
    public string Engine { get; init; } = "auto";

    public IReadOnlyList<ushort> JavaPorts { get; init; } = [25565];
    public IReadOnlyList<ushort> BedrockPorts { get; init; } = [];

    /// <summary>Servers probed in parallel.</summary>
    public int Concurrency { get; init; } = 512;

    public int BatchSize { get; init; } = 250;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>History snapshots older than this are deleted after each run. 0 keeps everything.</summary>
    public int HistoryDays { get; init; } = 180;
}

public sealed record RescanFilter
{
    public Edition? Edition { get; init; }
    public bool OnlineOnly { get; init; }
    public TimeSpan? SeenWithin { get; init; }
    public int? MaxFailures { get; init; }
}

/// <summary>
/// discovery (masscan / connect / UDP sweep) → probe workers → batched database writer, connected by channels.
/// </summary>
public sealed class ScanPipeline(ScanSettings settings, ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<ScanPipeline>();
    private readonly DbContextOptions<AppDbContext> _dbOptions = Database.CreateOptions(settings.DatabasePath);

    public async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        await using var db = new AppDbContext(_dbOptions);
        await Database.InitializeAsync(db, ct);
    }

    /// <summary>Discovers and probes new servers in <paramref name="targets"/>.</summary>
    public async Task<bool> RunScanAsync(TargetSet targets, string description, CancellationToken ct)
    {
        var engines = new List<string>();
        if (settings.JavaPorts.Count > 0)
        {
            engines.Add(ResolveEngineName());
        }

        if (settings.BedrockPorts.Count > 0)
        {
            engines.Add("bedrock-udp");
        }

        return await RunAsync(ScanKind.Scan, description, string.Join('+', engines), targets.Count,
            (onFound, progress, token) => DiscoverAsync(targets, onFound, progress, token), ct);
    }

    /// <summary>Re-probes servers already in the database, e.g. to refresh player counts.</summary>
    public async Task<bool> RunRescanAsync(RescanFilter filter, CancellationToken ct)
    {
        List<(string Address, int Port, Edition Edition)> known;
        await using (var db = new AppDbContext(_dbOptions))
        {
            var query = db.Servers.AsNoTracking();
            if (filter.Edition is { } edition)
            {
                query = query.Where(s => s.Edition == edition);
            }

            if (filter.OnlineOnly)
            {
                query = query.Where(s => s.IsOnline);
            }

            if (filter.SeenWithin is { } within)
            {
                var since = DateTime.UtcNow - within;
                query = query.Where(s => s.LastSeenUtc >= since);
            }

            if (filter.MaxFailures is { } maxFailures)
            {
                query = query.Where(s => s.ConsecutiveFailures <= maxFailures);
            }

            known = (await query.OrderBy(s => s.LastScannedUtc).Select(s => new { s.Address, s.Port, s.Edition }).ToListAsync(ct))
                .Select(s => (s.Address, s.Port, s.Edition))
                .ToList();
        }

        _logger.LogInformation("Rescanning {Count:N0} known servers", known.Count);
        return await RunAsync(ScanKind.Rescan, "rescan of known servers", "rescan", known.Count, async (onFound, progress, token) =>
        {
            progress.AddTotal(known.Count);
            foreach (var (address, port, edition) in known)
            {
                progress.AddSent();
                if (IpV4.TryParse(address, out var ip) && port is > 0 and <= ushort.MaxValue)
                {
                    await onFound(new ProbeTarget(ip, (ushort)port, edition));
                }
            }
        }, ct);
    }

    private string ResolveEngineName() => settings.Engine switch
    {
        "masscan" => "masscan",
        "connect" => "connect",
        _ => MasscanEngine.Locate(settings.Discovery.MasscanPath) is not null ? "masscan" : "connect",
    };

    private async Task DiscoverAsync(TargetSet targets, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        var tasks = new List<Task>();
        if (settings.JavaPorts.Count > 0)
        {
            tasks.Add(DiscoverJavaAsync(targets, onFound, progress, ct));
        }

        if (settings.BedrockPorts.Count > 0)
        {
            var bedrock = new BedrockSweepEngine(settings.Discovery, loggerFactory.CreateLogger<BedrockSweepEngine>());
            tasks.Add(bedrock.RunAsync(targets, settings.BedrockPorts, onFound, progress, ct));
        }

        await Task.WhenAll(tasks);
    }

    private async Task DiscoverJavaAsync(TargetSet targets, TargetFound onFound, DiscoveryProgress progress, CancellationToken ct)
    {
        var connect = new ConnectScanEngine(settings.Discovery, loggerFactory.CreateLogger<ConnectScanEngine>());
        var name = ResolveEngineName();
        if (name == "connect")
        {
            if (settings.Engine == "auto")
            {
                _logger.LogWarning("masscan not found: using the (much slower) built-in connect scanner. Install masscan for large ranges");
            }

            await connect.RunAsync(targets, settings.JavaPorts, onFound, progress, ct);
            return;
        }

        var masscan = new MasscanEngine(settings.Discovery, loggerFactory.CreateLogger<MasscanEngine>());
        var foundBefore = progress.Found;
        try
        {
            await masscan.RunAsync(targets, settings.JavaPorts, onFound, progress, ct);
        }
        catch (Exception e) when (settings.Engine == "auto" && progress.Found == foundBefore && e is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("masscan failed ({Error}); falling back to the connect scanner", e.Message);
            await connect.RunAsync(targets, settings.JavaPorts, onFound, progress, ct);
        }
    }

    private async Task<bool> RunAsync(ScanKind kind, string description, string engine, long targetCount,
        Func<TargetFound, DiscoveryProgress, CancellationToken, Task> producer, CancellationToken ct)
    {
        if (settings.GeoIpAutoUpdate)
        {
            await GeoIpUpdater.UpdateAsync(settings.GeoIpDirectory, force: false, _logger, ct);
        }

        using var geo = GeoIpService.Open(settings.GeoIpDirectory, _logger);
        var writer = new IngestWriter(_dbOptions, geo);
        var prober = new ServerProber(settings.Probe, loggerFactory.CreateLogger<ServerProber>());

        var run = new ScanRun
        {
            Kind = kind,
            StartedUtc = DateTime.UtcNow,
            Description = description.Length > 500 ? description[..500] : description,
            Engine = engine,
            TargetCount = targetCount,
        };
        await using (var db = new AppDbContext(_dbOptions))
        {
            db.ScanRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var progress = new DiscoveryProgress();
        var queue = Channel.CreateUnbounded<ProbeTarget>();
        var results = Channel.CreateBounded<ProbeResult>(new BoundedChannelOptions(settings.BatchSize * 4) { SingleReader = true });
        var seen = new HashSet<ProbeTarget>();
        long probed = 0, online = 0, created = 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        ValueTask OnFound(ProbeTarget target)
        {
            lock (seen)
            {
                if (!seen.Add(target))
                {
                    return ValueTask.CompletedTask; // masscan can report the same port more than once
                }
            }

            return queue.Writer.WriteAsync(target, cts.Token);
        }

        Exception? producerError = null;
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await producer(OnFound, progress, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                producerError = e;
                _logger.LogError(e, "Discovery failed");
            }
            finally
            {
                queue.Writer.TryComplete();
            }
        }, CancellationToken.None);

        var workers = Enumerable.Range(0, settings.Concurrency).Select(_ => Task.Run(async () =>
        {
            try
            {
                await foreach (var target in queue.Reader.ReadAllAsync(cts.Token))
                {
                    var result = await prober.ProbeAsync(target, cts.Token);
                    Interlocked.Increment(ref probed);
                    await results.Writer.WriteAsync(result, cts.Token);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
        }, CancellationToken.None)).ToArray();

        var workersDone = Task.WhenAll(workers).ContinueWith(_ => results.Writer.TryComplete(), TaskScheduler.Default);

        var writerTask = Task.Run(async () =>
        {
            await foreach (var batch in ReadBatchesAsync(results.Reader))
            {
                var stats = await WriteWithRetryAsync(writer, batch);
                Interlocked.Add(ref online, stats.Online);
                Interlocked.Add(ref created, stats.New);
            }
        }, CancellationToken.None);

        var sw = Stopwatch.StartNew();
        using var reporterCts = new CancellationTokenSource();
        var reporter = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(settings.ProgressInterval, reporterCts.Token);
                    LogProgress();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);

        void LogProgress()
        {
            var total = progress.Total;
            var percent = total > 0 ? 100.0 * progress.Sent / total : 0;
            _logger.LogInformation(
                "[{Elapsed:hh\\:mm\\:ss}] sent {Sent:N0}/{Total:N0} ({Percent:F1}%) | open {Found:N0} | probed {Probed:N0} | online {Online:N0} | new {New:N0} | queued {Queued:N0}{Extra}",
                sw.Elapsed, progress.Sent, total, percent, progress.Found, Interlocked.Read(ref probed), Interlocked.Read(ref online),
                Interlocked.Read(ref created), queue.Reader.Count, progress.ExternalStatus is { } s ? " | masscan " + s : "");
        }

        await producerTask;
        await workersDone;
        await writerTask;
        await reporterCts.CancelAsync();
        await reporter;
        LogProgress();

        var completed = !ct.IsCancellationRequested && producerError is null;
        run.FinishedUtc = DateTime.UtcNow;
        run.OpenPorts = progress.Found;
        run.ServersOnline = online;
        run.NewServers = created;
        run.Completed = completed;
        await using (var db = new AppDbContext(_dbOptions))
        {
            db.ScanRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
            if (settings.HistoryDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-settings.HistoryDays);
                var deleted = await db.Snapshots.Where(s => s.TimestampUtc < cutoff).ExecuteDeleteAsync(CancellationToken.None);
                if (deleted > 0)
                {
                    _logger.LogInformation("Deleted {Count:N0} history snapshots older than {Days} days", deleted, settings.HistoryDays);
                }
            }
        }

        _logger.LogInformation("{Kind} {State} in {Elapsed}: {Online:N0} servers online, {New:N0} new",
            kind, completed ? "finished" : "stopped", sw.Elapsed, online, created);
        return completed;
    }

    private async Task<IngestStats> WriteWithRetryAsync(IngestWriter writer, List<ProbeResult> batch)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Never cancelled: results that were already collected are always written.
                return await writer.WriteAsync(batch, CancellationToken.None);
            }
            catch (Exception e) when (attempt < 3 && e is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
            {
                _logger.LogWarning("Database write failed (attempt {Attempt}): {Error}", attempt, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
            catch (Exception e) when (e is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
            {
                _logger.LogError(e, "Dropping a batch of {Count} results after repeated database errors", batch.Count);
                return new IngestStats(0, 0, 0);
            }
        }
    }

    /// <summary>Groups results into batches of up to BatchSize, flushing at least every FlushInterval.</summary>
    private async IAsyncEnumerable<List<ProbeResult>> ReadBatchesAsync(ChannelReader<ProbeResult> reader)
    {
        var buffer = new List<ProbeResult>(settings.BatchSize);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (buffer.Count == 0)
            {
                if (!await reader.WaitToReadAsync())
                {
                    yield break;
                }

                sw.Restart();
            }

            while (buffer.Count < settings.BatchSize && reader.TryRead(out var item))
            {
                buffer.Add(item);
            }

            var finished = false;
            if (buffer.Count < settings.BatchSize && sw.Elapsed < settings.FlushInterval)
            {
                using var wait = new CancellationTokenSource(settings.FlushInterval - sw.Elapsed);
                try
                {
                    finished = !await reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException)
                {
                    // flush interval reached
                }

                if (!finished)
                {
                    continue;
                }
            }

            yield return buffer;
            buffer = new List<ProbeResult>(settings.BatchSize);
            sw.Restart();
            if (finished)
            {
                yield break;
            }
        }
    }
}
