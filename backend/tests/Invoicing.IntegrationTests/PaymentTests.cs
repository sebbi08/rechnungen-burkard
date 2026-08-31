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

    /// <summary>A grave with one item (count 2 at 100.00, 7% VAT) and two
    /// recipients, invoiced for 2031 H1. Returns the grave and the two invoice
    /// ids, ordered as generation created them.</summary>
    private static async Task<(
        long GraveId,
        long ItemId,
        long FirstInvoiceId,
        long SecondInvoiceId
    )> SeedGraveAsync(ApiTestHost host)
    {
        long graveId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "7002",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 100m,
            };
            var anna = new Recipient
            {
                Number = "E-0071",
                Name = "Anna Zahlung",
                Address = "A 1",
            };
            var bernd = new Recipient
            {
                Number = "E-0072",
                Name = "Bernd Zahlung",
                Address = "B 2",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.AddRange(anna, bernd);
            var grave = new Grave
            {
                GraveNumber = "G-700",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2040, 12, 31),
                CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();
            db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = item.Id,
                    Count = 2,
                }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = anna.Id }
            );
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = bernd.Id }
            );
            await db.SaveChangesAsync();
            (graveId, itemId) = (grave.Id, item.Id);
        }

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2031, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await verifyDb
            .Invoices.Where(i => i.GraveId == graveId)
            .OrderBy(i => i.Id)
            .ToListAsync();
        return (graveId, itemId, invoices[0].Id, invoices[1].Id);
    }

    [Fact]
    public async Task Revising_a_paid_invoice_is_refused()
    {
        using var host = new ApiTestHost();
        var (itemId, recipientId) = await SeedAsync(host);
        using var client = host.CreateClient();
        var invoiceId = await CreateInvoiceAsync(client, itemId, recipientId);
        (await SetPaidAsync(client, invoiceId, true)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
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
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvoicePaid",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        // the refusal left the chain alone
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Invoices.CountAsync(i => i.RootId == invoiceId));
    }

    [Fact]
    public async Task Revising_a_grave_invoice_is_refused_when_one_recipient_has_paid()
    {
        using var host = new ApiTestHost();
        var (_, itemId, firstInvoiceId, secondInvoiceId) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = secondInvoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("GraveInvoicePaid", error.GetProperty("code").GetString());
        // the message names who is paid: from the row the user pressed edit on,
        // another recipient's payment is invisible
        Assert.Contains("Anna Zahlung", error.GetProperty("message").GetString());

        // neither sibling gained a revision
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Invoices.CountAsync(i => i.GraveId != null));
    }

    [Fact]
    public async Task Revising_a_grave_invoice_succeeds_once_the_mark_is_taken_back()
    {
        using var host = new ApiTestHost();
        var (_, itemId, firstInvoiceId, secondInvoiceId) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();
        (await SetPaidAsync(client, firstInvoiceId, false)).EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId = secondInvoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 3,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // two originals plus one revision per recipient
        Assert.Equal(4, await db.Invoices.CountAsync(i => i.GraveId != null));
    }

    [Fact]
    public async Task Grave_status_and_group_report_how_many_recipients_have_paid()
    {
        using var host = new ApiTestHost();
        var (graveId, _, firstInvoiceId, _) = await SeedGraveAsync(host);
        using var client = host.CreateClient();
        (await SetPaidAsync(client, firstInvoiceId, true)).EnsureSuccessStatusCode();

        var status = await client.GetAsync("/odata/GraveInvoicingStatus?year=2031&half=1");
        status.EnsureSuccessStatusCode();
        using var statusJson = await ODataJson.ReadAsync(status);
        var row = statusJson
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(g => g.GetProperty("Id").GetInt64() == graveId);
        Assert.True(row.GetProperty("Invoiced").GetBoolean());
        Assert.Equal(2, row.GetProperty("InvoiceCount").GetInt32());
        Assert.Equal(1, row.GetProperty("PaidCount").GetInt32());

        // a half-year this grave was never invoiced for has nothing outstanding
        var other = await client.GetAsync("/odata/GraveInvoicingStatus?year=2031&half=2");
        other.EnsureSuccessStatusCode();
        using var otherJson = await ODataJson.ReadAsync(other);
        var otherRow = otherJson
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(g => g.GetProperty("Id").GetInt64() == graveId);
        Assert.Equal(0, otherRow.GetProperty("InvoiceCount").GetInt32());
        Assert.Equal(0, otherRow.GetProperty("PaidCount").GetInt32());

        var group = await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={firstInvoiceId}");
        group.EnsureSuccessStatusCode();
        using var groupJson = await ODataJson.ReadAsync(group);
        var paid = groupJson
            .RootElement.GetProperty("Recipients")
            .EnumerateArray()
            .Single(r => r.GetProperty("InvoiceId").GetInt64() == firstInvoiceId);
        Assert.True(paid.GetProperty("Paid").GetBoolean());
        var unpaid = groupJson
            .RootElement.GetProperty("Recipients")
            .EnumerateArray()
            .Single(r => r.GetProperty("InvoiceId").GetInt64() != firstInvoiceId);
        Assert.False(unpaid.GetProperty("Paid").GetBoolean());
    }
}
