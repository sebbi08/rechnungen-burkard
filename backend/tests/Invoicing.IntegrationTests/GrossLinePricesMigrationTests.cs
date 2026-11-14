using Burkard.Invoicing.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Grave prices became always net and lines gained a gross of their
/// own. A database written before that may hold a grave link priced as a gross;
/// upgrading converts it to the net it billed before, so it does not silently
/// bill the gross as a net, and leaves every existing invoice line as it was.</summary>
public class GrossLinePricesMigrationTests
{
    private const string PreviousMigration = "20261112212917_DocumentComments";

    /// <summary>Migrates a fresh database to the previous migration, seeds it,
    /// migrates on, and hands the upgraded context to <paramref name="check"/>.</summary>
    private static async Task UpgradedAsync(Func<AppDbContext, Task> check)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
        try
        {
            // the seed rows name no cemetery or recipient
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbFile};Foreign Keys=False")
                .Options;
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                await Seed(db);
                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                await check(db);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }

    private static async Task Seed(AppDbContext db)
    {
        // an item at 19 % whose default is a plain 120,00
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceItems (Id, Number, Description, VatRate, DefaultPrice, Quarter) "
                + "VALUES (1, '1001', 'Pflege', '19', '120.00', '')"
        );
        foreach (var (id, number) in new[] { (1, "A"), (2, "B"), (3, "C") })
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Graves (Id, GraveNumber, CemeteryId, StartDate, CreatedAt, "
                    + "FieldNumber, SiteName, Position, ContractNumber, IsCooperative) "
                    + $"VALUES ({id}, '{number}', 1, '2026-01-01', '2026-01-01', '', '', '', '', 0)"
            );
        }

        // A: own gross price; B: the item's default, flagged gross; C: net
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO GraveItems (GraveId, ItemId, Count, UnitPrice, PriceIncludesVat) VALUES "
                + "(1, 1, 1, '75.00', 1), (2, 1, 1, NULL, 1), (3, 1, 1, '50.00', 0)"
        );

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, InvoiceNumber, Year, Sequence, RootId, RevisionNumber, "
                + "IsLatestRevision, RecipientId, RecipientNumber, RecipientName, RecipientAddress, "
                + "IntroText, EndingText, StampWord, GeneratedAt, GraveNumber, GraveSiteName, "
                + "GraveFieldNumber, GravePosition) "
                + "VALUES (1, '2026-0001', 2026, 1, 1, 0, 1, 1, 'R', 'R', '', '', '', '', "
                + "'2026-01-01 00:00:00', '', '', '', '')"
        );
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO InvoiceLines (InvoiceId, LineNumber, ItemNumber, Description, Count, "
                + "UnitPrice, VatRate, PriceIncludesVat, Comment) "
                + "VALUES (1, 1, '1001', 'Pflege', 1, '63.03', '19', 1, '')"
        );
    }

    [Fact]
    public async Task Upgrading_bills_a_gross_grave_price_at_the_net_it_billed_before()
    {
        await UpgradedAsync(async db =>
        {
            var prices = await db
                .GraveItems.OrderBy(l => l.GraveId)
                .Select(l => l.UnitPrice)
                .ToListAsync();
            Assert.Equal(new decimal?[] { 63.03m, 100.84m, 50.00m }, prices);
        });
    }

    [Fact]
    public async Task Upgrading_leaves_existing_invoice_lines_without_a_gross()
    {
        await UpgradedAsync(async db =>
        {
            var line = await db.InvoiceLines.SingleAsync();
            Assert.Null(line.GrossUnitPrice);
            Assert.Equal(63.03m, line.UnitPrice);
            Assert.True(line.PriceIncludesVat);
        });
    }
}
