using System.CommandLine;
using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using McDiscovery.Scanner;
using McDiscovery.Scanner.Cli;
using McDiscovery.Scanner.Discovery;
using McDiscovery.Scanner.Geo;
using McDiscovery.Scanner.Probing;
using McDiscovery.Scanner.Targets;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------------------------------------
// Shared options (valid for every command)
// ---------------------------------------------------------------------------------------------------------
var dbOption = new Option<string?>("--db") { Description = $"SQLite database file [env {Database.PathVariable}, default {Database.DefaultPath}]", Recursive = true };
var geoDirOption = new Option<string?>("--geoip-dir") { Description = "Directory with the DB-IP Lite .mmdb files [env MCD_GEOIP_DIR, default: 'geoip' next to the database]", Recursive = true };
var verboseOption = new Option<bool>("--verbose", "-v") { Description = "Debug logging", Recursive = true };

// ---------------------------------------------------------------------------------------------------------
// Options for scan / rescan
// ---------------------------------------------------------------------------------------------------------
var geoUpdateOption = new Option<bool>("--geoip-update") { Description = "Download the monthly GeoIP databases when missing or older than a month", DefaultValueFactory = _ => true };
var concurrencyOption = new Option<int>("--concurrency") { Description = "Servers probed in parallel", DefaultValueFactory = _ => 512 };
var timeoutOption = new Option<TimeSpan>("--timeout") { Description = "Timeout per status ping / login probe", DefaultValueFactory = _ => TimeSpan.FromSeconds(5), CustomParser = CliParsing.ParseDuration };
var queryOption = new Option<bool>("--query") { Description = "Try the UDP query protocol (plugins, map, full player list)", DefaultValueFactory = _ => true };
var loginOption = new Option<bool>("--login-probe") { Description = "Detect online/offline mode and whitelists with a login attempt (visible in server logs)", DefaultValueFactory = _ => true };
var loginNameOption = new Option<string>("--login-name") { Description = "Username used by the login probe", DefaultValueFactory = _ => "McDiscoveryBot" };
var legacyOption = new Option<bool>("--legacy-ping") { Description = "Fall back to the pre-1.7 legacy ping", DefaultValueFactory = _ => true };
var historyOption = new Option<int>("--history-days") { Description = "Delete history snapshots older than this (0 = keep forever)", DefaultValueFactory = _ => 180 };
var loopOption = new Option<bool>("--loop") { Description = "Run again after finishing (until stopped)" };
var intervalOption = new Option<TimeSpan>("--interval") { Description = "Pause between runs with --loop", DefaultValueFactory = _ => TimeSpan.FromHours(24), CustomParser = CliParsing.ParseDuration };

// scan
var targetsOption = new Option<string[]>("--targets", "-t") { Description = "IPs, CIDRs or ranges (1.2.3.4, 1.2.3.0/24, 1.2.3.4-1.2.3.9), comma separated or repeated", AllowMultipleArgumentsPerToken = true };
var targetsFileOption = new Option<string[]>("--targets-file", "-T") { Description = "File(s) with targets, one per line, # for comments" };
var excludeOption = new Option<string[]>("--exclude") { Description = "Ranges to never scan", AllowMultipleArgumentsPerToken = true };
var excludeFileOption = new Option<string[]>("--exclude-file") { Description = "File(s) with ranges to never scan" };
var portsOption = new Option<List<ushort>>("--ports", "-p") { Description = "Java Edition TCP ports ('none' to skip Java)", DefaultValueFactory = _ => [25565], CustomParser = CliParsing.ParsePorts };
var bedrockOption = new Option<bool>("--bedrock") { Description = "Also sweep for Bedrock Edition servers over UDP" };
var bedrockPortsOption = new Option<List<ushort>>("--bedrock-ports") { Description = "Bedrock UDP ports", DefaultValueFactory = _ => [19132], CustomParser = CliParsing.ParsePorts };
var engineOption = new Option<string>("--engine") { Description = "TCP discovery engine", DefaultValueFactory = _ => "auto" };
engineOption.AcceptOnlyFromAmong("auto", "masscan", "connect");
var rateOption = new Option<int>("--rate", "-r") { Description = "Packets per second (masscan, connect scan and UDP sweep each)", DefaultValueFactory = _ => 10_000 };
var connectTimeoutOption = new Option<TimeSpan>("--connect-timeout") { Description = "Connect scanner timeout", DefaultValueFactory = _ => TimeSpan.FromMilliseconds(1500), CustomParser = CliParsing.ParseDuration };
var connectConcurrencyOption = new Option<int>("--connect-concurrency") { Description = "Connect scanner parallel connects", DefaultValueFactory = _ => 2048 };
var masscanPathOption = new Option<string?>("--masscan-path") { Description = "Path to masscan (default: search PATH)" };
var masscanAdapterOption = new Option<string?>("--masscan-adapter") { Description = "Network adapter for masscan (--adapter)" };
var masscanWaitOption = new Option<int>("--masscan-wait") { Description = "Seconds masscan waits for late replies", DefaultValueFactory = _ => 10 };
var masscanArgOption = new Option<string[]>("--masscan-arg") { Description = "Extra argument passed to masscan as-is (repeatable), e.g. --masscan-arg=--router-mac --masscan-arg=00-11-22-33-44-55" };
var udpWaitOption = new Option<TimeSpan>("--udp-wait") { Description = "How long the Bedrock sweep listens after the last ping", DefaultValueFactory = _ => TimeSpan.FromSeconds(5), CustomParser = CliParsing.ParseDuration };
var dryRunOption = new Option<bool>("--dry-run") { Description = "Print what would be scanned and exit" };

