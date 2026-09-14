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

/// <summary>A grave whose item is priced VAT-included. Every recipient is billed
/// the grave's own net in full, so it is that net which has to give up its VAT —
/// before generation, not after — and the recipient's share is worked out by the
/// document, not by dividing the line.
///
/// 120,00 € gross at 19 % is a net of 100,840, or 10 084 cents. That net already
/// divides evenly across two recipients (10 084 / 2 = 5 042), so MakeDivisible
/// bumps nothing and each recipient's line stays at 100,840. Each document's
/// share is then 5 042 cents net, VAT round(50,42 × 19 / 100, 2) = 9,58, for a
/// share gross of 60,00 € — twice 60,00 € being the 120,00 € that was
/// entered.</summary>
public class VatIncludedGraveTests
{
    private const decimal EnteredGross = 120.00m;
    private const decimal ExpectedGraveNet = 100.840m;
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
                PriceIncludesVat = true,
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
            // the line carries the grave's own net in full; nothing is split
            // any more, so there is no separate "before the split" price to record
            Assert.Equal(ExpectedGraveNet, line.UnitPrice);
            Assert.Null(line.GraveUnitPrice);
            Assert.Equal(2, invoice.RecipientCount);
            Assert.Equal(ExpectedShareGross, builder.Build(invoice).Share!.GrossTotal);
        }

        Assert.Equal(EnteredGross, invoices.Sum(i => builder.Build(i).Share!.GrossTotal));
    }

    /// <summary>A grave whose link overrides the item's default price
    /// (<see cref="GraveItem.UnitPrice"/>) on a flagged item: the override, not
    /// the item's default, is what gives up its VAT and is billed to every
    /// recipient in full.
    ///
    /// Derivation: the override is entered as 238,00 € gross at 19 %.
    /// <c>NetOf(238.00, 19, true)</c> rounds the gross to the cent (already
    /// exact), then divides by 1,19 — 238,00 / 1,19 = 200,000 exactly, so no
    /// rounding bites. At count 1 that is 20 000 net cents, which already
    /// divides evenly by 2 recipients, so <c>MakeDivisible</c> bumps nothing and
    /// every recipient's line keeps the full 200,000. Each recipient's share is
    /// then 20 000 / 2 = 10 000 cents net = 100,00 €, and its VAT is
    /// <c>round(100.00 × 19 / 100, 2)</c> = 19,00 €, for a share gross of
    /// 119,00 €. The two recipients' share grosses sum back to the 238,00 € that
    /// was entered — not the 120,00 € the item's own default price would have
    /// produced, which is what proves the override is what was billed.</summary>
    [Fact]
    public async Task A_grave_price_override_on_a_flagged_item_gives_up_its_own_vat()
    {
        const decimal EnteredOverrideGross = 238.00m;
        const decimal ExpectedOverrideNet = 200.000m;
        const decimal ExpectedOverrideShareGross = 119.00m;

        using var host = new ApiTestHost();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 19m });
            var item = new InvoiceItem
            {
                Number = "G-238",
                Description = "Grabpflege inklusive, Sonderpreis",
                VatRate = 19m,
                // deliberately different from the override below, so a result
                // matching this price instead would show the override was ignored
                DefaultPrice = EnteredGross,
            };
            db.InvoiceItems.Add(item);
            for (var i = 1; i <= 2; i++)
            {
                db.Recipients.Add(
                    new Recipient
                    {
                        Number = $"E-71{i:D2}",
                        Name = $"Empfänger Sonderpreis {i}",
                        Address = $"Weg {i}",
                    }
                );
            }

            var grave = new Grave
            {
                GraveNumber = "V-2",
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
                    UnitPrice = EnteredOverrideGross,
                    PriceIncludesVat = true,
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

        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.RecipientName.StartsWith("Empfänger Sonderpreis"))
            .ToListAsync();

        Assert.Equal(2, invoices.Count);
        var builder = new DocumentDataBuilder();
        foreach (var invoice in invoices)
        {
            var line = Assert.Single(invoice.Lines);
            Assert.Equal(ExpectedOverrideNet, line.UnitPrice);
            Assert.Null(line.GraveUnitPrice);
            Assert.Equal(2, invoice.RecipientCount);
            Assert.Equal(ExpectedOverrideShareGross, builder.Build(invoice).Share!.GrossTotal);
        }

        Assert.Equal(EnteredOverrideGross, invoices.Sum(i => builder.Build(i).Share!.GrossTotal));
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

        // the dialog opens on the grave's own line (100,840) and shows it as
        // 120,00 gross; sending that back untouched must reproduce the same shares
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
                            priceIncludesVat = true,
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
            var line = Assert.Single(revision.Lines);
            // the revision carries the grave's own net in full, exactly as
            // generation does — nothing is split any more
            Assert.Equal(ExpectedGraveNet, line.UnitPrice);
            Assert.Null(line.GraveUnitPrice);
            Assert.Equal(2, revision.RecipientCount);
            Assert.Equal(ExpectedShareGross, builder.Build(revision).Share!.GrossTotal);
        }
    }
}
