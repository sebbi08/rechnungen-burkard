using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RevisionTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public RevisionTests(ApiTestHost host) => _host = host;

    private async Task<(
        long InvoiceId,
        long ItemId,
        long RecipientId,
        byte[] NormalBytes
    )> SeedInvoiceAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "5001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0001",
            Name = "Familie Mahr",
            Address = "Altstraße 1\n11111 Altstadt",
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
        var invoiceId = json.RootElement.GetProperty("Id").GetInt64();

        var normal = await db.Documents.SingleAsync(d =>
            d.InvoiceId == invoiceId && d.Kind == DocumentKind.Normal
        );
        return (invoiceId, item.Id, recipient.Id, normal.Content);
    }

    [Fact]
    public async Task Revision_creates_new_row_keeps_original_and_resnapshots_recipient()
    {
        var (invoiceId, itemId, recipientId, originalBytes) = await SeedInvoiceAsync(_host);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Recipients.FindAsync(recipientId);
        recipient!.Address = "Neustraße 2\n22222 Neustadt";
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
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
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.EndsWith("-R1", json.RootElement.GetProperty("InvoiceNumber").GetString());

        var invoices = await db
            .Invoices.Where(i => i.Id == invoiceId || i.RootId == invoiceId)
            .Include(i => i.Lines)
            .Include(i => i.Documents)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(2, invoices.Count);

        var original = invoices[0];
        var revision = invoices[1];
        Assert.Equal(original.RootId, revision.RootId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("Altstraße 1\n11111 Altstadt", original.RecipientAddress);
        Assert.Equal("Neustraße 2\n22222 Neustadt", revision.RecipientAddress);
        Assert.Single(original.Lines);
        Assert.Equal(2, revision.Lines.Count);

        // original documents unchanged (byte-identical)
        var originalNormal = original.Documents.Single(d => d.Kind == DocumentKind.Normal);
        Assert.Equal(originalBytes, originalNormal.Content);
        Assert.Equal(2, revision.Documents.Count);
    }

    [Fact]
    public async Task Editing_the_same_invoice_twice_produces_two_revisions()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId, _) = await SeedInvoiceAsync(host);
        using var client = host.CreateClient();

        // both edits start from the original row, the way the invoice list offers
        // an edit button on every revision
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
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = await ODataJson.ReadAsync(response);
            Assert.EndsWith(
                $"-R{attempt}",
                json.RootElement.GetProperty("InvoiceNumber").GetString()
            );
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = await db
            .Invoices.Where(i => i.RootId == invoiceId)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(new[] { 0, 1, 2 }, chain.Select(i => i.RevisionNumber));
        // the original is still the one-line invoice it was issued as
        Assert.Single(await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).ToListAsync());
    }

    [Fact]
    public async Task Revision_with_blank_recipient_address_fails()
    {
        using var host = new ApiTestHost();
        var (invoiceId, itemId, recipientId, _) = await SeedInvoiceAsync(host);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Recipients.FindAsync(recipientId);
        recipient!.Address = "";
        await db.SaveChangesAsync();

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
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "RecipientAddressRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        var revisions = await db.Invoices.CountAsync(i => i.RootId == invoiceId);
        Assert.Equal(1, revisions);
    }

    [Fact]
    public async Task Revising_a_grave_invoice_through_the_plain_action_is_rejected()
    {
        // own host: this test seeds the same vat rate as the other tests in
        // this class, so the shared fixture database would collide
        using var host = new ApiTestHost();

        // seed a grave with one item and one recipient, then generate its invoice
        long graveId,
            itemId,
            recipientId,
            invoiceId;
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "5002",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var recipient = new Recipient
            {
                Number = "E-0002",
                Name = "Max Beispiel",
                Address = "Hauptstraße 2\n99999 Musterdorf",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.Add(recipient);
            var grave = new Grave
            {
                GraveNumber = "R-001",
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
                    Count = 2,
                }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId, recipientId) = (grave.Id, item.Id, recipient.Id);
        }

        using var client = host.CreateClient();
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2027, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var jobResponse = await client.GetAsync($"/odata/Jobs({jobId})");
            jobResponse.EnsureSuccessStatusCode();
            using var jobJson = await ODataJson.ReadAsync(jobResponse);
            var status = jobJson.RootElement.GetProperty("Status").GetString();
            Assert.NotEqual("Failed", status);
            if (status == "Succeeded")
            {
                break;
            }

            await Task.Delay(100);
        }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = (
                await db.Invoices.SingleAsync(i =>
                    i.GraveId == graveId && i.PeriodYear == 2027 && i.PeriodHalf == 1
                )
            ).Id;
        }

        var revisionResponse = await client.PostAsync(
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

        Assert.Equal(HttpStatusCode.BadRequest, revisionResponse.StatusCode);
        using var json = await ODataJson.ReadAsync(revisionResponse);
        Assert.Equal(
            "GraveInvoiceRevisionRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        using var scope2 = host.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db2.Invoices.CountAsync(i => i.RootId == invoiceId));
    }
}