// rescan
var editionOption = new Option<string>("--edition") { Description = "Which servers to rescan", DefaultValueFactory = _ => "all" };
editionOption.AcceptOnlyFromAmong("all", "java", "bedrock");
var onlineOnlyOption = new Option<bool>("--online-only") { Description = "Only servers that were online at the last scan" };
var seenWithinOption = new Option<TimeSpan?>("--seen-within") { Description = "Only servers seen online within this time (e.g. 7d)", CustomParser = r => CliParsing.ParseDuration(r) };
var maxFailuresOption = new Option<int?>("--max-failures") { Description = "Skip servers that failed this many probes in a row" };

// geoip
var forceOption = new Option<bool>("--force") { Description = "Download even when the files are recent" };

Option[] probeOptions = [geoUpdateOption, concurrencyOption, timeoutOption, queryOption, loginOption, loginNameOption, legacyOption, historyOption, loopOption, intervalOption];

var scanCommand = new Command("scan", "Discover servers in IP ranges and store what they report");
foreach (var o in probeOptions.Concat<Option>([targetsOption, targetsFileOption, excludeOption, excludeFileOption, portsOption, bedrockOption,
             bedrockPortsOption, engineOption, rateOption, connectTimeoutOption, connectConcurrencyOption, masscanPathOption,
             masscanAdapterOption, masscanWaitOption, masscanArgOption, udpWaitOption, dryRunOption]))
{
    scanCommand.Options.Add(o);
}

var rescanCommand = new Command("rescan", "Re-probe servers already in the database (no port scan)");
foreach (var o in probeOptions.Concat<Option>([editionOption, onlineOnlyOption, seenWithinOption, maxFailuresOption]))
{
    rescanCommand.Options.Add(o);
}

var geoCommand = new Command("geoip-update", "Download the DB-IP Lite GeoIP databases") { forceOption };

var backupOutOption = new Option<string?>("--out") { Description = "Backup file to create [default: backups/mcdiscovery-<timestamp>.db next to the database]" };
var backupCommand = new Command("backup", "Make a consistent copy of the database while the API and scanner keep running") { backupOutOption };

var root = new RootCommand("Minecraft server discovery scanner (research tool). See README.md.")
{
    dbOption, geoDirOption, verboseOption, scanCommand, rescanCommand, geoCommand, backupCommand,
};

