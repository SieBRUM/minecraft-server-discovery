using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace McDiscovery.Core.Data;

/// <summary>Shared SQLite setup for the scanner and the API.</summary>
public static class Database
{
    /// <summary>Environment variable / configuration key holding the SQLite file path.</summary>
    public const string PathVariable = "MCD_DB_PATH";

    public const string DefaultPath = "data/mcdiscovery.db";

    /// <summary>Resolves the database path: explicit value, then <c>MCD_DB_PATH</c>, then the default.</summary>
    public static string ResolvePath(string? configured = null)
    {
        var path = !string.IsNullOrWhiteSpace(configured) ? configured : Environment.GetEnvironmentVariable(PathVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultPath : path);
    }

    public static string BuildConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true,
        // Seconds to wait (retrying) when the other process holds the write lock.
        DefaultTimeout = 60,
    }.ToString();

    public static DbContextOptionsBuilder UseAppSqlite(this DbContextOptionsBuilder builder, string path) =>
        builder.UseSqlite(BuildConnectionString(path), o => o.CommandTimeout(60));

    public static DbContextOptions<AppDbContext> CreateOptions(string path) =>
        ((DbContextOptionsBuilder<AppDbContext>)new DbContextOptionsBuilder<AppDbContext>().UseAppSqlite(path)).Options;

    /// <summary>
    /// Creates the directory, applies migrations and switches the database to WAL mode so the API can keep
    /// reading while the scanner writes. Safe to call from both processes at the same time: EF Core takes a
    /// migration lock.
    /// </summary>
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (!string.IsNullOrEmpty(dataSource) && dataSource != ":memory:")
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }

    /// <summary>
    /// Copies the database with SQLite's online backup API: consistent even while other processes write,
    /// unlike copying the file (which can miss data that is still in the WAL file).
    /// </summary>
    public static async Task BackupAsync(string sourcePath, string targetPath, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var source = new SqliteConnection(BuildConnectionString(sourcePath));
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Pooling = false }.ToString());
        await source.OpenAsync(ct);
        await target.OpenAsync(ct);
        source.BackupDatabase(target);
        await using var cmd = target.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE;"; // a self-contained single file
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Used by <c>dotnet ef</c> when adding migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(Database.CreateOptions(Database.ResolvePath()));
}
