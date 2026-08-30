using System.IO.Compression;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class LatestRevisionTests
{
    private static async Task<(long InvoiceId, long ItemId, long RecipientId)> SeedAsync(
        ApiTestHost host
    )
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
            Name = "Familie Beispiel",
            Address = "Hauptstraße 1",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId = recipient.Id,
                    lines = new[]
                    {
                        new
                        {
                            itemId = item.Id,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        created.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(created);
        return (json.RootElement.GetProperty("Id").GetInt64(), item.Id, recipient.Id);
    }

    [Fact]
    public async Task A_new_invoice_is_the_latest_revision_of_its_chain()
    {
        using var host = new ApiTestHost();
        var (invoiceId, _, _) = await SeedAsync(host);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).IsLatestRevision);
    }

    [Fact]
    public async Task Revising_moves_the_flag_to_the_new_row()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = await db
            .Invoices.Where(i => i.RootId == invoiceId)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(2, chain.Count);
        Assert.False(chain[0].IsLatestRevision);
        Assert.True(chain[1].IsLatestRevision);
    }

    [Fact]
    public async Task Revising_twice_leaves_exactly_one_latest()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await client.PostAsync(
                "/odata/CreateRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId,
                        recipientId,
                        lines = new[]
                        {
                            new
                            {
                                itemId,
                                count = attempt,
                                unitPrice = (decimal?)null,
                            },
                        },
                    }
                )
            );
            response.EnsureSuccessStatusCode();
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db
            .Invoices.Where(i => i.RootId == invoiceId && i.IsLatestRevision)
            .ToListAsync();
        Assert.Equal(2, Assert.Single(latest).RevisionNumber);
    }

    [Fact]
    public async Task The_migrations_backfill_statement_clears_superseded_rows()
    {
        // The migration runs against a database that predates the column, which
        // this host never has. Run its statement against a chain whose flags are
        // all still at the column default, which is exactly the state it faces.
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        revised.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Invoices SET IsLatestRevision = 1;");

        await db.Database.ExecuteSqlRawAsync(
            @"
            UPDATE Invoices SET IsLatestRevision = 0
            WHERE EXISTS (
                SELECT 1 FROM Invoices later
                WHERE later.RootId = Invoices.RootId
                  AND later.RevisionNumber > Invoices.RevisionNumber);"
        );

        var chain = await db
            .Invoices.Where(i => i.RootId == invoiceId)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(new[] { false, true }, chain.Select(i => i.IsLatestRevision));
    }

    [Fact]
    public async Task Bulk_download_leaves_out_superseded_revisions()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        revised.EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            "/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Normal" })
        );
        response.EnsureSuccessStatusCode();

        using var zip = new ZipArchive(
            await response.Content.ReadAsStreamAsync(),
            ZipArchiveMode.Read
        );
        var name = Assert.Single(zip.Entries).Name;
        Assert.Contains("-R1", name);
    }

    [Fact]
    public async Task A_grave_invoice_line_keeps_the_grave_price_it_was_split_from()
    {
        using var host = new ApiTestHost();
        long graveId;
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "6002",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var first = new Recipient
            {
                Number = "E-0002",
                Name = "Anna",
                Address = "A 1",
            };
            var second = new Recipient
            {
                Number = "E-0003",
                Name = "Bernd",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(first, second);
            var grave = new Grave
            {
                GraveNumber = "G-100",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(System.DateTime.UtcNow),
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
                new GraveRecipient { GraveId = grave.Id, RecipientId = first.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = second.Id }
            );
            await db.SaveChangesAsync();
            graveId = grave.Id;
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2029, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = await verifyDb
            .InvoiceLines.Where(l =>
                verifyDb.Invoices.Any(i => i.Id == l.InvoiceId && i.GraveId == graveId)
            )
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        // each recipient pays half, but the line remembers the grave's price
        Assert.All(lines, l => Assert.Equal(50m, l.UnitPrice));
        Assert.All(lines, l => Assert.Equal(100m, l.GraveUnitPrice));
    }
}