// ---------------------------------------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------------------------------------
scanCommand.SetAction(async (parse, ct) =>
{
    using var loggerFactory = CreateLoggerFactory(parse.GetValue(verboseOption));
    var log = loggerFactory.CreateLogger("scanner");
    if (!Validate(parse, log, scan: true))
    {
        return 2;
    }

    TargetSet targets;
    try
    {
        var include = new List<IpRange>();
        foreach (var spec in parse.GetValue(targetsOption) ?? [])
        {
            include.AddRange(TargetParser.ParseText(spec, "--targets"));
        }

        foreach (var file in parse.GetValue(targetsFileOption) ?? [])
        {
            include.AddRange(TargetParser.ParseFile(file));
        }

        var exclude = new List<IpRange>();
        foreach (var spec in parse.GetValue(excludeOption) ?? [])
        {
            exclude.AddRange(TargetParser.ParseText(spec, "--exclude"));
        }

        foreach (var file in parse.GetValue(excludeFileOption) ?? [])
        {
            exclude.AddRange(TargetParser.ParseFile(file));
        }

        targets = TargetSet.Create(include, exclude);
    }
    catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
    {
        log.LogError("{Error}", e.Message);
        return 2;
    }

    var javaPorts = parse.GetValue(portsOption) ?? [];
    var bedrockPorts = parse.GetValue(bedrockOption) ? parse.GetValue(bedrockPortsOption) ?? [] : [];
    var settings = BuildSettings(parse) with
    {
        Engine = parse.GetValue(engineOption)!,
        JavaPorts = javaPorts,
        BedrockPorts = bedrockPorts,
        Discovery = new DiscoveryOptions
        {
            Rate = parse.GetValue(rateOption),
            ConnectTimeout = parse.GetValue(connectTimeoutOption),
            ConnectConcurrency = parse.GetValue(connectConcurrencyOption),
            MasscanPath = parse.GetValue(masscanPathOption),
            MasscanAdapter = parse.GetValue(masscanAdapterOption),
            MasscanWaitSeconds = parse.GetValue(masscanWaitOption),
            MasscanExtraArgs = parse.GetValue(masscanArgOption) ?? [],
            UdpWait = parse.GetValue(udpWaitOption),
        },
    };

    var description = DescribeTargets(targets);
    log.LogInformation("Targets: {Count:N0} addresses in {Ranges:N0} ranges ({Description}); Java ports: {Java}; Bedrock ports: {Bedrock}",
        targets.Count, targets.Ranges.Count, description, Show(javaPorts), Show(bedrockPorts));

    if (targets.Count == 0 || (javaPorts.Count == 0 && bedrockPorts.Count == 0))
    {
        log.LogWarning("Nothing to scan. Add targets with --targets or --targets-file (in Docker: edit config/targets.txt)");
        return 0;
    }

    if (parse.GetValue(dryRunOption))
    {
        var masscan = MasscanEngine.Locate(settings.Discovery.MasscanPath);
        var packets = targets.Count * (javaPorts.Count + bedrockPorts.Count);
        log.LogInformation("Dry run. Engine: {Engine} (masscan {Masscan}). About {Packets:N0} packets, roughly {Duration} at {Rate:N0}/s",
            settings.Engine, masscan ?? "not found", packets, TimeSpan.FromSeconds((double)packets / settings.Discovery.Rate), settings.Discovery.Rate);
        return 0;
    }

    var pipeline = new ScanPipeline(settings, loggerFactory);
    return await RunLoopAsync(parse, pipeline, log, token => pipeline.RunScanAsync(targets, description, token), ct);
});

rescanCommand.SetAction(async (parse, ct) =>
{
    using var loggerFactory = CreateLoggerFactory(parse.GetValue(verboseOption));
    var log = loggerFactory.CreateLogger("scanner");
    if (!Validate(parse, log, scan: false))
    {
        return 2;
    }
    if (!Validate(parse, log, scan: true))
    {
        return 2;
    }
    var filter = new RescanFilter
    {
        Edition = parse.GetValue(editionOption) switch
        {
            "java" => Edition.Java,
            "bedrock" => Edition.Bedrock,
            _ => null,
        },
        OnlineOnly = parse.GetValue(onlineOnlyOption),
        SeenWithin = parse.GetValue(seenWithinOption),
        MaxFailures = parse.GetValue(maxFailuresOption),
    };

    var pipeline = new ScanPipeline(BuildSettings(parse), loggerFactory);
    return await RunLoopAsync(parse, pipeline, log, token => pipeline.RunRescanAsync(filter, token), ct);
});

geoCommand.SetAction(async (parse, ct) =>
{
    using var loggerFactory = CreateLoggerFactory(parse.GetValue(verboseOption));
    var log = loggerFactory.CreateLogger("geoip");
    var dir = ResolveGeoDir(parse);
    await GeoIpUpdater.UpdateAsync(dir, parse.GetValue(forceOption), log, ct);
    using var geo = GeoIpService.Open(dir, log);
    return geo.Available ? 0 : 1;
});

