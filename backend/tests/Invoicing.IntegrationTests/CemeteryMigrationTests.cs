using Burkard.Invoicing.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Every grave needs a cemetery. Upgrading a database whose graves
/// have none puts them all in one named "Unbekannt", for the editor to correct;
/// a database without graves gets no cemetery at all.</summary>
public class CemeteryMigrationTests
{
    // the last migration of v3.1.0, the release before this change
    private const string PreviousMigration = "20261002083107_GraveSiteFieldsAndGermanOnly";

    private static async Task UpgradeAsync(string dbFile, string? seedSql)
    {
        await using var db = new AppDbContext(Options(dbFile));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        if (seedSql is not null)
        {
            await db.Database.ExecuteSqlRawAsync(seedSql);
        }

        await migrator.MigrateAsync();
    }

    private static DbContextOptions<AppDbContext> Options(string dbFile) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbFile}").Options;

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
        try
        {
            await test(dbFile);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }

    [Fact]
    public Task Upgrading_puts_the_stored_graves_in_one_unknown_cemetery() =>
        WithDatabaseAsync(async dbFile =>
        {
            await UpgradeAsync(
                dbFile,
                "INSERT INTO Graves (GraveNumber, FieldNumber, SiteName, StartDate, CreatedAt) "
                    + "VALUES ('M-1', '27', 'Schineller Bäcke', '2026-01-01', '2026-01-01'), "
                    + "('M-2', '28', 'Am Hang', '2026-01-01', '2026-01-01')"
            );

            await using var db = new AppDbContext(Options(dbFile));
            var cemetery = await db.Cemeteries.SingleAsync();
            Assert.Equal("Unbekannt", cemetery.Name);
            var graves = await db.Graves.Include(g => g.Cemetery).ToListAsync();
            Assert.Equal(2, graves.Count);
            Assert.All(graves, g => Assert.Equal(cemetery.Id, g.CemeteryId));
            // the site name is left as it was
            Assert.Contains(graves, g => g.SiteName == "Schineller Bäcke");
        });

    [Fact]
    public Task Upgrading_a_database_without_graves_creates_no_cemetery() =>
        WithDatabaseAsync(async dbFile =>
        {
            await UpgradeAsync(dbFile, null);

            await using var db = new AppDbContext(Options(dbFile));
            Assert.Empty(await db.Cemeteries.ToListAsync());
        });

    [Fact]
    public Task A_cemetery_with_graves_cannot_be_deleted_in_the_database_either() =>
        WithDatabaseAsync(async dbFile =>
        {
            await UpgradeAsync(
                dbFile,
                "INSERT INTO Graves (GraveNumber, FieldNumber, SiteName, StartDate, CreatedAt) "
                    + "VALUES ('M-1', '27', 'Schineller Bäcke', '2026-01-01', '2026-01-01')"
            );

            await using var db = new AppDbContext(Options(dbFile));
            db.Cemeteries.Remove(await db.Cemeteries.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
}
