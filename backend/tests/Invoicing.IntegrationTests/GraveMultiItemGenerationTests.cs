using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave with several differently priced items: every recipient's
/// invoice carries the grave's items in their configured proportion, in full —
/// nothing is levelled to the same amount or split across recipients any more.
/// The recipient's own amount is what the document works out as their
/// share.</summary>
public class GraveMultiItemGenerationTests
{
    private const decimal ExpensivePrice = 100.00m;

    // 10,04 rather than 10,00: with the 100,00 line the 7 % subtotal comes to
    // 110,04, whose 11 004 cents divide by both two and three — the two
    // recipient counts these tests use. At 110,00 the three-way grave could not
    // be saved at all, since 11 000 leaves a remainder.
    private const decimal CheapPrice = 10.04m;
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
            db.Recipients.Add(
                new Recipient
                {
                    Number = $"E-{i:D4}",
                    Name = $"Empfänger {i}",
                    Address = $"Weg {i}",
                }
            );
        }

        var grave = new Grave
        {
            GraveNumber = "M-1",
            CemeteryId = TestCemetery.Id,
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();

        db.GraveItems.AddRange(
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = expensive.Id,
                Count = 1,
            },
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = cheap.Id,
                Count = 1,
            }
        );
        foreach (var recipient in await db.Recipients.ToListAsync())
        {
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
        }

        await db.SaveChangesAsync();
        return grave.Id;
    }

    private static async Task GenerateAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half })
        );
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
            if (status == "Succeeded")
                return;
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
    public async Task Two_recipients_are_billed_the_full_lines_and_share_the_gross_evenly()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, recipientCount: 2);
        using var client = host.CreateClient();

        await GenerateAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();
        Assert.Equal(2, invoices.Count);

        // 11 004 net cents divide evenly by two, so the grave saves and every
        // recipient is billed its prices in full.
        var builder = new DocumentDataBuilder();
        foreach (var invoice in invoices)
        {
            Assert.Equal(50m, invoice.SharePercent);
            Assert.Equal(2, invoice.RecipientCount);
            Assert.Equal(
                ExpensivePrice,
                invoice.Lines.Single(l => l.ItemNumber == "M-100").UnitPrice
            );
            Assert.Equal(CheapPrice, invoice.Lines.Single(l => l.ItemNumber == "M-010").UnitPrice);
        }

        // 5 502 net cents each, VAT round(55.02 x 7 / 100, 2) = 3.85, so 58.87
        // apiece and 117.74 between them — the grave's worth
        Assert.Equal(117.74m, invoices.Sum(i => builder.Build(i).Share!.GrossTotal));
    }

    [Fact]
    public async Task Three_recipients_are_billed_the_graves_lines_and_share_them_evenly()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host, recipientCount: 3);
        using var client = host.CreateClient();

        await GenerateAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(3, invoices.Count);

        // 100.00 + 10.04 = 110.04 net at 7 %, or 11004 cents, which divides by
        // three into 3668 cents each. Every recipient is billed that same total;
        // nothing is split between them and no price is adjusted to make it fit.
        Assert.All(invoices, i => Assert.Equal(11004, NetCents(i)));
        Assert.All(invoices, i => Assert.Equal(3, i.RecipientCount));

        // Each recipient's share is 36.68 net (11004 / 3, no remainder). Its VAT
        // is round(36.68 x 7 / 100, 2) = round(2.5676, 2) = 2.57, a gross of
        // 39.25, and the three of them collect 117.75. That figure is the
        // grave's worth: VAT is rounded once per recipient, so adding VAT to the
        // grave's own 110.04 instead gives 117.74 — a number nobody is charged.
        var builder = new DocumentDataBuilder();
        var shareGrosses = invoices.Select(i => builder.Build(i).Share!.GrossTotal).ToList();
        Assert.All(shareGrosses, gross => Assert.Equal(39.25m, gross));
        Assert.Equal(117.75m, shareGrosses.Sum());

        // every invoice carries the grave's own prices, untouched
        foreach (var invoice in invoices)
        {
            var expensive = invoice.Lines.Single(l => l.ItemNumber == "M-100").UnitPrice;
            var cheap = invoice.Lines.Single(l => l.ItemNumber == "M-010").UnitPrice;
            Assert.InRange(expensive / cheap, 9.9m, 10.1m);
        }
    }

    /// <summary>What the invoice's persisted lines actually bill, net — the same
    /// rounding DocumentDataBuilder applies.</summary>
    private static long NetCents(Invoice invoice) =>
        invoice.Lines.Sum(l =>
            (long)Math.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero)
        );
}
