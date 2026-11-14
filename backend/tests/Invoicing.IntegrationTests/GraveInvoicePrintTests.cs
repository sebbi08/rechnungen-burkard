using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>FR-045a: the grave invoices of a half-year as one PDF to print —
/// the same documents as the bulk download, in its order, served for display.</summary>
public class GraveInvoicePrintTests
{
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    [Fact]
    public async Task A_half_years_print_holds_the_same_invoices_as_its_zip()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await BulkDownloadTests.SeedAsync(host, client, "B-1", "B-2");

        var zipResponse = await client.PostAsync(
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
        zipResponse.EnsureSuccessStatusCode();
        using var zip = new ZipArchive(
            await zipResponse.Content.ReadAsStreamAsync(),
            ZipArchiveMode.Read
        );
        var entries = zip
            .Entries.Select(entry =>
            {
                using var stream = entry.Open();
                using var memory = new System.IO.MemoryStream();
                stream.CopyTo(memory);
                using var pdf = PdfDocument.Open(memory.ToArray());
                return (Number: entry.Name[..^".pdf".Length], Pages: pdf.NumberOfPages);
            })
            .ToList();
        Assert.Equal(2, entries.Count);

        var response = await client.GetAsync("/odata/GraveInvoicesPdf?year=2026&half=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("inline", disposition?.DispositionType);
        Assert.Equal(
            "Grabrechnungen-2026-2.pdf",
            disposition?.FileNameStar ?? disposition?.FileName?.Trim('"')
        );

        using var printed = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(entries.Sum(e => e.Pages), printed.NumberOfPages);
        // each invoice starts where the pages of the ones before it end
        var firstPage = 1;
        foreach (var (number, pages) in entries)
        {
            Assert.Contains(number, printed.GetPage(firstPage).Text);
            firstPage += pages;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?year=2026")]
    [InlineData("?half=2")]
    [InlineData("?year=2026&half=3")]
    [InlineData("?year=abc&half=1")]
    public async Task A_print_needs_a_whole_half_year(string query)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync($"/odata/GraveInvoicesPdf{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidHalfYear", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_half_year_without_invoices_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await BulkDownloadTests.SeedAsync(host, client);

        var response = await client.GetAsync("/odata/GraveInvoicesPdf?year=2030&half=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("NoGraveInvoices", await ErrorCodeAsync(response));
    }
}
