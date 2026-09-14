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
/// 117,00 € gross at 7 % over three recipients: the net is 109,35, which
/// divides into three shares of 36,45, each carrying 2,55 VAT for a gross of
/// 39,00 — so the three of them collect exactly the 117,00 that was typed. A
/// grave whose prices did not come out that evenly could not be saved in the
/// first place, which is what makes this round trip a fixed point rather than
/// something that drifts.</summary>
public class VatIncludedGraveRevisionTests
{
    private const decimal EnteredGross = 117.00m;
    private const decimal VatRate = 7m;
    private const int Recipients = 3;

    // 10 935 net cents over three recipients: 3 645 net cents each, VAT
    // round(36,45 × 7 / 100, 2) = 2,55, gross 39,00.
    private const decimal ExpectedShareNet = 36.45m;
    private const decimal ExpectedShareGross = 39.00m;

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
        Assert.Equal(new[] { 109.35m, 109.35m, 109.35m }, storedNets);
        Assert.Equal(new[] { 117.00m, 117.00m, 117.00m }, shownGrosses);
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
