using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Four ending texts instead of one: ordinary invoices, quotes, and the
/// grave invoices of each half-year print their own. Every document takes the
/// text of its kind as it is set when the document is made.</summary>
public class EndingTextTests
{
    private const string InvoiceText = "Zahlbar sofort.";
    private const string QuoteText = "Dieses Angebot gilt vier Wochen.";
    private const string FirstHalfText = "Pflege Januar bis Juni.";
    private const string SecondHalfText = "Pflege Juli bis Dezember.";

    private static async Task SetTextsAsync(HttpClient client)
    {
        var response = await client.PatchAsync(
            "/odata/Settings",
            ODataJson.Body(
                new
                {
                    InvoiceEndingText = InvoiceText,
                    QuoteEndingText = QuoteText,
                    GraveFirstHalfEndingText = FirstHalfText,
                    GraveSecondHalfEndingText = SecondHalfText,
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_settings_keep_four_texts_with_their_line_breaks()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var response = await client.PatchAsync(
            "/odata/Settings",
            ODataJson.Body(
                new
                {
                    InvoiceEndingText = "Rechnung\nZeile zwei",
                    QuoteEndingText = "Angebot\nZeile zwei",
                    GraveFirstHalfEndingText = "H1\nZeile zwei",
                    GraveSecondHalfEndingText = "H2\nZeile zwei",
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ODataJson.ReadAsync(await client.GetAsync("/odata/Settings"));
        var root = json.RootElement;
        Assert.Equal("Rechnung\nZeile zwei", root.GetProperty("InvoiceEndingText").GetString());
        Assert.Equal("Angebot\nZeile zwei", root.GetProperty("QuoteEndingText").GetString());
        Assert.Equal("H1\nZeile zwei", root.GetProperty("GraveFirstHalfEndingText").GetString());
        Assert.Equal("H2\nZeile zwei", root.GetProperty("GraveSecondHalfEndingText").GetString());
        Assert.False(root.TryGetProperty("EndingText", out _));
    }

    [Fact]
    public async Task An_invoice_and_its_revision_print_the_invoice_text()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();
        await SetTextsAsync(client);

        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(new { recipientId, lines = QuoteTests.Lines(itemId, 1) })
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();
        Assert.Equal(InvoiceText, createdJson.RootElement.GetProperty("EndingText").GetString());

        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = QuoteTests.Lines(itemId, 2),
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        using var revisedJson = await ODataJson.ReadAsync(revised);
        Assert.Equal(InvoiceText, revisedJson.RootElement.GetProperty("EndingText").GetString());
    }

    [Fact]
    public async Task A_quote_prints_the_quote_text_and_becomes_an_invoice_with_the_invoice_text()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();
        await SetTextsAsync(client);

        var (quoteId, _) = await QuoteTests.CreatedAsync(
            await QuoteTests.CreateQuoteAsync(client, recipientId, QuoteTests.Lines(itemId, 1))
        );
        var (revisionId, _) = await QuoteTests.CreatedAsync(
            await QuoteTests.ReviseQuoteAsync(
                client,
                quoteId,
                recipientId,
                QuoteTests.Lines(itemId, 2)
            )
        );

        var converted = await client.PostAsync(
            "/odata/ConvertQuote",
            ODataJson.Body(new { quoteId })
        );
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        using var invoiceJson = await ODataJson.ReadAsync(converted);
        Assert.Equal(InvoiceText, invoiceJson.RootElement.GetProperty("EndingText").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var texts = await db
            .Quotes.Where(q => q.Id == quoteId || q.Id == revisionId)
            .Select(q => q.EndingText)
            .ToListAsync();
        Assert.Equal(new[] { QuoteText, QuoteText }, texts);
    }

    [Fact]
    public async Task Grave_invoices_print_the_text_of_their_half_year()
    {
        using var host = new ApiTestHost();
        var itemId = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        await SetTextsAsync(client);

        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        await GraveGenerationHelper.RunAsync(client, 2026, 2);

        var first = await GraveInvoiceAsync(host, 1);
        var second = await GraveInvoiceAsync(host, 2);
        Assert.Equal(FirstHalfText, first.EndingText);
        Assert.Equal(SecondHalfText, second.EndingText);

        // revised in the second half-year, the first half-year's invoice keeps
        // the text of the half-year it bills
        var revised = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = first.Id,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)100m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var revision = await GraveInvoiceAsync(host, 1);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(FirstHalfText, revision.EndingText);
    }

    [Fact]
    public async Task Upgrading_puts_the_old_text_into_all_four()
    {
        const string previousMigration = "20261112142130_Quotes";
        var dbFile = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbFile}")
                .Options;
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(previousMigration);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO GlobalSettings (EndingText, StampWord) VALUES ('Alter Text', 'Kopie')"
                );
                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var settings = await db.GlobalSettings.SingleAsync();
                Assert.Equal("Alter Text", settings.InvoiceEndingText);
                Assert.Equal("Alter Text", settings.QuoteEndingText);
                Assert.Equal("Alter Text", settings.GraveFirstHalfEndingText);
                Assert.Equal("Alter Text", settings.GraveSecondHalfEndingText);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }

    /// <summary>One grave billed all year to one recipient, due in both
    /// half-years of 2026. Answers the item's id.</summary>
    private static async Task<long> SeedGraveAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "6001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0001",
            Name = "Anna",
            Address = "A-Straße 1",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        var grave = new Grave
        {
            GraveNumber = "G-1",
            CemeteryId = TestCemetery.Id,
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        db.GraveItems.Add(
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = item.Id,
                Count = 1,
            }
        );
        db.GraveRecipients.Add(
            new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
        );
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>The latest revision of the grave invoice for a half-year of 2026.</summary>
    private static async Task<Invoice> GraveInvoiceAsync(ApiTestHost host, int half)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Invoices.AsNoTracking()
            .SingleAsync(i =>
                i.GraveId != null
                && i.PeriodYear == 2026
                && i.PeriodHalf == half
                && i.IsLatestRevision
            );
    }
}
