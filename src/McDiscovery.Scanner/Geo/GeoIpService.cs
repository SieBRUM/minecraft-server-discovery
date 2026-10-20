using System.Globalization;
using System.IO.Compression;
using System.Net;
using MaxMind.Db;
using Microsoft.Extensions.Logging;

namespace McDiscovery.Scanner.Geo;

public sealed record GeoInfo
{
    public string? ContinentCode { get; init; }
    public string? CountryCode { get; init; }
    public string? CountryName { get; init; }
    public string? Region { get; init; }
    public string? City { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public long? Asn { get; init; }
    public string? AsOrganization { get; init; }
}

/// <summary>
/// Offline IP geolocation using the free DB-IP Lite databases (CC BY 4.0, https://db-ip.com) in MaxMind DB
/// format. Lookups are local, so no scanned address is ever sent to a third party.
/// </summary>
public sealed class GeoIpService : IDisposable
{
    public const string CityFile = "dbip-city-lite.mmdb";
    public const string AsnFile = "dbip-asn-lite.mmdb";

    private readonly Reader? _city;
    private readonly Reader? _asn;

    private GeoIpService(Reader? city, Reader? asn)
    {
        _city = city;
        _asn = asn;
    }

    public bool Available => _city is not null || _asn is not null;

    /// <summary>Opens whatever databases exist in <paramref name="directory"/>. Missing files just mean no data.</summary>
    public static GeoIpService Open(string directory, ILogger logger)
    {
        return new GeoIpService(TryOpen(Path.Combine(directory, CityFile), logger), TryOpen(Path.Combine(directory, AsnFile), logger));
    }

    private static Reader? TryOpen(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("GeoIP database {Path} not found; location data will be missing", path);
            return null;
        }

        try
        {
            return new Reader(path, FileAccessMode.MemoryMapped);
        }
        catch (Exception e) when (e is InvalidDatabaseException or IOException)
        {
            logger.LogWarning("Could not open GeoIP database {Path}: {Error}", path, e.Message);
            return null;
        }
    }

    public GeoInfo? Lookup(IPAddress address)
    {
        if (!Available)
        {
            return null;
        }

        try
        {
            var city = _city?.Find<Dictionary<string, object>>(address);
            var asn = _asn?.Find<Dictionary<string, object>>(address);
            if (city is null && asn is null)
            {
                return null;
            }

            return FromRecords(city, asn);
        }
        catch (InvalidDatabaseException)
        {
            return null;
        }
    }

    internal static GeoInfo FromRecords(Dictionary<string, object>? city, Dictionary<string, object>? asn) => new()
    {
        ContinentCode = Str(Get(city, "continent", "code")),
        CountryCode = Str(Get(city, "country", "iso_code")),
        CountryName = Str(Get(city, "country", "names", "en")),
        Region = Str(GetFirst(city, "subdivisions", "names", "en")),
        City = Str(Get(city, "city", "names", "en")),
        Latitude = Num(Get(city, "location", "latitude")),
        Longitude = Num(Get(city, "location", "longitude")),
        Asn = Num(Get(asn, "autonomous_system_number")) is { } n ? (long)n : null,
        AsOrganization = Str(Get(asn, "autonomous_system_organization")),
    };

    private static object? Get(Dictionary<string, object>? record, params string[] path)
    {
        object? current = record;
        foreach (var key in path)
        {
            if (current is not IDictionary<string, object> dict || !dict.TryGetValue(key, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static object? GetFirst(Dictionary<string, object>? record, string listKey, params string[] path) =>
        record is not null && record.TryGetValue(listKey, out var list) && list is IList<object> { Count: > 0 } items &&
        items[0] is Dictionary<string, object> first
            ? Get(first, path)
            : null;

    private static string? Str(object? value) => value is string s && s.Length > 0 ? (s.Length > 128 ? s[..128] : s) : null;

    private static double? Num(object? value) => value switch
    {
        null => null,
        string => null,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };

    public void Dispose()
    {
        _city?.Dispose();
        _asn?.Dispose();
    }
}

/// <summary>Downloads the monthly DB-IP Lite databases when they are missing or out of date.</summary>
public static class GeoIpUpdater
{
    private const long MaxDownloadBytes = 512L * 1024 * 1024;

    public static async Task UpdateAsync(string directory, bool force, ILogger logger, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("minecraft-server-discovery (research; +https://github.com/SieBRUM/minecraft-server-discovery)");

        foreach (var (file, kind) in new[] { (GeoIpService.CityFile, "city"), (GeoIpService.AsnFile, "asn") })
        {
            var path = Path.Combine(directory, file);
            if (!force && File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-32))
            {
                continue;
            }

            try
            {
                await DownloadAsync(http, kind, path, logger, ct);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidDatabaseException
                or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("GeoIP {Kind} database download failed: {Error}", kind, e.Message);
            }
        }
    }

    private static async Task DownloadAsync(HttpClient http, string kind, string destination, ILogger logger, CancellationToken ct)
    {
        // The current month's file appears at the start of the month; fall back to last month's.
        var now = DateTime.UtcNow;
        foreach (var month in new[] { now, now.AddMonths(-1) })
        {
            var url = new Uri(string.Create(CultureInfo.InvariantCulture,
                $"https://download.db-ip.com/free/dbip-{kind}-lite-{month:yyyy-MM}.mmdb.gz"));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("GeoIP download {Url}: HTTP {Status}", url, (int)response.StatusCode);
                continue;
            }

            logger.LogInformation("Downloading {Url}", url);
            var temp = destination + ".download";
            try
            {
                await using (var body = await response.Content.ReadAsStreamAsync(ct))
                await using (var gzip = new GZipStream(body, CompressionMode.Decompress))
                await using (var file = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await gzip.ReadAsync(buffer, ct)) > 0)
                    {
                        total += read;
                        if (total > MaxDownloadBytes)
                        {
                            throw new InvalidDataException("GeoIP database is unexpectedly large");
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                // Make sure it is a valid database before replacing the old one.
                using (new Reader(temp, FileAccessMode.Memory))
                {
                }

                File.Move(temp, destination, overwrite: true);
                logger.LogInformation("GeoIP {Kind} database updated ({Month:yyyy-MM})", kind, month);
                return;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        logger.LogWarning("No GeoIP {Kind} database could be downloaded", kind);
    }
}
