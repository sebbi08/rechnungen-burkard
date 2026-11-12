using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;
using static Burkard.Invoicing.IntegrationTests.QuoteTests;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>GET odata/Quotes: one row per version, stamped with the totals the
/// PDF prints, searchable per column like the invoice list, and never loading
/// a document's bytes.</summary>
public class QuoteListTests
{
    private static async Task<JsonDocument> GetAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/odata/Quotes?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ODataJson.ReadAsync(response);
    }

    private static async Task<string[]> NumbersAsync(HttpClient client, string query)
    {
        using var json = await GetAsync(client, query);
        return json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(q => q.GetProperty("QuoteNumber").GetString()!)
            .OrderBy(x => x)
            .ToArray();
    }

    private static JsonValueKind KindOf(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) ? value.ValueKind : JsonValueKind.Null;

    [Fact]
    public async Task The_list_shows_each_quote_once_with_its_totals()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (revisionId, revisionNumber) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 3))
        );

        using var json = await GetAsync(client, "$filter=IsLatestRevision eq true");

        var row = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
        Assert.Equal(revisionId, row.GetProperty("Id").GetInt64());
        Assert.Equal(revisionNumber, row.GetProperty("QuoteNumber").GetString());
        Assert.Equal(originalId, row.GetProperty("RootId").GetInt64());
        Assert.Equal("Open", row.GetProperty("State").GetString());
        // 3 x 120,00 net at 7 %
        Assert.Equal(360.00m, row.GetProperty("NetTotal").GetDecimal());
        Assert.Equal(25.20m, row.GetProperty("VatTotal").GetDecimal());
        Assert.Equal(385.20m, row.GetProperty("GrossTotal").GetDecimal());
        Assert.Equal(JsonValueKind.Null, KindOf(row, "InvoiceNumber"));

        // the figure on the PDF
        var pdfBytes = await (
            await client.GetAsync($"/odata/Quotes({revisionId})/Pdf")
        ).Content.ReadAsByteArrayAsync();
        using var pdf = PdfDocument.Open(pdfBytes);
        Assert.Contains("385,20", string.Concat(pdf.GetPages().Select(p => p.Text)));

        // every version is listed without the filter
        Assert.Equal(2, (await NumbersAsync(client, "")).Length);
    }

    [Fact]
    public async Task The_list_expands_lines_and_serves_a_quotes_own_lines()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (id, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 2))
        );

        using (var json = await GetAsync(client, "$expand=Lines"))
        {
            var row = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
            var line = Assert.Single(row.GetProperty("Lines").EnumerateArray());
            Assert.Equal(2, line.GetProperty("Count").GetInt32());
        }

        var lines = await client.GetAsync($"/odata/Quotes({id})/Lines");
        Assert.Equal(HttpStatusCode.OK, lines.StatusCode);
        using (var json = await ODataJson.ReadAsync(lines))
        {
            Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
        }

        var one = await client.GetAsync($"/odata/Quotes({id})");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        using (var json = await ODataJson.ReadAsync(one))
        {
            Assert.Equal(id, json.RootElement.GetProperty("Id").GetInt64());
            Assert.Equal(256.80m, json.RootElement.GetProperty("GrossTotal").GetDecimal());
        }
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/odata/Quotes({id + 1000})")).StatusCode
        );
        // the PDF route is not swallowed by the entity set's convention routes
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/odata/Quotes({id})/Pdf")).StatusCode
        );
    }

    private static async Task SeedChainsAsync(
        ApiTestHost host,
        params (string Name, string Address, DateTime Date, decimal Net)[] quotes
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = 0;
        foreach (var (name, address, date, net) in quotes)
        {
            n++;
            var quote = new Quote
            {
                Year = date.Year,
                Sequence = n,
                QuoteNumber = $"A{date.Year}-{n:0000}",
                RecipientName = name,
                RecipientAddress = address,
                GeneratedAt = date,
                Lines =
                {
                    new QuoteLine
                    {
                        Description = "Grabpflege",
                        Count = 1,
                        UnitPrice = net,
                        VatRate = 0m,
                        LineNumber = 1,
                    },
                },
            };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            quote.RootId = quote.Id;
            await db.SaveChangesAsync();
        }
    }

    private static readonly DateTime May = new(2025, 5, 19, 10, 0, 0);
    private static readonly DateTime June = new(2025, 6, 19, 10, 0, 0);

    [Fact]
    public async Task The_list_searches_each_column()
    {
        using var host = new ApiTestHost();
        await SeedChainsAsync(
            host,
            ("Hanna Bauer", "Birkenallee 1", May, 1234.56m),
            ("Karl Öster", "Ahornweg 2", June, 12.50m)
        );
        using var client = host.CreateClient();

        Assert.Equal(new[] { "A2025-0002" }, await NumbersAsync(client, "number=0002"));
        Assert.Equal(new[] { "A2025-0001" }, await NumbersAsync(client, "recipient=Bauer%20Hanna"));
        Assert.Equal(
            new[] { "A2025-0002" },
            await NumbersAsync(client, "recipient=" + Uri.EscapeDataString("öster karl"))
        );
        Assert.Equal(new[] { "A2025-0002" }, await NumbersAsync(client, "recipient=Ahornweg"));
        Assert.Equal(new[] { "A2025-0001" }, await NumbersAsync(client, "date=19.05.2025"));
        foreach (var term in new[] { "1234,56", "1.234,56 €", "1234.56" })
        {
            Assert.Equal(
                new[] { "A2025-0001" },
                await NumbersAsync(client, "total=" + Uri.EscapeDataString(term))
            );
        }
        // columns combine with AND, and with $filter and paging
        Assert.Empty(await NumbersAsync(client, "number=0001&recipient=Öster"));
        Assert.Equal(
            new[] { "A2025-0002" },
            await NumbersAsync(client, "recipient=Karl&$filter=IsLatestRevision eq true&$top=5")
        );
    }

    [Fact]
    public async Task A_base_number_finds_the_revision()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (originalId, number) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (_, revisionNumber) = await CreatedAsync(
            await ReviseQuoteAsync(client, originalId, recipientId, Lines(itemId, 2))
        );

        Assert.Equal(
            new[] { number, revisionNumber }.OrderBy(x => x),
            await NumbersAsync(client, "number=" + number)
        );
        Assert.Equal(
            new[] { revisionNumber },
            await NumbersAsync(client, "number=R1&$filter=IsLatestRevision eq true")
        );
    }

    [Theory]
    [InlineData("State eq 'Open'", "Open")]
    [InlineData("State eq 'Declined'", "Declined")]
    [InlineData("State eq 'Converted'", "Converted")]
    [InlineData("State eq Burkard.Invoicing.Domain.Entities.QuoteState'Declined'", "Declined")]
    public async Task The_list_filters_by_state(string filter, string state)
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (openId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (declinedId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        var (convertedId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await QuoteActionAsync(client, "DeclineQuote", declinedId)).StatusCode
        );
        var converted = await QuoteActionAsync(client, "ConvertQuote", convertedId);
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        string invoiceNumber;
        using (var json = await ODataJson.ReadAsync(converted))
        {
            invoiceNumber = json.RootElement.GetProperty("InvoiceNumber").GetString()!;
        }

        using var list = await GetAsync(client, "$filter=" + Uri.EscapeDataString(filter));

        var row = Assert.Single(list.RootElement.GetProperty("value").EnumerateArray());
        Assert.Equal(
            state switch
            {
                "Open" => openId,
                "Declined" => declinedId,
                _ => convertedId,
            },
            row.GetProperty("Id").GetInt64()
        );
        Assert.Equal(state, row.GetProperty("State").GetString());
        if (state == "Converted")
        {
            Assert.Equal(invoiceNumber, row.GetProperty("InvoiceNumber").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, KindOf(row, "InvoiceNumber"));
        }
    }

    [Fact]
    public async Task A_converted_quote_names_the_original_invoice_number_after_a_revision()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );
        long invoiceId;
        using (
            var json = await ODataJson.ReadAsync(
                await QuoteActionAsync(client, "ConvertQuote", quoteId)
            )
        )
        {
            invoiceId = json.RootElement.GetProperty("Id").GetInt64();
        }
        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = Lines(itemId, 2),
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);

        using var list = await GetAsync(client, "");

        var row = Assert.Single(list.RootElement.GetProperty("value").EnumerateArray());
        Assert.Equal("2026-0001", row.GetProperty("InvoiceNumber").GetString());
    }

    [Fact]
    public async Task Quotes_stay_out_of_every_invoice_read()
    {
        // no invoice in the host, so no id can coincide with a quote's
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var (quoteId, _) = await CreatedAsync(
            await CreateQuoteAsync(client, recipientId, Lines(itemId, 1))
        );

        using (var json = await ODataJson.ReadAsync(await client.GetAsync("/odata/Invoices")))
        {
            Assert.Empty(json.RootElement.GetProperty("value").EnumerateArray());
        }
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/odata/Invoices({quoteId})")).StatusCode
        );
        Assert.Equal(
            "InvoiceNotFound",
            await ErrorCodeAsync(
                await client.PostAsync(
                    "/odata/SetInvoicePaid",
                    ODataJson.Body(new { invoiceId = quoteId, paid = true })
                )
            )
        );
        Assert.Equal(
            "InvoiceNotFound",
            await ErrorCodeAsync(
                await client.PostAsync(
                    "/odata/CreateRevision",
                    ODataJson.Body(
                        new
                        {
                            invoiceId = quoteId,
                            recipientId,
                            lines = Lines(itemId, 1),
                        }
                    )
                )
            )
        );
    }
}