backupCommand.SetAction(async (parse, ct) =>
{
    using var loggerFactory = CreateLoggerFactory(parse.GetValue(verboseOption));
    var log = loggerFactory.CreateLogger("backup");
    var source = Database.ResolvePath(parse.GetValue(dbOption));
    if (!File.Exists(source))
    {
        log.LogError("Database {Path} does not exist", source);
        return 1;
    }

    var target = parse.GetValue(backupOutOption) is { Length: > 0 } outPath
        ? Path.GetFullPath(outPath)
        : Path.Combine(Path.GetDirectoryName(source) ?? ".", "backups", $"mcdiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
    if (File.Exists(target))
    {
        log.LogError("{Path} already exists", target);
        return 1;
    }

    await Database.BackupAsync(source, target, ct);
    log.LogInformation("Backup written to {Path} ({Size:N0} bytes)", target, new FileInfo(target).Length);
    return 0;
});

return await root.Parse(args).InvokeAsync();

// ---------------------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------------------
bool Validate(ParseResult parse, ILogger log, bool scan)
{
    var errors = new List<string>();
    if (parse.GetValue(concurrencyOption) is < 1 or > 16384)
    {
        errors.Add("--concurrency must be between 1 and 16384");
    }

    if (!CliParsing.UsernameRegex().IsMatch(parse.GetValue(loginNameOption) ?? ""))
    {
        errors.Add("--login-name must be 3-16 characters of A-Z, a-z, 0-9 or _");
    }

    if (parse.GetValue(historyOption) < 0)
    {
        errors.Add("--history-days can't be negative");
    }

    if (parse.GetValue(timeoutOption) is var timeout && (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromMinutes(5)))
    {
        errors.Add("--timeout must be between 100ms and 5m");
    }

    if (scan)
    {
        if (parse.GetValue(rateOption) is < 1 or > 100_000_000)
        {
            errors.Add("--rate must be between 1 and 100000000");
        }

        if (parse.GetValue(connectConcurrencyOption) is < 1 or > 65535)
        {
            errors.Add("--connect-concurrency must be between 1 and 65535");
        }

        if (parse.GetValue(masscanWaitOption) is < 0 or > 3600)
        {
            errors.Add("--masscan-wait must be between 0 and 3600");
        }

        if (parse.GetValue(connectTimeoutOption) is var ct && (ct < TimeSpan.FromMilliseconds(50) || ct > TimeSpan.FromMinutes(1)))
        {
            errors.Add("--connect-timeout must be between 50ms and 1m");
        }
    }

    foreach (var error in errors)
    {
        log.LogError("{Error}", error);
    }

    return errors.Count == 0;
}

ScanSettings BuildSettings(ParseResult parse) => new()
{
    DatabasePath = Database.ResolvePath(parse.GetValue(dbOption)),
    GeoIpDirectory = ResolveGeoDir(parse),
    GeoIpAutoUpdate = parse.GetValue(geoUpdateOption),
    Concurrency = parse.GetValue(concurrencyOption),
    HistoryDays = parse.GetValue(historyOption),
    Probe = new ProbeOptions
    {
        StatusTimeout = parse.GetValue(timeoutOption),
        Query = parse.GetValue(queryOption),
        QueryTimeout = TimeSpan.FromTicks(Math.Min(parse.GetValue(timeoutOption).Ticks, TimeSpan.FromSeconds(2).Ticks)),
        LoginProbe = parse.GetValue(loginOption),
        LoginName = parse.GetValue(loginNameOption)!,
        LegacyFallback = parse.GetValue(legacyOption),
    },
};

string ResolveGeoDir(ParseResult parse)
{
    var configured = parse.GetValue(geoDirOption) ?? Environment.GetEnvironmentVariable("MCD_GEOIP_DIR");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    var dbDir = Path.GetDirectoryName(Database.ResolvePath(parse.GetValue(dbOption))) ?? ".";
    return Path.Combine(dbDir, "geoip");
}

async Task<int> RunLoopAsync(ParseResult parse, ScanPipeline pipeline, ILogger log, Func<CancellationToken, Task<bool>> run, CancellationToken ct)
{
    try
    {
        await pipeline.InitializeDatabaseAsync(ct);
        var loop = parse.GetValue(loopOption);
        var interval = parse.GetValue(intervalOption);
        while (true)
        {
            var ok = await run(ct);
            if (!loop)
            {
                return ok ? 0 : 1;
            }

            log.LogInformation("Next run at {Time:u}", DateTime.UtcNow + interval);
            await Task.Delay(interval, ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        log.LogInformation("Stopped");
        return 130;
    }
}

static ILoggerFactory CreateLoggerFactory(bool verbose) => LoggerFactory.Create(b => b
    .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information)
    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        o.UseUtcTimestamp = true;
    }));

static string Show(IReadOnlyCollection<ushort> ports) => ports.Count == 0 ? "none" : string.Join(',', ports.Take(20)) + (ports.Count > 20 ? ",..." : "");

static string DescribeTargets(TargetSet targets)
{
    var text = string.Join(", ", targets.Ranges.Take(3));
    return targets.Ranges.Count > 3 ? $"{text}, ... (+{targets.Ranges.Count - 3} more)" : text;
}
