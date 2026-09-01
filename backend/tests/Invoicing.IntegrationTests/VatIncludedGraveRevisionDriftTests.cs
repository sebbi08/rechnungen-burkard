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

/// <summary>Revising a VAT-included grave line the user never touched moves the
/// gross the page shows, and this pins the fact that it moves no money.
///
/// The round trip is not the identity. The stored net is three decimals, the
/// gross shown for it is two, and <c>GraveShare.MakeDivisible</c> adds whole
/// cents to the net on the way back in — so submitting the shown gross
/// unchanged can land on a different net than the one it was rendered from.
///
/// Traced at 119,00 € gross, 7 %, count 1, three recipients:
///
/// <list type="bullet">
/// <item>Generation: NetOf(119,00) = 111,215, whose 11 122 net cents leave a
/// remainder of 1 modulo 3, so two bumps reach 11 124 and the line is stored at
/// 111,235. The page shows round(111,235 × 1,07) = 119,02.</item>
/// <item>First revision submits 119,02. NetOf gives 111,234 — 11 123 cents,
/// remainder 2 — so one bump reaches 11 124 and the line is stored at 111,244.
/// The page now shows 119,03.</item>
/// <item>Second revision submits 119,03. NetOf gives 111,243, already 11 124
/// cents, so nothing is bumped and the line is stored at 111,243. The page
/// still shows 119,03: it has settled.</item>
/// </list>
///
/// The displayed gross therefore moves once by a cent and then stops. What never
/// moves is the bill: every one of those three nets rounds to the same 11 124
/// net cents, so each recipient is billed 3 708 net cents and 39,68 € gross at
/// every generation. That is the property worth guaranteeing, and the one this
/// test asserts.</summary>
public class VatIncludedGraveRevisionDriftTests
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

        // the traced drift, recorded rather than fixed: the stored net moves and
        // the shown gross moves once, then settles
        Assert.Equal(new[] { 111.235m, 111.244m, 111.243m }, storedNets);
        Assert.Equal(new[] { 119.02m, 119.03m, 119.03m }, shownGrosses);

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
