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

/// <summary>Revising a VAT-included grave line the user never touched changes
/// nothing at all — not the stored net, not the gross the page shows, not the
/// bill. This pins that round trip.
///
/// It did not always hold. While the stored net kept three decimals and the
/// gross shown for it two, submitting the shown gross unchanged could land on a
/// different net than the one it was rendered from, and the displayed price
/// moved by a cent on the first revision before settling. Two-decimal nets
/// removed the mismatch: what the page shows converts back to exactly the net
/// it was rendered from, so the first generation is already the fixed point.
///
/// Traced at 119,00 € gross, 7 %, count 1, three recipients: NetOf rounds
/// 111,21495 down to 111,21, whose line would bill 118,99 — under the 119,00
/// entered — so it is raised to 111,22. Those 11 122 net cents leave a
/// remainder of 1 modulo 3, so two more cents reach 11 124 and the line is
/// stored at 111,24. The page shows round(111,24 × 1,07) = 119,03, and
/// submitting that back gives 111,24 again.
///
/// Each recipient is billed 3 708 net cents — 37,08 € net, 39,68 € gross — at
/// every one of the three generations.</summary>
public class VatIncludedGraveRevisionTests
{
    private const decimal EnteredGross = 119.00m;
    private const decimal VatRate = 7m;
    private const int Recipients = 3;

    // 11 124 net cents over three recipients: 3 708 net cents each, VAT
    // round(37,08 × 7 / 100, 2) = 2,60, gross 39,68.
    private const decimal ExpectedShareNet = 37.08m;
    private const decimal ExpectedShareGross = 39.68m;

    private static async Task SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = VatRate });
        var item = new InvoiceItem
        {
            Number = "G-119",
            Description = "Grabpflege inklusive",
            VatRate = VatRate,
            DefaultPrice = EnteredGross,
        };
        db.InvoiceItems.Add(item);
        for (var i = 1; i <= Recipients; i++)
        {
            db.Recipients.Add(
                new Recipient
                {
                    Number = $"E-80{i:D2}",
                    Name = $"Empfänger {i}",
                    Address = $"Weg {i}",
                }
            );
        }

        var grave = new Grave
        {
            GraveNumber = "D-1",
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
    }

    /// <summary>The gross a price field shows for a stored net — the same rule as
    /// <c>api.grossFromNet</c> in the frontend, which is what puts the number in
    /// front of the user before they press save.</summary>
    private static decimal ShownGross(decimal net) =>
        decimal.Round(net * (1m + VatRate / 100m), 2, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task Revising_an_untouched_vat_included_line_bills_the_same_every_time()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        var storedNets = new List<decimal>();
        var shownGrosses = new List<decimal>();

        for (var round = 0; round <= 2; round++)
        {
            long editedInvoiceId;
            long itemId;
            decimal storedNet;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var latest = await db
                    .Invoices.AsNoTracking()
                    .Include(i => i.Lines)
                    .Where(i => i.IsLatestRevision)
                    .OrderBy(i => i.Id)
                    .ToListAsync();
                itemId = await db.InvoiceItems.Select(i => i.Id).FirstAsync();

                Assert.Equal(Recipients, latest.Count);
                var builder = new DocumentDataBuilder();
                foreach (var invoice in latest)
                {
                    Assert.Equal(Recipients, invoice.RecipientCount);
                    var share = builder.Build(invoice).Share!;
                    // the invariant: the same bill at every generation, whatever
                    // the stored net has drifted to
                    Assert.Equal(ExpectedShareNet, share.NetTotal);
                    Assert.Equal(ExpectedShareGross, share.GrossTotal);
                }

                // every recipient carries the grave's own line, so any of them
                // reports the grave's stored net
                storedNet = Assert.Single(latest[0].Lines).UnitPrice;
                editedInvoiceId = latest[0].Id;
            }

            storedNets.Add(storedNet);
            shownGrosses.Add(ShownGross(storedNet));
            if (round == 2)
            {
                break;
            }

            // the dialog sends back exactly what it showed, untouched
            var response = await client.PostAsync(
                "/odata/CreateGraveInvoiceRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId = editedInvoiceId,
                        lines = new[]
                        {
                            new
                            {
                                itemId,
                                count = 1,
                                unitPrice = ShownGross(storedNet),
                                priceIncludesVat = true,
                            },
                        },
                    }
                )
            );
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // nothing moves: the first generation is already the fixed point, and
        // every price has two decimals
        Assert.Equal(new[] { 111.24m, 111.24m, 111.24m }, storedNets);
        Assert.Equal(new[] { 119.03m, 119.03m, 119.03m }, shownGrosses);
        Assert.All(storedNets, net => Assert.Equal(net, decimal.Round(net, 2)));

        // and all three revisions exist, so the ones above really were three
        // separate generations rather than one repeated read
        using var verifyScope = host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            new[] { 0, 1, 2 },
            await verifyDb
                .Invoices.Select(i => i.RevisionNumber)
                .Distinct()
                .OrderBy(n => n)
                .ToArrayAsync()
        );
    }
}
