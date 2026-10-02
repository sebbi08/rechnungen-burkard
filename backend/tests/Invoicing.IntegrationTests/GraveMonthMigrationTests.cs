using Burkard.Invoicing.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave's term became a month. A database written before that has
/// days in it; upgrading turns each into the first of its month and leaves the
/// new site fields empty for the editor to fill.</summary>
public class GraveMonthMigrationTests
{
    // the last migration of v2.1.0, the release before this change
    private const string PreviousMigration = "20260901134020_PaymentVatAndGraveShare";

    [Fact]
    public async Task Upgrading_truncates_grave_terms_to_their_month()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbFile}")
                .Options;
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Graves (GraveNumber, StartDate, EndDate, CreatedAt) "
                        + "VALUES ('M-1', '2026-03-15', '2040-12-31', '2026-03-15')"
                );
                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var grave = await db.Graves.SingleAsync();
                Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
                Assert.Equal(new DateOnly(2040, 12, 1), grave.EndDate);
                Assert.Equal("", grave.FieldNumber);
                Assert.Equal("", grave.SiteName);
                Assert.Null(grave.Position);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }
}
