using McDiscovery.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Tests;

/// <summary>A migrated SQLite database in a temporary directory, deleted on dispose.</summary>
public sealed class TempDatabase : IAsyncDisposable
{
    private TempDatabase(string directory)
    {
        Directory = directory;
        Path = System.IO.Path.Combine(directory, "test.db");
        Options = Database.CreateOptions(Path);
    }

    public string Directory { get; }
    public string Path { get; }
    public DbContextOptions<AppDbContext> Options { get; }

    public static async Task<TempDatabase> CreateAsync()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcd-tests-" + Guid.NewGuid().ToString("N"));
        var db = new TempDatabase(dir);
        await using var context = db.NewContext();
        await Database.InitializeAsync(context);
        return db;
    }

    public AppDbContext NewContext() => new(Options);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best effort (Windows may still hold a lock for a moment)
        }

        return ValueTask.CompletedTask;
    }
}
