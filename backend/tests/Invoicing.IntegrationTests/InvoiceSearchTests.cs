using System;
using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceSearchTests
{
    [Fact]
    public void Gross_total_rounds_each_line_the_way_the_document_does()
    {
        var total = InvoiceSearch.GrossTotal(new[] { (2, 100m, 7m) });
        Assert.Equal(214.00m, total);
    }

    [Fact]
    public async Task The_column_options_compose_with_filter_and_top()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4002",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0003",
            Name = "Familie Beispiel",
            Address = "Weg 3",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostAsync(
                "/odata/CreateInvoice",
                ODataJson.Body(
                    new
                    {
                        recipientId = recipient.Id,
                        lines = new[]
                        {
                            new
                            {
                                itemId = item.Id,
                                count = 1,
                                unitPrice = (decimal?)null,
                            },
                        },
                    }
                )
            );
            created.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync(
            "/odata/Invoices?recipient=Beispiel&$filter=GraveId eq null&$orderby=Id desc&$top=2"
        );
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(2, json.RootElement.GetProperty("value").GetArrayLength());
    }

    private static async Task<long[]> SeedAsync(
        ApiTestHost host,
        params (string Number, string Name, string Address, DateTime Date, decimal Net)[] invoices
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeded = new System.Collections.Generic.List<Invoice>();
        var n = 0;
        foreach (var (number, name, address, date, net) in invoices)
        {
            var recipient = new Recipient
            {
                Number = $"C-{++n:0000}",
                Name = name,
                Address = address,
            };
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            var invoice = new Invoice
            {
                InvoiceNumber = number,
                Year = date.Year,
                Sequence = n,
                RecipientId = recipient.Id,
                RecipientName = name,
                RecipientAddress = address,
                GeneratedAt = date,
                Lines =
                {
                    new InvoiceLine
                    {
                        Description = "Grabpflege",
                        Count = 1,
                        UnitPrice = net,
                        VatRate = 0m,
                        LineNumber = 1,
                    },
                },
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoice.RootId = invoice.Id;
            await db.SaveChangesAsync();
            seeded.Add(invoice);
        }

        return seeded.Select(i => i.Id).ToArray();
    }

    private static async Task<string[]> NumbersAsync(ApiTestHost host, string query)
    {
        using var client = host.CreateClient();
        var response = await client.GetAsync("/odata/Invoices?" + query);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(i => i.GetProperty("InvoiceNumber").GetString()!)
            .OrderBy(x => x)
            .ToArray();
    }

    private static readonly DateTime May = new(2025, 5, 19, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime June = new(2025, 6, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Number_matches_the_number_only()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0007", "Hanna Bauer", "Ahornweg 1", May, 10m),
            ("2026-0008", "0007er", "Ahornweg 2", May, 10m)
        );
        Assert.Equal(new[] { "2026-0007" }, await NumbersAsync(host, "number=0007"));
    }

    [Fact]
    public async Task Recipient_matches_name_and_address_word_by_word()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "Hanna Bauer", "Birkenallee 1", May, 10m),
            ("2026-0002", "Karl Lang", "Ahornweg 2", May, 10m)
        );
        Assert.Equal(new[] { "2026-0001" }, await NumbersAsync(host, "recipient=Bauer%20Hanna"));
        Assert.Equal(new[] { "2026-0002" }, await NumbersAsync(host, "recipient=Ahornweg"));
    }

    [Fact]
    public async Task Date_matches_the_shown_date()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "A", "x", May, 10m),
            ("2026-0002", "B", "y", June, 10m)
        );
        Assert.Equal(new[] { "2026-0001" }, await NumbersAsync(host, "date=05.2025"));
    }

    [Theory]
    [InlineData("1.234", "2026-0001")]
    [InlineData("1.234,56 €", "2026-0001")]
    [InlineData("1.234,56€", "2026-0001")]
    [InlineData("1.234,56", "2026-0001")]
    [InlineData("1234,56", "2026-0001")]
    [InlineData("1234.56", "2026-0001")]
    [InlineData("12,50 €", "2026-0002")]
    [InlineData("12.50", "2026-0002")]
    [InlineData("12,5", "2026-0002")]
    public async Task Total_matches_as_shown_or_as_typed(string term, string expected)
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "A", "x", May, 1234.56m),
            ("2026-0002", "B", "y", May, 12.50m)
        );
        Assert.Equal(
            new[] { expected },
            await NumbersAsync(host, "total=" + Uri.EscapeDataString(term))
        );
    }

    [Theory]
    [InlineData("1.234,56 €", 1234.56, true)]
    [InlineData("1234,56", 1234.56, true)]
    [InlineData("1234.56", 1234.56, true)]
    [InlineData("12.50", 12.50, true)]
    [InlineData("12,5", 12.50, true)]
    [InlineData("€", 12.50, true)]
    [InlineData("12,50 €", 1234.56, false)]
    [InlineData("1234,56", 12.50, false)]
    public void InAmount_reads_the_shown_and_the_typed_forms(
        string term,
        double amount,
        bool expected
    ) => Assert.Equal(expected, SearchWords.InAmount(term, (decimal)amount));

    [Fact]
    public async Task Columns_combine_with_and()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "Hanna Bauer", "x", May, 10m),
            ("2026-0002", "Hanna Bauer", "x", May, 10m),
            ("2026-0003", "Karl Lang", "x", May, 10m)
        );
        Assert.Equal(
            new[] { "2026-0002" },
            await NumbersAsync(host, "number=0002&recipient=Bauer")
        );
    }

    [Fact]
    public async Task Terms_of_only_spaces_filter_nothing()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, ("2026-0001", "A", "x", May, 10m), ("2026-0002", "B", "y", May, 10m));
        Assert.Equal(
            new[] { "2026-0001", "2026-0002" },
            await NumbersAsync(host, "recipient=%20%20")
        );
    }

    [Fact]
    public async Task Special_characters_arrive_intact()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "O'Brien & Söhne", "x", May, 10m),
            ("2026-0002", "B", "y", May, 10m)
        );
        Assert.Equal(
            new[] { "2026-0001" },
            await NumbersAsync(host, "recipient=O'Brien%20%26%20s%C3%B6hne")
        );
    }

    [Theory]
    [InlineData("a b", true)]
    [InlineData("a z", false)]
    [InlineData("  ", true)]
    [InlineData(null, true)]
    public void InAny_needs_every_word_in_some_value(string? term, bool expected) =>
        Assert.Equal(expected, SearchWords.InAny(term, "A", null, "b"));
}
