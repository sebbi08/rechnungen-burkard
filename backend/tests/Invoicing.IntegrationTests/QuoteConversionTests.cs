using System;
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
using static Burkard.Invoicing.IntegrationTests.QuoteTests;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>ConvertQuote: the quote's latest version becomes an ordinary new
/// invoice, which takes a real invoice number — gapless and never deleted — so
/// every refusal must leave the invoices exactly as they were.</summary>
public class QuoteConversionTests
{
    private static Task<HttpResponseMessage> ConvertAsync(HttpClient client, long quoteId) =>
        QuoteActionAsync(client, "ConvertQuote", quoteId);

    private static async Task<(long Id, string Number)> ConvertedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return (
            json.RootElement.GetProperty("Id").GetInt64(),
            json.RootElement.GetProperty("InvoiceNumber").GetString()!
        );
    }

    private static async Task<(long Id, string Number)> CreateInvoiceAsync(
        HttpClient client,
        long recipientId,
        long itemId
    ) =>
        await ConvertedAsync(
            await client.PostAsync(
                "/odata/CreateInvoice",
                ODataJson.Body(new { recipientId, lines = Lines(itemId, 1) })
            )
        );

    [Fact]
    public async Task Converting_creates_an_ordinary_invoice_from_the_latest_version()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 2))
        );
        var (revisionId, _) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 3, 99.50m))
        );

        var response = await ConvertAsync(client, originalId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        long invoiceId;
        using (var json = await ODataJson.ReadAsync(response))
        {
            var root = json.RootElement;
            invoiceId = root.GetProperty("Id").GetInt64();
            Assert.Equal(invoiceId, root.GetProperty("RootId").GetInt64());
            Assert.Equal("2026-0001", root.GetProperty("InvoiceNumber").GetString());
            Assert.Equal(originalId, root.GetProperty("QuoteId").GetInt64());
        }

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db
            .Invoices.Include(i => i.Lines)
            .Include(i => i.Documents)
            .SingleAsync();
        var revision = await db.Quotes.Include(q => q.Lines).SingleAsync(q => q.Id == revisionId);
        var settings = await db.GlobalSettings.SingleAsync();

        Assert.Equal(invoiceId, invoice.Id);
        Assert.Equal(0, invoice.RevisionNumber);
        Assert.True(invoice.IsLatestRevision);
        Assert.Equal(2026, invoice.Year);
        Assert.Equal(1, invoice.Sequence);
        Assert.Equal(originalId, invoice.QuoteId);
        Assert.Null(invoice.GraveId);
        Assert.Equal(host.Now, invoice.GeneratedAt);
        Assert.Equal(recipientId, invoice.RecipientId);
        Assert.Equal("Familie Mahr", invoice.RecipientName);
        Assert.Equal("Musterstraße 1\n12345 Musterstadt", invoice.RecipientAddress);
        Assert.Equal("E-0004", invoice.RecipientNumber);
        Assert.Equal(settings.EndingText, invoice.EndingText);
        Assert.Equal(settings.StampWord, invoice.StampWord);
        Assert.Equal(
            new[] { DocumentKind.Normal, DocumentKind.Copy },
            invoice.Documents.OrderBy(d => d.Kind).Select(d => d.Kind)
        );
        Assert.Equal(
            new[] { "2026-0001.pdf", "2026-0001-Kopie.pdf" }.OrderBy(n => n),
            invoice.Documents.Select(d => d.FileName).OrderBy(n => n)
        );
        Assert.All(invoice.Documents, d => Assert.NotEmpty(d.Content));

        var line = Assert.Single(invoice.Lines);
        var quoted = Assert.Single(revision.Lines);
        Assert.Equal(quoted.ItemId, line.ItemId);
        Assert.Equal(quoted.ItemNumber, line.ItemNumber);
        Assert.Equal(quoted.Description, line.Description);
        Assert.Equal(3, line.Count);
        Assert.Equal(99.50m, line.UnitPrice);
        Assert.Equal(quoted.VatRate, line.VatRate);
        Assert.Equal(quoted.PriceIncludesVat, line.PriceIncludesVat);
        Assert.Equal(1, line.LineNumber);
        Assert.Null(line.GraveUnitPrice);

        var chain = await db.Quotes.Where(q => q.RootId == originalId).ToListAsync();
        Assert.Equal(2, chain.Count);
        Assert.All(
            chain,
            q =>
            {
                Assert.Equal(QuoteState.Converted, q.State);
                Assert.Equal(invoiceId, q.InvoiceId);
            }
        );
    }

    [Fact]
    public async Task Converting_keeps_the_quoted_prices()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var lines = new object[]
        {
            new
            {
                itemId,
                count = 1,
                unitPrice = (decimal?)null,
                priceIncludesVat = false,
            },
            new
            {
                itemId,
                count = 2,
                unitPrice = (decimal?)107.00m,
                priceIncludesVat = true,
            },
        };
        var (quoteId, _) = await CreatedAsync(await CreateQuoteAsync(client, recipientId, lines));
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InvoiceItems.SingleAsync(i => i.Id == itemId);
            item.DefaultPrice = 150.00m;
            item.Description = "Grabpflege neu";
            await db.SaveChangesAsync();
        }

        await ConvertedAsync(await ConvertAsync(client, quoteId));

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await verifyDb.Invoices.Include(i => i.Lines).SingleAsync();
        var invoiceLines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();
        Assert.Equal(2, invoiceLines.Count);

        Assert.Equal(1, invoiceLines[0].LineNumber);
        Assert.Equal("Grabpflege", invoiceLines[0].Description);
        Assert.Equal(120.00m, invoiceLines[0].UnitPrice);
        Assert.False(invoiceLines[0].PriceIncludesVat);

        Assert.Equal(2, invoiceLines[1].LineNumber);
        Assert.Equal("Grabpflege", invoiceLines[1].Description);
        Assert.Equal(2, invoiceLines[1].Count);
        Assert.Equal(100.00m, invoiceLines[1].UnitPrice);
        Assert.True(invoiceLines[1].PriceIncludesVat);
        Assert.Equal(7m, invoiceLines[1].VatRate);
    }

    [Fact]
    public async Task Converting_takes_the_recipient_as_it_is_now()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipient = await db.Recipients.SingleAsync(r => r.Id == recipientId);
            recipient.Name = "Familie Mahr-Kunz";
            await db.SaveChangesAsync();
        }

        await ConvertedAsync(await ConvertAsync(client, quoteId));

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await verifyDb.Invoices.Include(i => i.Documents).SingleAsync();
        Assert.Equal("Familie Mahr-Kunz", invoice.RecipientName);
        var normal = invoice.Documents.Single(d => d.Kind == DocumentKind.Normal);
        using var pdf = PdfDocument.Open(normal.Content);
        Assert.Contains("Familie Mahr-Kunz", pdf.GetPage(1).Text);
        // the quote keeps what it printed
        var quote = await verifyDb.Quotes.SingleAsync(q => q.Id == quoteId);
        Assert.Equal("Familie Mahr", quote.RecipientName);
    }

    [Fact]
    public async Task A_quote_converts_once()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        await ConvertedAsync(await ConvertAsync(client, quoteId));

        Assert.Equal("QuoteNotOpen", await ErrorCodeAsync(await ConvertAsync(client, quoteId)));
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Invoices.CountAsync());
        }

        // two at once: the transaction that allocates the number also reads the
        // state, so the second waits for the first and then finds it converted
        var (raceId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Run(() =>
                        client.PostAsync(
                            "/odata/ConvertQuote",
                            ODataJson.Body(new { quoteId = raceId })
                        )
                    )
                )
        );

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var refused = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created);
        Assert.Equal("QuoteNotOpen", await ErrorCodeAsync(refused));
        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var raced = Assert.Single(
            await verifyDb.Invoices.Where(i => i.QuoteId == raceId).ToListAsync()
        );
        Assert.Equal("2026-0002", raced.InvoiceNumber);
        Assert.Equal(2, await verifyDb.Invoices.CountAsync());
        var raceQuote = await verifyDb.Quotes.SingleAsync(q => q.Id == raceId);
        Assert.Equal(QuoteState.Converted, raceQuote.State);
        Assert.Equal(raced.Id, raceQuote.InvoiceId);
    }

    [Fact]
    public async Task A_declined_quote_cannot_be_converted()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await QuoteActionAsync(client, "DeclineQuote", quoteId)).StatusCode
        );

        Assert.Equal("QuoteNotOpen", await ErrorCodeAsync(await ConvertAsync(client, quoteId)));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Invoices.ToListAsync());
        var quote = await db.Quotes.SingleAsync();
        Assert.Equal(QuoteState.Declined, quote.State);
        Assert.Null(quote.InvoiceId);
    }

    [Fact]
    public async Task A_recipient_without_address_refuses_and_uses_no_number()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        await SetAddressAsync(host, recipientId, "");

        Assert.Equal(
            "RecipientAddressRequired",
            await ErrorCodeAsync(await ConvertAsync(client, quoteId))
        );

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Invoices.ToListAsync());
            Assert.Empty(await db.Documents.ToListAsync());
            var quote = await db.Quotes.SingleAsync();
            Assert.Equal(QuoteState.Open, quote.State);
            Assert.Null(quote.InvoiceId);
        }

        await SetAddressAsync(host, recipientId, "Musterstraße 2\n12345 Musterstadt");
        var (_, number) = await CreateInvoiceAsync(client, recipientId, itemId);
        Assert.Equal("2026-0001", number);
    }

    private static async Task SetAddressAsync(ApiTestHost host, long recipientId, string address)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Recipients.SingleAsync(r => r.Id == recipientId);
        recipient.Address = address;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Last_years_quote_becomes_this_years_invoice()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = await db.Quotes.SingleAsync(q => q.Id == quoteId);
            quote.Year = 2025;
            quote.Sequence = 7;
            quote.QuoteNumber = "A2025-0007";
            await db.SaveChangesAsync();
        }

        var (invoiceId, number) = await ConvertedAsync(await ConvertAsync(client, quoteId));

        Assert.Equal("2026-0001", number);
        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await verifyDb.Invoices.SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(2026, invoice.Year);
        Assert.Equal(1, invoice.Sequence);
    }

    [Fact]
    public async Task The_invoice_of_a_quote_revises_and_pays_as_any_other()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 2))
        );
        var (invoiceId, _) = await ConvertedAsync(await ConvertAsync(client, originalId));
        var before = await QuoteRowsAsync(host, originalId);

        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = Lines(itemId, 5),
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        long revisionId;
        using (var json = await ODataJson.ReadAsync(revised))
        {
            revisionId = json.RootElement.GetProperty("Id").GetInt64();
            Assert.Equal("2026-0001-R1", json.RootElement.GetProperty("InvoiceNumber").GetString());
            Assert.Equal(originalId, json.RootElement.GetProperty("QuoteId").GetInt64());
        }

        var paid = await client.PostAsync(
            "/odata/SetInvoicePaid",
            ODataJson.Body(new { invoiceId = revisionId, paid = true })
        );
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var revision = await db.Invoices.SingleAsync(i => i.Id == revisionId);
            Assert.Equal(originalId, revision.QuoteId);
            Assert.Equal(invoiceId, revision.RootId);
            Assert.NotNull(revision.PaidOn);
        }
        Assert.Equal(before, await QuoteRowsAsync(host, originalId));
    }

    [Fact]
    public async Task Converting_and_declining_at_once_leave_a_consistent_chain()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );

        var responses = await Task.WhenAll(
            Task.Run(() => ConvertAsync(client, quoteId)),
            Task.Run(() => QuoteActionAsync(client, "DeclineQuote", quoteId))
        );

        Assert.Contains(
            responses,
            r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK
        );
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = await db.Quotes.Where(q => q.RootId == quoteId).ToListAsync();
        var invoices = await db.Invoices.Where(i => i.QuoteId == quoteId).ToListAsync();
        Assert.Single(chain.Select(q => q.State).Distinct());
        if (chain[0].State == QuoteState.Converted)
        {
            var invoice = Assert.Single(invoices);
            Assert.All(chain, q => Assert.Equal(invoice.Id, q.InvoiceId));
        }
        else
        {
            Assert.Equal(QuoteState.Declined, chain[0].State);
            Assert.All(chain, q => Assert.Null(q.InvoiceId));
            Assert.Empty(invoices);
            Assert.Equal(0, await db.Invoices.CountAsync());
        }
    }

    [Fact]
    public async Task The_invoice_documents_do_not_mention_the_quote()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, quoteNumber) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        await ConvertedAsync(await ConvertAsync(client, quoteId));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.Include(i => i.Documents).SingleAsync();
        foreach (var document in invoice.Documents)
        {
            using var pdf = PdfDocument.Open(document.Content);
            var text = string.Concat(pdf.GetPages().Select(p => p.Text));
            Assert.Contains("2026-0001", text);
            Assert.DoesNotContain("ANGEBOT", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(quoteNumber, text);
        }
    }

    [Fact]
    public async Task Converting_through_a_revisions_id_names_the_original_quote()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (revisionId, _) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 2))
        );

        var (invoiceId, _) = await ConvertedAsync(await ConvertAsync(client, revisionId));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(originalId, invoice.QuoteId);
        var chain = await db.Quotes.Where(q => q.RootId == originalId).ToListAsync();
        Assert.All(chain, q => Assert.Equal(invoiceId, q.InvoiceId));
    }

    private static async Task<string> QuoteRowsAsync(ApiTestHost host, long rootId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db
            .Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => q.RootId == rootId)
            .OrderBy(q => q.Id)
            .Select(q => new
            {
                q.Id,
                q.QuoteNumber,
                q.State,
                q.InvoiceId,
                q.IsLatestRevision,
                q.GeneratedAt,
                Lines = q
                    .Lines.OrderBy(l => l.LineNumber)
                    .Select(l => new { l.Count, l.UnitPrice }),
            })
            .ToListAsync();
        return JsonSerializer.Serialize(rows);
    }
}
