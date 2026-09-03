using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave dialog warns before saving when a price will not be
/// billed as it was entered, which means asking for the billed price while the
/// grave has no invoices at all. These pin that the preview answers with the
/// figures generation would go on to produce.</summary>
public class GraveBillingPreviewTests
{
    private static async Task<(long Net, long Gross)> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.AddRange(new VatRate { Rate = 7m }, new VatRate { Rate = 19m });

        var net = new InvoiceItem
        {
            Number = "P-100",
            Description = "Grabstein reinigen",
            VatRate = 19m,
            DefaultPrice = 47.80m,
        };
        var gross = new InvoiceItem
        {
            Number = "P-101",
            Description = "Sammelgrabpflege",
            VatRate = 19m,
            DefaultPrice = 100.25m,
        };
        db.InvoiceItems.AddRange(net, gross);
        await db.SaveChangesAsync();
        return (net.Id, gross.Id);
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/odata/PreviewGraveBilling", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task A_net_price_that_has_to_be_raised_reports_both_figures()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        // 47,80 at 19 % over three recipients: 4 780 cents leave a remainder of
        // one, and 4 782 is the first total that both divides by three and
        // leaves the three shares collecting the 56,88 the line is worth.
        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 3,
                Lines = new[]
                {
                    new
                    {
                        ItemId = netItem,
                        Count = 1,
                        UnitPrice = 47.80m,
                        PriceIncludesVat = false,
                    },
                },
            }
        );

        var line = body.GetProperty("Lines")[0];
        Assert.Equal(47.80m, line.GetProperty("EnteredPrice").GetDecimal());
        Assert.Equal(47.82m, line.GetProperty("BilledPrice").GetDecimal());
        Assert.Equal(47.82m, line.GetProperty("UnitPrice").GetDecimal());
    }

    [Fact]
    public async Task A_gross_price_is_compared_as_a_gross()
    {
        using var host = new ApiTestHost();
        var (_, grossItem) = await SeedAsync(host);
        using var client = host.CreateClient();

        // The reported case: 100,25 entered as a gross at 19 % over two
        // recipients. Its net of 84,24 divides evenly, but the two shares would
        // collect 100,24, so the net is raised to 84,26 — which shows as a gross
        // of 100,27. The comparison has to happen in the unit that was typed, or
        // the dialog would be holding 100,25 against a net of 84,26.
        //
        // 100,27 is the gross of the raised unit price, which is the figure the
        // grave page displays; the two invoices collect 100,26 between them,
        // because their VAT is rounded once per recipient. Both are right, and
        // the dialog warns with the one the user will see on the page.
        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 2,
                Lines = new[]
                {
                    new
                    {
                        ItemId = grossItem,
                        Count = 1,
                        UnitPrice = 100.25m,
                        PriceIncludesVat = true,
                    },
                },
            }
        );

        var line = body.GetProperty("Lines")[0];
        Assert.Equal(100.25m, line.GetProperty("EnteredPrice").GetDecimal());
        Assert.Equal(100.27m, line.GetProperty("BilledPrice").GetDecimal());
        Assert.Equal(84.26m, line.GetProperty("UnitPrice").GetDecimal());
    }

    [Fact]
    public async Task A_price_that_needs_no_raising_reports_the_same_figure_twice()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        // 47,80 over two recipients: 4 780 cents already divide.
        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 2,
                Lines = new[]
                {
                    new
                    {
                        ItemId = netItem,
                        Count = 1,
                        UnitPrice = 47.80m,
                        PriceIncludesVat = false,
                    },
                },
            }
        );

        var line = body.GetProperty("Lines")[0];
        Assert.Equal(
            line.GetProperty("EnteredPrice").GetDecimal(),
            line.GetProperty("BilledPrice").GetDecimal()
        );
    }

    [Fact]
    public async Task A_line_without_its_own_price_falls_back_to_the_item_default()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await PreviewAsync(
            client,
            new
            {
                Recipients = 3,
                Lines = new[]
                {
                    new
                    {
                        ItemId = netItem,
                        Count = 1,
                        PriceIncludesVat = false,
                    },
                },
            }
        );

        var line = body.GetProperty("Lines")[0];
        Assert.Equal(47.80m, line.GetProperty("EnteredPrice").GetDecimal());
        Assert.Equal(47.82m, line.GetProperty("BilledPrice").GetDecimal());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Without_recipients_there_is_no_billed_price_to_report(int recipients)
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/odata/PreviewGraveBilling",
            new
            {
                Recipients = recipients,
                Lines = new[]
                {
                    new
                    {
                        ItemId = netItem,
                        Count = 1,
                        PriceIncludesVat = false,
                    },
                },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NoRecipients", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_item_is_refused_rather_than_guessed_at()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/odata/PreviewGraveBilling",
            new
            {
                Recipients = 2,
                Lines = new[]
                {
                    new
                    {
                        ItemId = 999_999L,
                        Count = 1,
                        PriceIncludesVat = false,
                    },
                },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ItemNotFound", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_preview_matches_what_generation_goes_on_to_bill()
    {
        // The preview is only worth showing if it agrees with billing, so this
        // asks for both and compares them rather than restating the expected
        // numbers a third time.
        using var host = new ApiTestHost();
        var (netItem, grossItem) = await SeedAsync(host);
        using var client = host.CreateClient();

        var lines = new[]
        {
            new
            {
                ItemId = netItem,
                Count = 1,
                UnitPrice = 47.80m,
                PriceIncludesVat = false,
            },
            new
            {
                ItemId = grossItem,
                Count = 1,
                UnitPrice = 100.25m,
                PriceIncludesVat = true,
            },
        };

        var preview = await PreviewAsync(client, new { Recipients = 3, Lines = lines });

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = new Grave
            {
                GraveNumber = "PV-1",
                StartDate = new DateOnly(2025, 1, 1),
                EndDate = new DateOnly(2035, 12, 31),
            };
            db.Graves.Add(grave);
            await db.SaveChangesAsync();

            foreach (var line in lines)
            {
                db.GraveItems.Add(
                    new GraveItem
                    {
                        GraveId = grave.Id,
                        ItemId = line.ItemId,
                        Count = line.Count,
                        UnitPrice = line.UnitPrice,
                        PriceIncludesVat = line.PriceIncludesVat,
                    }
                );
            }

            for (var i = 0; i < 3; i++)
            {
                var recipient = new Recipient
                {
                    Number = $"PV-{i:D4}",
                    Name = $"Empfänger {i}",
                    Address = "Musterweg 1",
                };
                db.Recipients.Add(recipient);
                await db.SaveChangesAsync();
                db.GraveRecipients.Add(
                    new GraveRecipient { GraveId = grave.Id, RecipientId = recipient.Id }
                );
            }

            await db.SaveChangesAsync();
        }

        await GraveGenerationHelper.RunAsync(client, 2030, 1);

        using var verify = host.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var billed = verifyDb
            .Invoices.Include(i => i.Lines)
            .Where(i => i.GraveId != null)
            .OrderBy(i => i.Id)
            .First()
            .Lines.OrderBy(l => l.LineNumber)
            .Select(l => l.UnitPrice)
            .ToList();

        var previewed = new List<decimal>();
        foreach (var line in preview.GetProperty("Lines").EnumerateArray())
        {
            previewed.Add(line.GetProperty("UnitPrice").GetDecimal());
        }

        Assert.Equal(previewed, billed);
    }
}
