using McDiscovery.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace McDiscovery.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<ServerPlayer> ServerPlayers => Set<ServerPlayer>();
    public DbSet<ServerSnapshot> Snapshots => Set<ServerSnapshot>();
    public DbSet<Favicon> Favicons => Set<Favicon>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no date type; EF stores ISO-8601 text. Make sure values always come back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Server>(e =>
        {
            e.HasIndex(x => new { x.Address, x.Port, x.Edition }).IsUnique();
            e.HasIndex(x => x.LastSeenUtc);
            e.HasIndex(x => x.FirstSeenUtc);
            e.HasIndex(x => x.PlayersOnline);
            e.HasIndex(x => x.CountryCode);
            e.HasIndex(x => x.VersionName);
            e.HasIndex(x => x.IsOnline);
            e.HasOne(x => x.Favicon).WithMany().HasForeignKey(x => x.FaviconId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Player>(e =>
        {
            e.HasIndex(x => x.Uuid).IsUnique();
            // Name-only players (no UUID) are unique by name.
            e.HasIndex(x => x.Name).IsUnique().HasFilter("\"Uuid\" IS NULL").HasDatabaseName("IX_Players_Name_NoUuid");
            e.HasIndex(x => x.Name).HasDatabaseName("IX_Players_Name");
        });

        modelBuilder.Entity<ServerPlayer>(e =>
        {
            e.HasKey(x => new { x.ServerId, x.PlayerId });
            e.HasIndex(x => x.PlayerId);
            e.HasOne(x => x.Server).WithMany(x => x.Players).HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany(x => x.Servers).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerSnapshot>(e =>
        {
            e.HasIndex(x => new { x.ServerId, x.TimestampUtc });
            e.HasIndex(x => x.TimestampUtc);
            e.HasOne(x => x.Server).WithMany(x => x.Snapshots).HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Favicon>(e => e.HasIndex(x => x.Sha256).IsUnique());

        modelBuilder.Entity<ScanRun>(e => e.HasIndex(x => x.StartedUtc));
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
