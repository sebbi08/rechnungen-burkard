using System;
using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Generation bills every recipient the grave's own lines at 100 %,
/// instead of dividing each line across the recipients. The grave seeded here
/// divides as it is, so nothing is corrected.</summary>
public class GraveShareGenerationTests
{
    private static async Task SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.AddRange(new VatRate { Rate = 7m }, new VatRate { Rate = 19m });

        var grabpflege = new InvoiceItem
        {
            Number = "M-100",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100.00m,
        };
        var blumenschmuck = new InvoiceItem
        {
            Number = "M-101",
            Description = "Blumenschmuck",
            VatRate = 19m,
            DefaultPrice = 24.95m,
        };
        var grabstein = new InvoiceItem
        {
            Number = "M-102",
            Description = "Grabstein",
            VatRate = 19m,
            // 47,81 rather than 47,80: with Blumenschmuck's 49,90 the 19 %
            // subtotal comes to 97,71, which divides by three. At 47,80 it would
            // be 97,70, and the save would ask to raise a price first -- see
            // GraveBillingCorrectionTests. Seeded as it divides, the figures below
            // are the prices as entered.
            DefaultPrice = 47.81m,
        };
        var giessdienst = new InvoiceItem
        {
            Number = "M-103",
            Description = "Gießdienst",
            VatRate = 7m,
            DefaultPrice = 3.70m,
        };
        db.InvoiceItems.AddRange(grabpflege, blumenschmuck, grabstein, giessdienst);

        for (var i = 1; i <= 3; i++)
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
                ItemId = grabpflege.Id,
                Count = 1,
            },
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = blumenschmuck.Id,
                Count = 2,
            },
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = grabstein.Id,
                Count = 1,
            },
            new GraveItem
            {
                GraveId = grave.Id,
                ItemId = giessdienst.Id,
                Count = 5,
            }
        );
        foreach (var recipient in await db.Recipients.ToListAsync())
        {
            db.GraveRecipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
            );
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_recipient_is_billed_the_graves_own_lines()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();

        Assert.Equal(3, invoices.Count);
        Assert.All(invoices, i => Assert.Equal(3, i.RecipientCount));

        // the prices as entered: the 19 % subtotal, 49,90 + 47,81 = 97,71,
        // divides by three as it is
        foreach (var invoice in invoices)
        {
            var lines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();
            Assert.Equal(4, lines.Count);
            Assert.Equal(100.00m, lines[0].UnitPrice);
            Assert.Equal(24.95m, lines[1].UnitPrice);
            Assert.Equal(47.81m, lines[2].UnitPrice);
            Assert.Equal(3.70m, lines[3].UnitPrice);
        }

        // and every recipient's invoice carries exactly the same lines
        var first = invoices[0]
            .Lines.OrderBy(l => l.LineNumber)
            .Select(l => (l.Count, l.UnitPrice, l.VatRate));
        Assert.All(
            invoices.Skip(1),
            i =>
                Assert.Equal(
                    first,
                    i.Lines.OrderBy(l => l.LineNumber)
                        .Select(l => (l.Count, l.UnitPrice, l.VatRate))
                )
        );
    }

    [Fact]
    public async Task Each_vat_subtotal_divides_by_the_recipient_count()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = (await db.Invoices.Include(i => i.Lines).FirstAsync()).Lines;

        foreach (var group in lines.GroupBy(l => l.VatRate))
        {
            var subtotal = group.Sum(l =>
                (long)decimal.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero)
            );
            Assert.Equal(0, subtotal % 3);
        }
    }
}
