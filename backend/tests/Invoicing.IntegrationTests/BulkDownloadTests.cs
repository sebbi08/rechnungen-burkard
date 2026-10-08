using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>FR-045 – FR-047: the one bulk download there is, the normal
/// documents of a half-year's grave invoices. Copies are downloaded one at a
/// time, never in bulk.</summary>
public class BulkDownloadTests
{
    /// <summary>A grave invoiced for H1 and H2 2026, and an invoice that
    /// belongs to no grave.</summary>
    private static async Task SeedAsync(ApiTestHost host, HttpClient client)
    {
        long itemId,
            recipientId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "7001",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 10m,
            };
            var recipient = new Recipient
            {
                Number = "E-0001",
                Name = "Anna Beispiel",
                Address = "Weg 1",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            (itemId, recipientId) = (item.Id, recipient.Id);
        }

        var saved = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "B-1",
                    fieldNumber = "1",
                    siteName = "Grabstätte",
                    position = "SingleInside",
                    startDate = "2026-01",
                    endDate = "2040-12",
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
        saved.EnsureSuccessStatusCode();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        await GraveGenerationHelper.RunAsync(client, 2026, 2);

        var created = await client.PostAsync(
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
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        created.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_half_years_download_holds_its_grave_invoices_and_nothing_else()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(host, client);

        var response = await client.PostAsync(
            "/odata/BulkDownloadDocuments",
            ODataJson.Body(
                new
                {
                    kind = "Normal",
                    year = 2026,
                    half = 2,
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db
            .Documents.Where(d =>
                d.Kind == DocumentKind.Normal
                && d.Invoice!.PeriodYear == 2026
                && d.Invoice.PeriodHalf == 2
            )
            .Select(d => d.FileName)
            .ToListAsync();
        Assert.Single(expected);

        using var zip = new ZipArchive(
            await response.Content.ReadAsStreamAsync(),
            ZipArchiveMode.Read
        );
        Assert.Equal(expected, zip.Entries.Select(e => e.Name).ToList());
    }

    [Fact]
    public async Task Copies_are_not_downloaded_in_bulk()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(host, client);

        var response = await client.PostAsync(
            "/odata/BulkDownloadDocuments",
            ODataJson.Body(
                new
                {
                    kind = "Copy",
                    year = 2026,
                    half = 2,
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidKind",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    /// <summary>A year without its half used to download every invoice there
    /// is, grave or not, of every half-year.</summary>
    [Theory]
    [InlineData(2026, null)]
    [InlineData(null, 2)]
    [InlineData(null, null)]
    [InlineData(2026, 3)]
    public async Task A_download_needs_a_whole_half_year(int? year, int? half)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/BulkDownloadDocuments",
            ODataJson.Body(
                new
                {
                    kind = "Normal",
                    year,
                    half,
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidHalfYear",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
}
