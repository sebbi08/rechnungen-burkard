using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave with several differently priced items: each recipient's
/// invoice must keep the items in their configured proportion instead of
/// levelling every line to the same amount.</summary>
public class GraveMultiItemGenerationTests
{
    private const decimal ExpensivePrice = 100.00m;
    private const decimal CheapPrice = 10.00m;
    private const decimal VatRate = 7m;

    private static async Task<long> SeedAsync(ApiTestHost host, int recipientCount)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = VatRate });
        var expensive = new InvoiceItem
        {
            Number = "M-100",
            Description = "Grabpflege",
            VatRate = VatRate,
            DefaultPrice = ExpensivePrice,
        };
        var cheap = new InvoiceItem
        {
            Number = "M-010",
            Description = "Gießkanne",
            VatRate = VatRate,
            DefaultPrice = CheapPrice,
        };
        db.InvoiceItems.AddRange(expensive, cheap);
        for (var i = 1; i <= recipientCount; i++)
        {
            db.Recipients.Add(new Recipient
            {
                Number = $"E-{i:D4}",
                Name = $"Empfänger {i}",
                Address = $"Weg {i}",
            });
        }

        var grave = new Grave
        {
            GraveNumber = "M-1",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();

        db.GraveItems.AddRange(
            new GraveItem { GraveId = grave.Id, ItemId = expensive.Id, Count = 1 },
            new GraveItem { GraveId = grave.Id, ItemId = cheap.Id, Count = 1 });
        foreach (var recipient in await db.Recipients.ToListAsync())
        {
            db.GraveRecipients.Add(new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id });
        }

        await db.SaveChangesAsync();
        return grave.Id;
    }

    private static async Task GenerateAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/odata/Jobs({jobId})");
            response.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(response);
            var status = json.RootElement.GetProperty("Status").GetString();
            if (status == "Succeeded") return;
            Assert.NotEqual("Failed", status);
            await Task.Delay(100);
        }

        throw new TimeoutException("generation job did not finish");
    }

    [Fact]
    public async Task Single_recipient_invoice_bills_every_item_at_its_own_price()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, recipientCount: 1);
        using var client = host.CreateClient();

        await GenerateAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync();
        var lines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();

        Assert.Equal(ExpensivePrice, lines.Single(l => l.ItemNumber == "M-100").UnitPrice);
        Assert.Equal(CheapPrice, lines.Single(l => l.ItemNumber == "M-010").UnitPrice);
    }

    [Fact]
    public async Task Two_recipients_each_get_half_of_every_line()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, recipientCount: 2);
        using var client = host.CreateClient();

        await GenerateAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();
        Assert.Equal(2, invoices.Count);

        foreach (var invoice in invoices)
        {
            Assert.Equal(50m, invoice.SharePercent);
            Assert.Equal(ExpensivePrice / 2, invoice.Lines.Single(l => l.ItemNumber == "M-100").UnitPrice);
            Assert.Equal(CheapPrice / 2, invoice.Lines.Single(l => l.ItemNumber == "M-010").UnitPrice);
        }

        // the shares still add up to the grave's full gross: (100 + 10) * 1.07
        Assert.Equal(11770, invoices.Sum(i => GrossCents(i)));
    }

    [Fact]
    public async Task Three_way_split_distributes_the_remainder_and_stays_exact()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, recipientCount: 3);
        using var client = host.CreateClient();

        await GenerateAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(3, invoices.Count);

        // 10700 / 3 = 3566.67 and 1070 / 3 = 356.67: the cent remainders go to the
        // first recipients, and the totals still sum to the grave's 11770 cents
        Assert.Equal(11770, invoices.Sum(i => GrossCents(i)));
        Assert.All(invoices, i => Assert.All(i.Lines, l => Assert.True(l.UnitPrice > 0)));

        // every invoice keeps the 10:1 ratio between the two items
        foreach (var invoice in invoices)
        {
            var expensive = invoice.Lines.Single(l => l.ItemNumber == "M-100").UnitPrice;
            var cheap = invoice.Lines.Single(l => l.ItemNumber == "M-010").UnitPrice;
            Assert.InRange(expensive / cheap, 9.9m, 10.1m);
        }
    }

    private static long GrossCents(Invoice invoice) =>
        invoice.Lines.Sum(l => (long)Math.Round(
            Math.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero)
            * (100m + l.VatRate) / 100m, 0, MidpointRounding.AwayFromZero));
}
