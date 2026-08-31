using System;
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

/// <summary>A grave whose item is priced VAT-included. The split divides a net —
/// that is what lets each recipient's document work out its own VAT and add up —
/// so the entered gross has to give up its VAT before the split, not after.
///
/// 120,00 € gross at 19 % is a net of 100,840, or 10 084 cents. Split across two
/// recipients that is 5 042 cents each, a unit price of 50,420, and a document
/// total of 50,42 € + 9,58 € = 60,00 € — twice 60,00 € being the 120,00 € that
/// was entered.</summary>
public class VatIncludedGraveTests
{
    private const decimal EnteredGross = 120.00m;
    private const decimal ExpectedGraveNet = 100.840m;
    private const decimal ExpectedShareNet = 50.420m;
    private const decimal ExpectedShareGross = 60.00m;

    private static async Task<long> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem
        {
            Number = "G-120",
            Description = "Grabpflege inklusive",
            VatRate = 19m,
            DefaultPrice = EnteredGross,
            PriceIncludesVat = true,
        };
        db.InvoiceItems.Add(item);
        for (var i = 1; i <= 2; i++)
        {
            db.Recipients.Add(
                new Recipient
                {
                    Number = $"E-70{i:D2}",
                    Name = $"Empfänger {i}",
                    Address = $"Weg {i}",
                }
            );
        }

        var grave = new Grave
        {
            GraveNumber = "V-1",
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

    [Fact]
    public async Task The_split_divides_the_net_and_the_shares_add_up_to_the_entered_gross()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).ToListAsync();

        Assert.Equal(2, invoices.Count);
        var builder = new DocumentDataBuilder();
        foreach (var invoice in invoices)
        {
            var line = Assert.Single(invoice.Lines);
            Assert.Equal(ExpectedShareNet, line.UnitPrice);
            // the grave's own price, before the split, is a net too
            Assert.Equal(ExpectedGraveNet, line.GraveUnitPrice);
            Assert.Equal(ExpectedShareGross, builder.Build(invoice).GrossTotal);
        }

        Assert.Equal(EnteredGross, invoices.Sum(i => builder.Build(i).GrossTotal));
    }

    [Fact]
    public async Task A_revision_that_returns_the_shown_gross_reproduces_the_same_shares()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        long invoiceId;
        long itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = await db.Invoices.OrderBy(i => i.Id).Select(i => i.Id).FirstAsync();
            itemId = await db.InvoiceItems.Select(i => i.Id).FirstAsync();
        }

        // the dialog opens on GraveUnitPrice (100,840) and shows it as 120,00;
        // sending that back untouched must reproduce the same split
        var response = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = EnteredGross,
                        },
                    },
                }
            )
        );
        // the action answers with the revisions themselves, so 200 rather than 201
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revisions = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.RevisionNumber == 1)
            .ToListAsync();

        Assert.Equal(2, revisions.Count);
        var builder = new DocumentDataBuilder();
        foreach (var revision in revisions)
        {
            Assert.Equal(ExpectedShareNet, Assert.Single(revision.Lines).UnitPrice);
            Assert.Equal(ExpectedShareGross, builder.Build(revision).GrossTotal);
        }
    }
}
