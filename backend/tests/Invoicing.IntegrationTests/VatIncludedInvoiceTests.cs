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

/// <summary>A line entered VAT-included on an ordinary invoice is gross-led: the
/// entered gross is stored, and the line bills exactly count times that gross,
/// with the VAT taken out of it. Lines from before that keep their old
/// figures.</summary>
public class VatIncludedInvoiceTests
{
    private sealed record Seed(
        long GrossItemId,
        long NetItemId,
        long SeventyFiveId,
        long ZeroRateId,
        long RecipientId
    );

    private static async Task<Seed> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.AddRange(new VatRate { Rate = 19m }, new VatRate { Rate = 0m });
        var gross = new InvoiceItem
        {
            Number = "V-120",
            Description = "Grabpflege inklusive",
            VatRate = 19m,
            DefaultPrice = 120.00m,
        };
        var net = new InvoiceItem
        {
            Number = "V-100",
            Description = "Grabpflege netto",
            VatRate = 19m,
            DefaultPrice = 100.00m,
        };
        var seventyFive = new InvoiceItem
        {
            Number = "V-75",
            Description = "Grabpflege 75",
            VatRate = 19m,
            DefaultPrice = 75.00m,
        };
        var zero = new InvoiceItem
        {
            Number = "V-0",
            Description = "Ohne Mehrwertsteuer",
            VatRate = 0m,
            DefaultPrice = 50.00m,
        };
        var recipient = new Recipient
        {
            Number = "E-9001",
            Name = "Familie Brutto",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.AddRange(gross, net, seventyFive, zero);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return new Seed(gross.Id, net.Id, seventyFive.Id, zero.Id, recipient.Id);
    }

    private static async Task<Invoice> LoadAsync(ApiTestHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == id);
    }

    private static object Line(long itemId, int count, decimal? unitPrice, bool priceIncludesVat) =>
        new
        {
            itemId,
            count,
            unitPrice,
            priceIncludesVat,
        };

    private static async Task<long> CreateAsync(
        HttpClient client,
        long recipientId,
        params object[] lines
    )
    {
        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(new { recipientId, lines })
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    /// <summary>The totals the invoice list stamps.</summary>
    private static async Task<(decimal Net, decimal Vat, decimal Gross)> ListTotalsAsync(
        HttpClient client,
        long invoiceId
    )
    {
        var response = await client.GetAsync($"/odata/Invoices?$filter=Id eq {invoiceId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var row = json.RootElement.GetProperty("value").EnumerateArray().Single();
        return (
            row.GetProperty("NetTotal").GetDecimal(),
            row.GetProperty("VatTotal").GetDecimal(),
            row.GetProperty("GrossTotal").GetDecimal()
        );
    }

    [Fact]
    public async Task A_ticked_line_bills_the_entered_gross_exactly()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.SeventyFiveId, 1, 75.00m, true)
        );

        var line = Assert.Single((await LoadAsync(host, id)).Lines);
        Assert.Equal(75.00m, line.GrossUnitPrice);
        Assert.Equal(63.03m, line.UnitPrice);
        Assert.True(line.PriceIncludesVat);
        var (net, vat, gross) = await ListTotalsAsync(client, id);
        Assert.Equal(75.00m, gross);
        Assert.Equal(11.97m, vat);
        Assert.Equal(63.03m, net);
    }

    [Fact]
    public async Task A_count_above_one_bills_count_times_the_gross()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.GrossItemId, 3, 10.00m, true)
        );

        var (net, vat, gross) = await ListTotalsAsync(client, id);
        Assert.Equal(30.00m, gross);
        Assert.Equal(4.79m, vat);
        Assert.Equal(25.21m, net);
    }

    [Fact]
    public async Task An_untyped_ticked_line_reads_the_default_as_gross()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.SeventyFiveId, 1, null, true)
        );

        Assert.Equal(75.00m, Assert.Single((await LoadAsync(host, id)).Lines).GrossUnitPrice);
    }

    [Fact]
    public async Task An_unticked_line_stores_no_gross()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(client, seed.RecipientId, Line(seed.NetItemId, 1, null, false));

        var line = Assert.Single((await LoadAsync(host, id)).Lines);
        Assert.Null(line.GrossUnitPrice);
        Assert.Equal(100.00m, line.UnitPrice);
    }

    [Fact]
    public async Task A_gross_line_and_a_net_line_at_one_rate_add_up()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.SeventyFiveId, 1, 75.00m, true),
            Line(seed.NetItemId, 1, 100.00m, false)
        );

        var (net, vat, gross) = await ListTotalsAsync(client, id);
        Assert.Equal(163.03m, net);
        Assert.Equal(30.97m, vat);
        Assert.Equal(194.00m, gross);
    }

    [Fact]
    public async Task A_rate_of_zero_takes_no_vat_out()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var id = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.ZeroRateId, 1, 50.00m, true)
        );

        var (net, vat, gross) = await ListTotalsAsync(client, id);
        Assert.Equal(50.00m, net);
        Assert.Equal(0m, vat);
        Assert.Equal(50.00m, gross);
    }

    [Fact]
    public async Task A_revision_that_returns_the_gross_keeps_it()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateAsync(
            client,
            seed.RecipientId,
            Line(seed.SeventyFiveId, 1, 75.00m, true)
        );

        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId = seed.RecipientId,
                    lines = new[] { Line(seed.SeventyFiveId, 1, 75.00m, true) },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        using var revisedJson = await ODataJson.ReadAsync(revised);
        var revisionId = revisedJson.RootElement.GetProperty("Id").GetInt64();

        Assert.Equal(
            75.00m,
            Assert.Single((await LoadAsync(host, revisionId)).Lines).GrossUnitPrice
        );
        var (net, vat, gross) = await ListTotalsAsync(client, revisionId);
        Assert.Equal((63.03m, 11.97m, 75.00m), (net, vat, gross));
    }

    [Fact]
    public async Task A_flagged_line_from_before_keeps_its_old_figures()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        long invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = new Invoice
            {
                Year = 2026,
                Sequence = 1,
                InvoiceNumber = "2026-0001",
                RecipientId = seed.RecipientId,
                RecipientName = "Familie Brutto",
                RecipientAddress = "Musterstraße 1",
                RecipientNumber = "E-9001",
                GeneratedAt = host.Now,
                Lines =
                {
                    new InvoiceLine
                    {
                        ItemId = seed.GrossItemId,
                        ItemNumber = "V-120",
                        Description = "Grabpflege inklusive",
                        Count = 3,
                        UnitPrice = 63.03m,
                        VatRate = 19m,
                        PriceIncludesVat = true,
                        GrossUnitPrice = null,
                        LineNumber = 1,
                    },
                },
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoice.RootId = invoice.Id;
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var (net, vat, gross) = await ListTotalsAsync(client, invoiceId);
        Assert.Equal(189.09m, net);
        Assert.Equal(35.93m, vat);
        Assert.Equal(225.02m, gross);
    }

    [Fact]
    public async Task A_third_decimal_on_a_gross_is_refused()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId = seed.RecipientId,
                    lines = new[] { Line(seed.SeventyFiveId, 1, 75.005m, true) },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidPrice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
}
