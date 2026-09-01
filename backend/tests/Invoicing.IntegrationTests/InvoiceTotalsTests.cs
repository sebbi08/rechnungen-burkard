using System;
using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceTotalsTests
{
    /// <summary>A grave with one item (count 3 at 300.00, 7% VAT) and three
    /// recipients, invoiced for 2032 H1.</summary>
    private static async Task<long> SeedGraveWithThreeRecipientsAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "9001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 300m,
        };
        var recipients = new[]
        {
            new Recipient
            {
                Number = "E-0091",
                Name = "Anna Total",
                Address = "A 1",
            },
            new Recipient
            {
                Number = "E-0092",
                Name = "Bernd Total",
                Address = "B 2",
            },
            new Recipient
            {
                Number = "E-0093",
                Name = "Clara Total",
                Address = "C 3",
            },
        };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(recipients);
        var grave = new Grave
        {
            GraveNumber = "G-900",
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
                Count = 3,
            }
        );
        foreach (var recipient in recipients)
        {
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
        }
        await db.SaveChangesAsync();
        return grave.Id;
    }

    [Fact]
    public async Task Every_recipient_of_a_grave_invoice_is_billed_the_same_share_not_the_graves_whole_total()
    {
        using var host = new ApiTestHost();
        await SeedGraveWithThreeRecipientsAsync(host);
        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2032, 1);

        var response = await client.GetAsync("/odata/Invoices?$filter=GraveId ne null");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var rows = json.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);

        // The whole grave bills 3 x 300.00 net at 7% VAT = 900.00 net / 63.00
        // VAT / 963.00 gross. Each of the three recipients is billed a third of
        // that — not the grave's 100% total.
        foreach (var row in rows)
        {
            Assert.Equal(300.00m, row.GetProperty("NetTotal").GetDecimal());
            Assert.Equal(21.00m, row.GetProperty("VatTotal").GetDecimal());
            Assert.Equal(321.00m, row.GetProperty("GrossTotal").GetDecimal());
        }

        // and every recipient agrees with every other
        var grossTotals = rows.Select(r => r.GetProperty("GrossTotal").GetDecimal()).Distinct();
        Assert.Single(grossTotals);
    }

    [Fact]
    public async Task A_non_grave_invoice_reports_its_own_unchanged_totals()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "9002",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var recipient = new Recipient
        {
            Number = "E-0094",
            Name = "Dora Total",
            Address = "D 4",
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
                            count = 2,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        created.EnsureSuccessStatusCode();
        using var createdJson = await ODataJson.ReadAsync(created);
        var invoiceId = createdJson.RootElement.GetProperty("Id").GetInt64();

        var response = await client.GetAsync($"/odata/Invoices?$filter=Id eq {invoiceId}");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        var row = json.RootElement.GetProperty("value").EnumerateArray().Single();

        // 2 x 100.00 net at 7% VAT, undivided: this invoice's own 100%
        Assert.Equal(200.00m, row.GetProperty("NetTotal").GetDecimal());
        Assert.Equal(14.00m, row.GetProperty("VatTotal").GetDecimal());
        Assert.Equal(214.00m, row.GetProperty("GrossTotal").GetDecimal());
    }
}
