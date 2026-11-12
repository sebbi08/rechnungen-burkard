using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A free text above the lines and a comment on each line, entered
/// with an ordinary invoice or a quote. Both are kept with the version they
/// were entered for, travel from a quote's latest version to its invoice, and
/// never reach a grave invoice.</summary>
public class CommentTests
{
    private const string Intro = "Arbeiten im Garten\nvom 3. bis 5. März";
    private const string Comment = "Hecke an der Straßenseite\nSchnittgut entsorgt";

    private static object Lines(long itemId, string? firstComment) =>
        new object[]
        {
            new
            {
                itemId,
                count = 1,
                unitPrice = (decimal?)null,
                comment = firstComment,
            },
            new
            {
                itemId,
                count = 2,
                unitPrice = (decimal?)null,
            },
        };

    private static async Task<long> CreateInvoiceAsync(
        HttpClient client,
        long recipientId,
        string? introText,
        object lines
    )
    {
        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    introText,
                    lines,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<Invoice> InvoiceAsync(ApiTestHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Invoices.AsNoTracking()
            .Include(i => i.Lines.OrderBy(l => l.LineNumber))
            .Include(i => i.Documents)
            .SingleAsync(i => i.Id == id);
    }

    private static async Task<Quote> QuoteAsync(ApiTestHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .Quotes.AsNoTracking()
            .Include(q => q.Lines.OrderBy(l => l.LineNumber))
            .Include(q => q.Document)
            .SingleAsync(q => q.Id == id);
    }

    private static string PdfText(byte[] content)
    {
        using var pdf = PdfDocument.Open(content);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task An_invoice_keeps_its_free_text_and_line_comments_and_prints_them()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateInvoiceAsync(client, recipientId, Intro, Lines(itemId, Comment));

        var invoice = await InvoiceAsync(host, id);
        Assert.Equal(Intro, invoice.IntroText);
        Assert.Equal(new[] { Comment, "" }, invoice.Lines.Select(l => l.Comment));
        Assert.Equal(2, invoice.Documents.Count);
        foreach (var document in invoice.Documents)
        {
            var text = PdfText(document.Content);
            Assert.Contains("Arbeiten im Garten", text);
            Assert.Contains("vom 3. bis 5. März", text);
            Assert.Contains("Hecke an der Straßenseite", text);
            Assert.Contains("Schnittgut entsorgt", text);
        }
    }

    [Fact]
    public async Task Texts_are_optional_and_blank_ones_are_kept_empty()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();

        var without = await CreateInvoiceAsync(
            client,
            recipientId,
            null,
            QuoteTests.Lines(itemId, 1)
        );
        var blank = await CreateInvoiceAsync(client, recipientId, "  \n ", Lines(itemId, " \n"));
        var padded = await CreateInvoiceAsync(
            client,
            recipientId,
            "\n Vorspann \n",
            Lines(itemId, "  Notiz\n")
        );

        Assert.Equal("", (await InvoiceAsync(host, without)).IntroText);
        Assert.Equal("", (await InvoiceAsync(host, without)).Lines.Single().Comment);
        Assert.Equal("", (await InvoiceAsync(host, blank)).IntroText);
        Assert.Equal("", (await InvoiceAsync(host, blank)).Lines[0].Comment);
        Assert.Equal("Vorspann", (await InvoiceAsync(host, padded)).IntroText);
        Assert.Equal("Notiz", (await InvoiceAsync(host, padded)).Lines[0].Comment);
    }

    [Fact]
    public async Task A_revision_takes_the_texts_entered_for_it()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();
        var id = await CreateInvoiceAsync(client, recipientId, Intro, Lines(itemId, Comment));

        var response = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = id,
                    recipientId,
                    introText = "Neuer Vorspann",
                    lines = Lines(itemId, "Neuer Kommentar"),
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var revision = await InvoiceAsync(host, json.RootElement.GetProperty("Id").GetInt64());
        var original = await InvoiceAsync(host, id);

        Assert.Equal("Neuer Vorspann", revision.IntroText);
        Assert.Equal("Neuer Kommentar", revision.Lines[0].Comment);
        Assert.Equal(Intro, original.IntroText);
        Assert.Equal(Comment, original.Lines[0].Comment);
    }

    [Fact]
    public async Task A_quote_keeps_its_texts_per_version_and_its_invoice_takes_the_latest()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await QuoteTests.SeedAsync(host);
        using var client = host.CreateClient();

        var (quoteId, _) = await QuoteTests.CreatedAsync(
            await client.PostAsync(
                "/odata/CreateQuote",
                ODataJson.Body(
                    new
                    {
                        recipientId,
                        introText = "Erster Vorspann",
                        lines = Lines(itemId, "Erster Kommentar"),
                    }
                )
            )
        );
        var (revisionId, _) = await QuoteTests.CreatedAsync(
            await client.PostAsync(
                "/odata/CreateQuoteRevision",
                ODataJson.Body(
                    new
                    {
                        quoteId,
                        recipientId,
                        introText = Intro,
                        lines = Lines(itemId, Comment),
                    }
                )
            )
        );

        var first = await QuoteAsync(host, quoteId);
        var latest = await QuoteAsync(host, revisionId);
        Assert.Equal("Erster Vorspann", first.IntroText);
        Assert.Equal("Erster Kommentar", first.Lines[0].Comment);
        Assert.Equal(Intro, latest.IntroText);
        Assert.Equal(new[] { Comment, "" }, latest.Lines.Select(l => l.Comment));
        var quoteText = PdfText(latest.Document!.Content);
        Assert.Contains("vom 3. bis 5. März", quoteText);
        Assert.Contains("Schnittgut entsorgt", quoteText);

        var converted = await client.PostAsync(
            "/odata/ConvertQuote",
            ODataJson.Body(new { quoteId })
        );
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        using var json = await ODataJson.ReadAsync(converted);
        var invoice = await InvoiceAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        Assert.Equal(Intro, invoice.IntroText);
        Assert.Equal(new[] { Comment, "" }, invoice.Lines.Select(l => l.Comment));
        Assert.Contains("Schnittgut entsorgt", PdfText(invoice.Documents.First().Content));
    }

    [Fact]
    public async Task A_grave_invoice_takes_neither_text()
    {
        using var host = new ApiTestHost();
        var itemId = await EndingTextTests.SeedGraveAsync(host);
        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        var generated = await EndingTextTests.GraveInvoiceAsync(host, 1);

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = generated.Id,
                    introText = Intro,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)100m,
                            comment = Comment,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var revision = await InvoiceAsync(
            host,
            (await EndingTextTests.GraveInvoiceAsync(host, 1)).Id
        );
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("", revision.IntroText);
        Assert.All(revision.Lines, l => Assert.Equal("", l.Comment));
        Assert.DoesNotContain("Schnittgut", PdfText(revision.Documents.First().Content));
    }
}
