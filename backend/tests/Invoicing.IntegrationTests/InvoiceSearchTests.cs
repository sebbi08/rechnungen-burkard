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
    private static InvoiceSearch.Row Row(
        string number = "2026-0001",
        string name = "Familie Müller",
        string address = "Hauptstraße 1\n12345 Musterstadt",
        decimal gross = 214.00m
    ) =>
        new(1, number, name, address, new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc), gross);

    [Theory]
    [InlineData("0001")]
    [InlineData("2026-0001")]
    public void Matches_the_invoice_number(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Theory]
    [InlineData("müller")]
    [InlineData("MÜLLER")]
    [InlineData("Müller")]
    public void Matches_the_recipient_name_regardless_of_case_including_umlauts(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Fact]
    public void Matches_the_recipient_address() =>
        Assert.True(InvoiceSearch.Matches(Row(), "hauptstraße"));

    [Theory]
    [InlineData("14.03.2026")]
    [InlineData("03.2026")]
    public void Matches_the_date_as_it_is_displayed(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Theory]
    [InlineData("214.00")]
    [InlineData("214,00")]
    [InlineData("214")]
    public void Matches_the_total_with_either_decimal_separator(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Fact]
    public void Does_not_match_an_unrelated_term() =>
        Assert.False(InvoiceSearch.Matches(Row(), "Schmidt"));

    [Theory]
    [InlineData("Hanna Bauer")]
    [InlineData("bauer hanna")]
    [InlineData("  Hanna   Bauer ")]
    public void Matches_the_words_of_a_term_in_any_order(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(name: "Bauer Hanna"), term));

    [Theory]
    [InlineData("Müller Hauptstraße")]
    [InlineData("musterstadt 0001 müller")]
    [InlineData("Müller 214,00 14.03.2026")]
    public void Matches_words_that_are_found_in_different_fields(string term) =>
        Assert.True(InvoiceSearch.Matches(Row(), term));

    [Fact]
    public void Does_not_match_when_one_word_of_the_term_is_found_nowhere() =>
        Assert.False(InvoiceSearch.Matches(Row(), "Müller Schmidt"));

    [Fact]
    public void Gross_total_rounds_each_line_the_way_the_document_does()
    {
        var total = InvoiceSearch.GrossTotal(new[] { (2, 100m, 7m) });
        Assert.Equal(214.00m, total);
    }

    [Fact]
    public async Task The_search_query_option_narrows_the_entity_set()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var mueller = new Recipient
        {
            Number = "E-0001",
            Name = "Familie Müller",
            Address = "Hauptstraße 1",
        };
        var schmidt = new Recipient
        {
            Number = "E-0002",
            Name = "Familie Schmidt",
            Address = "Nebenweg 2",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(mueller, schmidt);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
        foreach (var recipient in new[] { mueller, schmidt })
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
                                count = 2,
                                unitPrice = (decimal?)null,
                            },
                        },
                    }
                )
            );
            created.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync("/odata/Invoices?search=m%C3%BCller");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var names = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(i => i.GetProperty("RecipientName").GetString())
            .ToList();
        Assert.Equal(new[] { "Familie Müller" }, names);
    }

    [Fact]
    public async Task The_search_query_option_composes_with_filter_and_top()
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
            "/odata/Invoices?search=Beispiel&$filter=GraveId eq null&$orderby=Id desc&$top=2"
        );
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(2, json.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task An_empty_search_returns_everything()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4003",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0004",
            Name = "Familie Beispiel",
            Address = "Weg 4",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = host.CreateClient();
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

        var response = await client.GetAsync("/odata/Invoices?search=");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(1, json.RootElement.GetProperty("value").GetArrayLength());
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
    [InlineData("1234,56", "2026-0001")]
    [InlineData("1234.56", "2026-0001")]
    [InlineData("12,5", "2026-0002")]
    public async Task Total_matches_with_comma_or_point(string term, string expected)
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            ("2026-0001", "A", "x", May, 1234.56m),
            ("2026-0002", "B", "y", May, 12.50m)
        );
        Assert.Equal(new[] { expected }, await NumbersAsync(host, "total=" + term));
    }

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
