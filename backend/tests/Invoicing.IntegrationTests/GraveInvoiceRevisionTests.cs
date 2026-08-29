using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveInvoiceRevisionTests
{
    /// <summary>A grave with one item (count 2 at 100.00, 7% VAT) and two
    /// recipients, invoiced for 2031 H1.</summary>
    private static async Task<(long GraveId, long ItemId, long InvoiceId)> SeedAsync(ApiTestHost host)
    {
        long graveId, itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem { Number = "2001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
            var anna = new Recipient { Number = "E-0001", Name = "Anna Beispiel", Address = "A 1" };
            var bernd = new Recipient { Number = "E-0002", Name = "Bernd Beispiel", Address = "B 2" };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-300",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(new GraveItem { GraveId = grave.Id, ItemId = item.Id, Count = 2 });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id });
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id });
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoiceId = (await verifyDb.Invoices.OrderBy(i => i.Id).FirstAsync(i => i.GraveId == graveId)).Id;
        return (graveId, itemId, invoiceId);
    }

    [Fact]
    public async Task Editing_one_invoice_revises_every_recipient()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = new[] { new { itemId, count = 3, unitPrice = (decimal?)200m } },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices
            .Include(i => i.Lines).Include(i => i.Documents)
            .Where(i => i.GraveId == graveId)
            .ToListAsync();

        // two originals plus one revision each
        Assert.Equal(4, invoices.Count);
        var revisions = invoices.Where(i => i.RevisionNumber == 1).ToList();
        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.True(r.IsLatestRevision));
        Assert.All(revisions, r => Assert.EndsWith("-R1", r.InvoiceNumber));
        Assert.All(invoices.Where(i => i.RevisionNumber == 0), o => Assert.False(o.IsLatestRevision));

        // each revision carries the grave's new line, split in half
        Assert.All(revisions, r =>
        {
            var line = Assert.Single(r.Lines);
            Assert.Equal(3, line.Count);
            Assert.Equal(200m, line.GraveUnitPrice);
            Assert.Equal(100m, line.UnitPrice);
        });

        // both recipients keep their own invoice and get both documents again
        Assert.Equal(new[] { "Anna Beispiel", "Bernd Beispiel" },
            revisions.Select(r => r.RecipientName).OrderBy(n => n));
        Assert.All(revisions, r => Assert.Equal(2, r.Documents.Count));
        Assert.All(revisions, r => Assert.All(r.Documents, d => Assert.NotEmpty(d.Content)));
    }

    [Fact]
    public async Task The_revisions_keep_the_grave_period_and_share()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = new[] { new { itemId, count = 2, unitPrice = (decimal?)100m } },
        }));
        response.EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revisions = await db.Invoices.Where(i => i.GraveId == graveId && i.RevisionNumber == 1).ToListAsync();

        Assert.All(revisions, r => Assert.Equal(graveId, r.GraveId));
        Assert.All(revisions, r => Assert.Equal(2031, r.PeriodYear));
        Assert.All(revisions, r => Assert.Equal(1, r.PeriodHalf));
        Assert.All(revisions, r => Assert.Equal(50m, r.SharePercent));
    }

    [Fact]
    public async Task Editing_twice_keeps_the_chains_in_step()
    {
        using var host = new ApiTestHost();
        var (graveId, itemId, invoiceId) = await SeedAsync(host);
        using var client = host.CreateClient();

        for (var count = 3; count <= 4; count++)
        {
            var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
            {
                invoiceId,
                lines = new[] { new { itemId, count, unitPrice = (decimal?)100m } },
            }));
            response.EnsureSuccessStatusCode();
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db.Invoices.Where(i => i.GraveId == graveId && i.IsLatestRevision).ToListAsync();
        Assert.Equal(2, latest.Count);
        Assert.All(latest, i => Assert.Equal(2, i.RevisionNumber));
    }

    [Fact]
    public async Task Rejects_a_non_grave_invoice()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "2002", Description = "X", VatRate = 7m, DefaultPrice = 10m };
        var recipient = new Recipient { Number = "E-0009", Name = "Solo", Address = "S 1" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);

        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64(),
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)10m } },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("NotAGraveInvoice", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejects_an_empty_line_list_without_touching_anything()
    {
        using var host = new ApiTestHost();
        var (graveId, _, invoiceId) = await SeedAsync(host);

        using var client = host.CreateClient();
        var response = await client.PostAsync("/odata/CreateGraveInvoiceRevision", ODataJson.Body(new
        {
            invoiceId,
            lines = Array.Empty<object>(),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("NoLines", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId == graveId));
    }
}
