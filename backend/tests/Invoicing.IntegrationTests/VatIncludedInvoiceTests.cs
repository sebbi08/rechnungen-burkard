using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>An item whose price includes VAT, on an ordinary invoice: the line
/// is stored net, and the document totals to the price that was entered.</summary>
public class VatIncludedInvoiceTests
{
    private static async Task<(long GrossItemId, long NetItemId, long RecipientId)> SeedAsync(
        ApiTestHost host
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var gross = new InvoiceItem
        {
            Number = "V-120",
            Description = "Grabpflege inklusive",
            VatRate = 19m,
            DefaultPrice = 120.00m,
            PriceIncludesVat = true,
        };
        var net = new InvoiceItem
        {
            Number = "V-100",
            Description = "Grabpflege netto",
            VatRate = 19m,
            DefaultPrice = 100.00m,
        };
        var recipient = new Recipient
        {
            Number = "E-9001",
            Name = "Familie Brutto",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.AddRange(gross, net);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (gross.Id, net.Id, recipient.Id);
    }

    private static async Task<Invoice> LoadAsync(ApiTestHost host, long id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == id);
    }

    [Fact]
    public async Task The_default_price_is_billed_net_and_totals_to_the_entered_gross()
    {
        using var host = new ApiTestHost();
        var (grossItemId, _, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var invoice = await LoadAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(100.840m, line.UnitPrice);

        var data = new DocumentDataBuilder().Build(invoice);
        Assert.Equal(120.00m, data.GrossTotal);
        Assert.Equal(100.84m, data.NetTotal);
    }

    [Fact]
    public async Task Only_the_flagged_line_gives_up_its_vat()
    {
        using var host = new ApiTestHost();
        var (grossItemId, netItemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                        new
                        {
                            itemId = netItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var invoice = await LoadAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        Assert.Equal(100.840m, invoice.Lines.Single(l => l.ItemNumber == "V-120").UnitPrice);
        Assert.Equal(100.00m, invoice.Lines.Single(l => l.ItemNumber == "V-100").UnitPrice);
    }

    /// <summary>The case the 3-decimal net exists for: at a count above one, a
    /// net rounded to the cent would drift the total away from the gross the
    /// customer was quoted. 120,00 € / 1,19 = 100,840 (3 decimals). At count 3
    /// that is 302,52 € net; VAT is round(30252 × 0,19) = 5748 ct = 57,48 €;
    /// gross is 302,52 + 57,48 = 360,00 €.</summary>
    [Fact]
    public async Task A_count_above_one_on_a_flagged_line_still_totals_to_the_entered_gross()
    {
        using var host = new ApiTestHost();
        var (grossItemId, _, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var invoice = await LoadAsync(host, json.RootElement.GetProperty("Id").GetInt64());

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(100.840m, line.UnitPrice);

        var data = new DocumentDataBuilder().Build(invoice);
        Assert.Equal(360.00m, data.GrossTotal);
    }

    [Fact]
    public async Task A_revision_that_returns_the_shown_gross_stores_the_same_net()
    {
        using var host = new ApiTestHost();
        var (grossItemId, _, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();

        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();

        // 120,00 is what the dialog shows for a stored net of 100,840 — saving it
        // back untouched must not take the VAT out a second time
        var revised = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = grossItemId,
                            count = 1,
                            unitPrice = 120.00m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        using var revisedJson = await ODataJson.ReadAsync(revised);
        var revision = await LoadAsync(host, revisedJson.RootElement.GetProperty("Id").GetInt64());

        Assert.Equal(100.840m, Assert.Single(revision.Lines).UnitPrice);
        Assert.Equal(120.00m, new DocumentDataBuilder().Build(revision).GrossTotal);
    }
}
