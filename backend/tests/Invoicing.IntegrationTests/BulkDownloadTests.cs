using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class BulkDownloadTests
{
    private async Task SeedTwoInvoicesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "7001", Description = "X", VatRate = 7m, DefaultPrice = 10m };
        var recipient = new Recipient { Number = "E-0001", Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId = recipient.Id,
                lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
            }));
            created.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Bulk_normal_download_contains_only_normal_documents()
    {
        using var host = new ApiTestHost();
        await SeedTwoInvoicesAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Normal" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.False(n.Contains("Kopie")));
    }

    [Fact]
    public async Task Bulk_copy_download_contains_only_copies()
    {
        using var host = new ApiTestHost();
        await SeedTwoInvoicesAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Copy" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.Contains("Kopie", n));
    }
}
