using System.Net;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What the document prints in its head and above the lines is
/// snapshotted like the recipient's name and address (FR-017): a later change
/// to the recipient or the grave never reaches an issued invoice.</summary>
public class InvoiceSnapshotTests
{
    private static async Task<(long GraveId, long ItemId)> SeedGraveAsync(
        ApiTestHost host,
        HttpClient client
    )
    {
        long itemId,
            recipientId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 19m });
            var item = new InvoiceItem
            {
                Number = "12001",
                Description = "Herbstbepflanzung",
                VatRate = 19m,
                DefaultPrice = 24m,
            };
            var recipient = new Recipient
            {
                Number = "067200",
                Name = "Werner Schineller",
                Address = "Bartholomäus-Weltz-Straße 4\n67346 Speyer",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            (itemId, recipientId) = (item.Id, recipient.Id);
        }

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "1234",
                    fieldNumber = "27",
                    siteName = "Schineller Bäcke",
                    position = "DoubleOutside",
                    startDate = "2020-01",
                    endDate = (string?)null,
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return (json.RootElement.GetProperty("Id").GetInt64(), itemId);
    }

    [Fact]
    public async Task A_grave_invoice_keeps_the_site_it_was_generated_with()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId) = await SeedGraveAsync(host, client);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        // the grave is renamed after the invoice went out
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == graveId);
            grave.SiteName = "Neuer Name";
            await db.SaveChangesAsync();
        }

        long invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.GraveId == graveId);
            Assert.Equal("067200", invoice.RecipientNumber);
            Assert.Equal("1234", invoice.GraveNumber);
            Assert.Equal("Schineller Bäcke", invoice.GraveSiteName);
            Assert.Equal("27", invoice.GraveFieldNumber);
            Assert.Equal(GravePosition.DoubleOutside, invoice.GravePosition);
            invoiceId = invoice.Id;
        }

        // a revision is newly generated, so it takes the grave as it is now
        var revision = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)24m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latest = await db.Invoices.SingleAsync(i =>
                i.GraveId == graveId && i.IsLatestRevision
            );
            Assert.Equal("Neuer Name", latest.GraveSiteName);
            Assert.Equal("1234", latest.GraveNumber);
            Assert.Equal("067200", latest.RecipientNumber);
        }
    }

    [Fact]
    public async Task An_ordinary_invoice_carries_the_recipient_number_and_no_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedGraveAsync(host, client);
        long recipientId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            recipientId = (await db.Recipients.SingleAsync()).Id;
            itemId = (await db.InvoiceItems.SingleAsync()).Id;
        }

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)24m,
                        },
                    },
                }
            )
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope2 = host.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db2.Invoices.SingleAsync(i => i.GraveId == null);
        Assert.Equal("067200", invoice.RecipientNumber);
        Assert.Null(invoice.GraveNumber);
        Assert.Null(invoice.GraveSiteName);
    }
}
