using System;
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

public class PaymentTests
{
    /// <summary>One item (100.00 at 7% VAT) and one recipient, ready to be
    /// invoiced through /odata/CreateInvoice.</summary>
    private static async Task<(long ItemId, long RecipientId)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "7001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0007",
            Name = "Familie Zahlung",
            Address = "Hauptstraße 7",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    /// <summary>Creates one invoice with a single line and returns its id.</summary>
    private static async Task<long> CreateInvoiceAsync(
        HttpClient client,
        long itemId,
        long recipientId
    )
    {
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
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task New_invoice_is_unpaid_and_the_unpaid_filter_returns_it()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }

        var unpaid = await client.GetAsync("/odata/Invoices?$filter=PaidOn eq null");
        Assert.Equal(HttpStatusCode.OK, unpaid.StatusCode);
        using var body = await ODataJson.ReadAsync(unpaid);
        var ids = body
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(row => row.GetProperty("Id").GetInt64())
            .ToList();
        Assert.Contains(invoiceId, ids);
    }
}
