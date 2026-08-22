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

    private async Task<(long InvoiceId, long ItemId, long RecipientId, byte[] NormalBytes)> SeedInvoiceAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "5001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var recipient = new Recipient { Name = "Familie Mahr", Address = "Altstraße 1\n11111 Altstadt" };
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
        using var json = await ODataJson.ReadAsync(created);
        var invoiceId = json.RootElement.GetProperty("Id").GetInt64();

        var normal = await db.Documents.SingleAsync(d => d.InvoiceId == invoiceId && d.Kind == DocumentKind.Normal);
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
        var response = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[]
            {
                new { itemId, count = 1, unitPrice = (decimal?)null },
                new { itemId, count = 1, unitPrice = (decimal?)null },
            },
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.EndsWith("-R1", json.RootElement.GetProperty("InvoiceNumber").GetString());

        var invoices = await db.Invoices.Where(i => i.Id == invoiceId || i.RootId == invoiceId)
            .Include(i => i.Lines).Include(i => i.Documents)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(2, invoices.Count);

        var original = invoices[0];
        var revision = invoices[1];
        Assert.Equal(original.RootId, revision.RootId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("Altstraße 1\n11111 Altstadt", original.RecipientAddress);
        Assert.Equal("Neustraße 2\n22222 Neustadt", revision.RecipientAddress);
        Assert.Equal(1, original.Lines.Count);
        Assert.Equal(2, revision.Lines.Count);

        // original documents unchanged (byte-identical)
        var originalNormal = original.Documents.Single(d => d.Kind == DocumentKind.Normal);
        Assert.Equal(originalBytes, originalNormal.Content);
        Assert.Equal(2, revision.Documents.Count);
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
        var response = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[] { new { itemId, count = 1, unitPrice = (decimal?)null } },
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("RecipientAddressRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        var revisions = await db.Invoices.CountAsync(i => i.RootId == invoiceId);
        Assert.Equal(1, revisions);
    }
}
