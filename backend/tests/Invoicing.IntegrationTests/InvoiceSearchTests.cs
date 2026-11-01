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
}
