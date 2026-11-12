using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class QuoteTests
{
    private static async Task<(long ItemId, long RecipientId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 120.00m,
        };
        var recipient = new Recipient
        {
            Number = "E-0004",
            Name = "Familie Mahr",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    private static object Lines(long itemId, int count, decimal? unitPrice = null) =>
        new[]
        {
            new
            {
                itemId,
                count,
                unitPrice,
            },
        };

    private static Task<HttpResponseMessage> CreateQuoteAsync(
        HttpClient client,
        long recipientId,
        object lines
    ) => client.PostAsync("/odata/CreateQuote", ODataJson.Body(new { recipientId, lines }));

    private static Task<HttpResponseMessage> ReviseQuoteAsync(
        HttpClient client,
        long quoteId,
        long recipientId,
        object lines
    ) =>
        client.PostAsync(
            "/odata/CreateQuoteRevision",
            ODataJson.Body(
                new
                {
                    quoteId,
                    recipientId,
                    lines,
                }
            )
        );

    private static async Task<(long Id, string Number)> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return (
            json.RootElement.GetProperty("Id").GetInt64(),
            json.RootElement.GetProperty("QuoteNumber").GetString()!
        );
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    [Fact]
    public async Task Creating_a_quote_snapshots_and_renders_one_pdf()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await CreateQuoteAsync(client, recipientId, Lines(itemId, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var root = json.RootElement;
        var id = root.GetProperty("Id").GetInt64();
        Assert.Equal("A2026-0001", root.GetProperty("QuoteNumber").GetString());
        Assert.Equal("Open", root.GetProperty("State").GetString());
        Assert.Equal(0, root.GetProperty("RevisionNumber").GetInt32());
        Assert.Equal(id, root.GetProperty("RootId").GetInt64());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quote = await db
            .Quotes.Include(q => q.Lines)
            .Include(q => q.Document)
            .SingleAsync(q => q.Id == id);
        var settings = await db.GlobalSettings.SingleAsync();

        Assert.Equal("Familie Mahr", quote.RecipientName);
        Assert.Equal("Musterstraße 1\n12345 Musterstadt", quote.RecipientAddress);
        Assert.Equal("E-0004", quote.RecipientNumber);
        Assert.Equal(settings.EndingText, quote.EndingText);
        Assert.True(quote.IsLatestRevision);
        var line = Assert.Single(quote.Lines);
        Assert.Equal(2, line.Count);
        Assert.Equal(120.00m, line.UnitPrice);
        Assert.Equal(7m, line.VatRate);
        Assert.Equal("4001", line.ItemNumber);

        Assert.NotNull(quote.Document);
        Assert.Equal("A2026-0001.pdf", quote.Document!.FileName);
        using var pdf = PdfDocument.Open(quote.Document.Content);
        Assert.Contains("**** ANGEBOT ****", pdf.GetPage(1).Text);

        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Empty(await db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Quote_numbers_count_per_year_apart_from_invoices()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var invoice = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(new { recipientId, lines = Lines(itemId, 1) })
        );
        Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        using (var json = await ODataJson.ReadAsync(invoice))
        {
            Assert.Equal("2026-0001", json.RootElement.GetProperty("InvoiceNumber").GetString());
        }

        var (_, first) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (_, second) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        Assert.Equal("A2026-0001", first);
        Assert.Equal("A2026-0002", second);

        var nextInvoice = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(new { recipientId, lines = Lines(itemId, 1) })
        );
        Assert.Equal(HttpStatusCode.Created, nextInvoice.StatusCode);
        using (var json = await ODataJson.ReadAsync(nextInvoice))
        {
            Assert.Equal("2026-0002", json.RootElement.GetProperty("InvoiceNumber").GetString());
        }
    }

    [Fact]
    public async Task Quotes_are_validated_like_invoices()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        long unpricedItemId;
        long addresslessRecipientId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unpriced = new InvoiceItem
            {
                Number = "4002",
                Description = "Ohne Preis",
                VatRate = 7m,
                DefaultPrice = null,
            };
            var addressless = new Recipient
            {
                Number = "E-0005",
                Name = "Ohne Adresse",
                Address = "",
            };
            db.InvoiceItems.Add(unpriced);
            db.Recipients.Add(addressless);
            await db.SaveChangesAsync();
            unpricedItemId = unpriced.Id;
            addresslessRecipientId = addressless.Id;
        }
        using var client = host.CreateClient();

        Assert.Equal(
            "NoLines",
            await ErrorCodeAsync(await CreateQuoteAsync(client, recipientId, new object[0]))
        );
        Assert.Equal(
            "RecipientAddressRequired",
            await ErrorCodeAsync(
                await CreateQuoteAsync(client, addresslessRecipientId, Lines(itemId, 1))
            )
        );
        Assert.Equal(
            "PriceRequired",
            await ErrorCodeAsync(
                await CreateQuoteAsync(client, recipientId, Lines(unpricedItemId, 1))
            )
        );
        Assert.Equal(
            "InvalidPrice",
            await ErrorCodeAsync(
                await CreateQuoteAsync(client, recipientId, Lines(itemId, 3, 10.125m))
            )
        );

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await verifyDb.Quotes.ToListAsync());
    }

    [Fact]
    public async Task Revising_a_quote_keeps_the_old_version()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 2))
        );

        var (revisionId, revisionNumber) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 3))
        );

        Assert.Equal("A2026-0001-R1", revisionNumber);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var original = await db
            .Quotes.Include(q => q.Lines)
            .Include(q => q.Document)
            .SingleAsync(q => q.Id == originalId);
        var revision = await db
            .Quotes.Include(q => q.Lines)
            .Include(q => q.Document)
            .SingleAsync(q => q.Id == revisionId);

        Assert.False(original.IsLatestRevision);
        Assert.Equal("A2026-0001", original.QuoteNumber);
        Assert.Equal(2, Assert.Single(original.Lines).Count);
        Assert.Equal("A2026-0001.pdf", original.Document!.FileName);

        Assert.True(revision.IsLatestRevision);
        Assert.Equal(originalId, revision.RootId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(QuoteState.Open, revision.State);
        Assert.Equal(3, Assert.Single(revision.Lines).Count);
        Assert.Equal("A2026-0001-R1.pdf", revision.Document!.FileName);
        Assert.NotEqual(original.Document.Content, revision.Document.Content);
    }

    [Fact]
    public async Task Revising_an_older_version_continues_the_chain()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (firstRevisionId, _) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 2))
        );
        await CreatedAsync(
            await ReviseQuoteAsync(client, firstRevisionId, recipientId, Lines(itemId, 3))
        );

        var (_, number) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 4))
        );

        Assert.Equal("A2026-0001-R3", number);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db.Quotes.Where(q => q.IsLatestRevision).ToListAsync();
        Assert.Equal("A2026-0001-R3", Assert.Single(latest).QuoteNumber);
    }

    [Fact]
    public async Task A_revision_keeps_the_year_of_its_quote()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = await db.Quotes.SingleAsync(q => q.Id == originalId);
            quote.Year = 2025;
            quote.Sequence = 7;
            quote.QuoteNumber = "A2025-0007";
            await db.SaveChangesAsync();
        }

        var (revisionId, number) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 2))
        );

        Assert.Equal("A2025-0007-R1", number);
        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revision = await verifyDb.Quotes.SingleAsync(q => q.Id == revisionId);
        Assert.Equal(2025, revision.Year);
        Assert.Equal(7, revision.Sequence);
    }

    [Fact]
    public async Task The_pdf_opens_inline()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (id, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );

        var response = await client.GetAsync($"/odata/Quotes({id})/Pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("inline", disposition!.DispositionType);
        Assert.Contains("A2026-0001.pdf", disposition.FileNameStar ?? disposition.FileName ?? "");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.QuoteDocuments.SingleAsync(d => d.QuoteId == id);
        Assert.Equal(stored.Content, await response.Content.ReadAsByteArrayAsync());

        var unknown = await client.GetAsync($"/odata/Quotes({id + 1000})/Pdf");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
