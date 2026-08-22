using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class DocumentDownloadTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public DocumentDownloadTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Download_normal_document_returns_stored_pdf_bytes()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "5002", Description = "X", VatRate = 7m, DefaultPrice = 5m };
        var recipient = new Recipient { Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();

        var stored = await db.Documents.SingleAsync(d => d.Kind == DocumentKind.Normal);
        var response = await client.GetAsync($"/odata/Documents({stored.Id})/$value");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(stored.Content, bytes);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Contains(".pdf", disposition!.FileNameStar ?? disposition.FileName ?? "");
    }
}
