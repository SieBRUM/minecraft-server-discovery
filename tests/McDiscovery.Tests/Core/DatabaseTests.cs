using McDiscovery.Core.Data;
using McDiscovery.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace McDiscovery.Tests.Core;

public class DatabaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_enables_wal_and_is_idempotent()
    {
        await using var db = await TempDatabase.CreateAsync();
        await using var ctx = db.NewContext();
        await Database.InitializeAsync(ctx, Ct); // second time: nothing to do

        var mode = await ctx.Database.SqlQueryRaw<string>("SELECT journal_mode AS Value FROM pragma_journal_mode").SingleAsync(Ct);
        Assert.Equal("wal", mode);
    }

    [Fact]
    public async Task Backup_contains_the_data()
    {
        await using var db = await TempDatabase.CreateAsync();
        await using (var ctx = db.NewContext())
        {
            var now = DateTime.UtcNow;
            ctx.Servers.Add(new Server { Address = "1.2.3.4", Port = 25565, FirstSeenUtc = now, LastSeenUtc = now, LastScannedUtc = now });
            await ctx.SaveChangesAsync(Ct);
        }

        var backup = Path.Combine(db.Directory, "backups", "copy.db");
        await Database.BackupAsync(db.Path, backup, Ct);

        await using var copy = new AppDbContext(Database.CreateOptions(backup));
        Assert.Equal("1.2.3.4", (await copy.Servers.SingleAsync(Ct)).Address);
        Assert.Equal(DateTimeKind.Utc, (await copy.Servers.SingleAsync(Ct)).FirstSeenUtc.Kind);
    }
}
