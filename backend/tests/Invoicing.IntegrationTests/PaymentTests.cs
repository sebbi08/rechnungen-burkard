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

    /// <summary>Marks an invoice paid or unpaid through the action.</summary>
    private static Task<HttpResponseMessage> SetPaidAsync(
        HttpClient client,
        long invoiceId,
        bool paid
    ) => client.PostAsync("/odata/SetInvoicePaid", ODataJson.Body(new { invoiceId, paid }));

    [Fact]
    public async Task Marking_stamps_todays_date_and_unmarking_clears_it()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        var marked = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Now), invoice.PaidOn);
        }

        var cleared = await SetPaidAsync(client, invoiceId, false);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }
    }

    [Fact]
    public async Task Marking_an_already_paid_invoice_keeps_the_recorded_day()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

        // a payment recorded on an earlier day: marking again must not move it
        // to today, or a double click would silently rewrite a payment date
        var earlier = new DateOnly(2026, 1, 2);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            invoice.PaidOn = earlier;
            await db.SaveChangesAsync();
        }

        var again = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using (var verify = host.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(earlier, (await db.Invoices.SingleAsync(i => i.Id == invoiceId)).PaidOn);
        }
    }

    [Fact]
    public async Task Marking_a_superseded_revision_is_refused()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);

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
                            itemId,
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        revised.EnsureSuccessStatusCode();

        var response = await SetPaidAsync(client, invoiceId, true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NotLatestRevision",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Marking_an_unknown_invoice_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await SetPaidAsync(client, 987654, true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvoiceNotFound",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
}
