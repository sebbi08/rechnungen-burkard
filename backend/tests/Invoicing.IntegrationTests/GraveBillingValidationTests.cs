using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave dialog asks whether the prices it is about to save can be
/// split evenly, so it can name the rate and what would work instead. The save
/// enforces the same rule; this is what lets the dialog say so first.</summary>
public class GraveBillingValidationTests
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

    private static async Task<JsonElement> ValidateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/odata/ValidateGraveBilling", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static object Line(long itemId, decimal? price, bool gross = false, int count = 1) =>
        new
        {
            ItemId = itemId,
            Count = count,
            UnitPrice = price,
            PriceIncludesVat = gross,
        };

    [Fact]
    public async Task A_subtotal_that_does_not_divide_names_the_rate_and_what_would()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        // 47,80 at 19 % over three: 4 780 cents leaves a remainder of one
        var body = await ValidateAsync(
            client,
            new { Recipients = 3, Lines = new[] { Line(netItem, 47.80m) } }
        );

        Assert.False(body.GetProperty("Ok").GetBoolean());
        var problem = Assert.Single(body.GetProperty("Problems").EnumerateArray().ToArray());
        Assert.Equal(19m, problem.GetProperty("VatRate").GetDecimal());
        Assert.Equal("NotDivisible", problem.GetProperty("Kind").GetString());
        Assert.Equal(47.80m, problem.GetProperty("NetSubtotal").GetDecimal());

        var nearby = problem
            .GetProperty("NearbyValidSubtotals")
            .EnumerateArray()
            .Select(v => v.GetDecimal())
            .ToArray();
        Assert.Equal(new[] { 47.79m, 47.82m }, nearby);
    }

    [Fact]
    public async Task A_subtotal_that_divides_is_accepted_with_what_it_collects()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        // 47,79 at 19 % over three: 1 593 net cents each, VAT 303, so 1 896
        var body = await ValidateAsync(
            client,
            new { Recipients = 3, Lines = new[] { Line(netItem, 47.79m) } }
        );

        Assert.True(body.GetProperty("Ok").GetBoolean());
        Assert.Empty(body.GetProperty("Problems").EnumerateArray().ToArray());
        Assert.Equal(56.88m, body.GetProperty("GrossTotal").GetDecimal());
    }

    [Fact]
    public async Task A_gross_price_that_cannot_be_billed_exactly_is_reported_as_such()
    {
        using var host = new ApiTestHost();
        var (_, grossItem) = await SeedAsync(host);
        using var client = host.CreateClient();

        // 100,25 entered as a gross at 19 % over two. Its net of 84,24 divides,
        // but the two shares collect 100,24 — a cent under what was typed.
        var body = await ValidateAsync(
            client,
            new { Recipients = 2, Lines = new[] { Line(grossItem, 100.25m, gross: true) } }
        );

        Assert.False(body.GetProperty("Ok").GetBoolean());
        var problem = Assert.Single(body.GetProperty("Problems").EnumerateArray().ToArray());
        Assert.Equal("DoesNotAddUp", problem.GetProperty("Kind").GetString());
        Assert.Equal(100.24m, problem.GetProperty("Collected").GetDecimal());
        Assert.Equal(100.25m, problem.GetProperty("Owed").GetDecimal());
    }

    [Fact]
    public async Task A_gross_price_that_comes_out_even_is_accepted()
    {
        using var host = new ApiTestHost();
        var (_, grossItem) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await ValidateAsync(
            client,
            new { Recipients = 2, Lines = new[] { Line(grossItem, 100.24m, gross: true) } }
        );

        Assert.True(body.GetProperty("Ok").GetBoolean());
        // what was typed is what the two of them collect
        Assert.Equal(100.24m, body.GetProperty("GrossTotal").GetDecimal());
    }

    [Fact]
    public async Task A_line_without_its_own_price_falls_back_to_the_item_default()
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var body = await ValidateAsync(
            client,
            new { Recipients = 3, Lines = new[] { Line(netItem, null) } }
        );

        // the item's default of 47,80 is what gets judged
        var problem = Assert.Single(body.GetProperty("Problems").EnumerateArray().ToArray());
        Assert.Equal(47.80m, problem.GetProperty("NetSubtotal").GetDecimal());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Without_recipients_there_is_nothing_to_divide(int recipients)
    {
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/odata/ValidateGraveBilling",
            new { Recipients = recipients, Lines = new[] { Line(netItem, 47.80m) } }
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
            "/odata/ValidateGraveBilling",
            new { Recipients = 2, Lines = new[] { Line(999_999L, 10m) } }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ItemNotFound", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_check_agrees_with_the_save()
    {
        // The dialog would be lying if it said yes to something the save then
        // refused, so this asks both about the same grave.
        using var host = new ApiTestHost();
        var (netItem, _) = await SeedAsync(host);
        using var client = host.CreateClient();

        var recipientIds = new List<long>();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var recipient = new Recipient
                {
                    Number = $"CV-{i:D4}",
                    Name = $"Empfänger {i}",
                    Address = "Musterweg 1",
                };
                db.Recipients.Add(recipient);
                await db.SaveChangesAsync();
                recipientIds.Add(recipient.Id);
            }
        }

        foreach (var price in new[] { 47.80m, 47.79m })
        {
            var check = await ValidateAsync(
                client,
                new { Recipients = 3, Lines = new[] { Line(netItem, price) } }
            );

            var save = await client.PostAsJsonAsync(
                "/odata/SaveGrave",
                new
                {
                    graveNumber = $"CV-{price}",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId = netItem,
                            count = 1,
                            unitPrice = price,
                            priceIncludesVat = false,
                        },
                    },
                    recipientIds,
                }
            );

            Assert.Equal(
                check.GetProperty("Ok").GetBoolean(),
                save.StatusCode == HttpStatusCode.OK
            );
        }
    }
}
