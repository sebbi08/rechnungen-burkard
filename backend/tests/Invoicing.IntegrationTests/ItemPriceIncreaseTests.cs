using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What raising an item's prices would need, checked before anything
/// is written: the corrections a raised price makes necessary and the rows no
/// correction can fix.</summary>
public class ItemPriceIncreaseTests
{
    private static async Task<long> CreateAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsync(path, ODataJson.Body(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<long> ItemAsync(
        HttpClient client,
        string number,
        decimal? defaultPrice
    )
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        return await CreateAsync(
            client,
            "/odata/InvoiceItems",
            new
            {
                Number = number,
                Description = "Grabpflege",
                VatRate = 19.0m,
                DefaultPrice = defaultPrice,
            }
        );
    }

    private static async Task<long[]> RecipientsAsync(HttpClient client, string tag, int count)
    {
        var ids = new List<long>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(
                await CreateAsync(
                    client,
                    "/odata/Recipients",
                    new { Name = $"Empfänger {tag} {i}", Address = "Weg 1" }
                )
            );
        }

        return ids.ToArray();
    }

    private sealed record Link(long ItemId, decimal? UnitPrice, bool Gross = false);

    /// <summary>Saves a grave the way the dialog does: the corrections its
    /// prices need are the ones the preview names, as the editor confirms them.</summary>
    private static async Task<long> SaveGraveAsync(
        HttpClient client,
        string graveNumber,
        Link[] links,
        int recipients,
        bool cooperative = false
    )
    {
        var recipientIds = cooperative
            ? []
            : await RecipientsAsync(client, graveNumber, recipients);
        var corrections = new List<object>();
        if (!cooperative)
        {
            var preview = await client.PostAsync(
                "/odata/PreviewGraveBilling",
                ODataJson.Body(
                    new
                    {
                        recipients,
                        startMonth = "2026-01",
                        endMonth = "2040-12",
                        lines = links.Select(l => new
                        {
                            itemId = l.ItemId,
                            count = 1,
                            unitPrice = l.UnitPrice,
                            priceIncludesVat = l.Gross,
                        }),
                    }
                )
            );
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            using var json = await ODataJson.ReadAsync(preview);
            foreach (var invoice in json.RootElement.GetProperty("Invoices").EnumerateArray())
            {
                foreach (var c in invoice.GetProperty("Corrections").EnumerateArray())
                {
                    corrections.Add(
                        new
                        {
                            kind = c.GetProperty("Kind").GetString(),
                            year = c.GetProperty("Year").ValueKind == JsonValueKind.Null
                                ? (int?)null
                                : c.GetProperty("Year").GetInt32(),
                            half = c.GetProperty("Half").GetInt32(),
                            vatRate = c.GetProperty("VatRate").GetDecimal(),
                            itemId = c.GetProperty("ItemId").GetInt64(),
                            correctedPrice = c.GetProperty("CorrectedPrice").GetDecimal(),
                        }
                    );
                }
            }
        }

        var saved = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber,
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    isCooperative = cooperative,
                    contractNumber = cooperative ? "V-1" : null,
                    items = links.Select(l => new
                    {
                        itemId = l.ItemId,
                        count = 1,
                        unitPrice = l.UnitPrice,
                        priceIncludesVat = l.Gross,
                    }),
                    recipientIds,
                    corrections,
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var result = await ODataJson.ReadAsync(saved);
        return result.RootElement.GetProperty("Id").GetInt64();
    }

    private static object OwnRow(decimal oldPrice, decimal newPrice, bool gross = false) =>
        new
        {
            IsDefault = false,
            OldPrice = oldPrice,
            PriceIncludesVat = gross,
            NewPrice = newPrice,
        };

    private static Task<HttpResponseMessage> PreviewAsync(
        HttpClient client,
        long itemId,
        params object[] rows
    ) =>
        client.PostAsync(
            "/odata/PreviewItemPriceIncrease",
            ODataJson.Body(new { ItemId = itemId, Rows = rows })
        );

    private static async Task<JsonDocument> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ODataJson.ReadAsync(response);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Preview_lists_the_corrections_a_raised_price_needs()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);
        await SaveGraveAsync(client, "G-B", [new Link(itemId, 95.00m)], recipients: 2);

        using var json = await OkAsync(await PreviewAsync(client, itemId, OwnRow(95.00m, 99.75m)));

        var rows = json.RootElement.GetProperty("Rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(0, rows[0].GetProperty("Problems").GetArrayLength());
        var corrections = json.RootElement.GetProperty("Corrections").EnumerateArray().ToList();
        Assert.NotEmpty(corrections);
        Assert.All(
            corrections,
            c =>
            {
                Assert.Equal("G-B", c.GetProperty("GraveNumber").GetString());
                Assert.Equal(itemId, c.GetProperty("ItemId").GetInt64());
                Assert.Equal(99.75m, c.GetProperty("EnteredPrice").GetDecimal());
                Assert.Equal(99.76m, c.GetProperty("CorrectedPrice").GetDecimal());
            }
        );
    }

    [Fact]
    public async Task Preview_writes_nothing()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);
        await SaveGraveAsync(client, "G-B", [new Link(itemId, 95.00m)], recipients: 2);
        int StoredCorrections()
        {
            using var scope = host.Services.CreateScope();
            return scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .GraveBillingCorrections.Count();
        }

        var before = StoredCorrections();

        using var preview = await OkAsync(
            await PreviewAsync(client, itemId, OwnRow(95.00m, 99.75m))
        );
        Assert.NotEmpty(preview.RootElement.GetProperty("Corrections").EnumerateArray());

        using var graves = await OkAsync(
            await client.GetAsync($"/odata/ItemGraves?itemId={itemId}")
        );
        var group = Assert.Single(graves.RootElement.GetProperty("Groups").EnumerateArray());
        Assert.Equal(95.00m, group.GetProperty("UnitPrice").GetDecimal());
        Assert.Equal(before, StoredCorrections());
    }

    [Fact]
    public async Task Preview_leaves_one_recipient_graves_and_cooperative_graves_without_corrections()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);
        await SaveGraveAsync(client, "G-A", [new Link(itemId, 95.00m)], recipients: 1);
        await SaveGraveAsync(
            client,
            "G-K",
            [new Link(itemId, 95.00m)],
            recipients: 0,
            cooperative: true
        );

        using var json = await OkAsync(await PreviewAsync(client, itemId, OwnRow(95.00m, 99.75m)));

        Assert.Equal(0, json.RootElement.GetProperty("Corrections").GetArrayLength());
        Assert.Equal(
            0,
            json.RootElement.GetProperty("Rows")[0].GetProperty("Problems").GetArrayLength()
        );
    }

    [Fact]
    public async Task A_raised_grave_is_checked_with_its_other_items()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);
        var otherId = await ItemAsync(client, "U-2", 40.01m);
        // 135,01 does not divide, so G-S is saved with its confirmed correction
        await SaveGraveAsync(
            client,
            "G-S",
            [new Link(itemId, 95.00m), new Link(otherId, 40.01m)],
            recipients: 2
        );

        // 139,76 divides on its own: checked alone, 99,75 would need a correction
        using var json = await OkAsync(await PreviewAsync(client, itemId, OwnRow(95.00m, 99.75m)));

        Assert.DoesNotContain(
            json.RootElement.GetProperty("Corrections").EnumerateArray(),
            c => c.GetProperty("GraveNumber").GetString() == "G-S"
        );
        Assert.Equal(
            0,
            json.RootElement.GetProperty("Rows")[0].GetProperty("Problems").GetArrayLength()
        );
    }

    [Fact]
    public void ProblemOf_names_the_first_failing_invoice()
    {
        var example = new BillingExample(
            BillingExampleKind.FullFirstHalf,
            new BillingPeriod(2027, 1, 1, 6)
        );
        var failing = new GraveBillingEvaluation([
            new BillingExampleResult(
                example,
                [],
                [new GraveShareProblem(19m, GraveShareProblemKind.NotDivisible, 9975, [])],
                []
            ),
        ]);

        Assert.Equal(
            new PriceIncreaseProblem(7, "G-X", HalfYearBilling.Label(example), 19m),
            ItemPriceIncreaseService.ProblemOf(7, "G-X", failing)
        );
        Assert.Null(ItemPriceIncreaseService.ProblemOf(7, "G-X", new GraveBillingEvaluation([])));
    }

    [Fact]
    public async Task Preview_refuses_an_own_row_at_the_default_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);

        await AssertRefusedAsync(
            await PreviewAsync(client, itemId, OwnRow(120.00m, 126.00m)),
            "PricesChanged"
        );
    }

    [Fact]
    public async Task Preview_refuses_a_default_row_on_an_item_without_default()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", null);

        await AssertRefusedAsync(
            await PreviewAsync(
                client,
                itemId,
                new
                {
                    IsDefault = true,
                    OldPrice = 120.00m,
                    PriceIncludesVat = false,
                    NewPrice = 126.00m,
                }
            ),
            "PricesChanged"
        );
    }

    [Fact]
    public async Task Preview_refuses_a_price_with_three_decimals()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);

        await AssertRefusedAsync(
            await PreviewAsync(client, itemId, OwnRow(95.00m, 99.755m)),
            "InvalidPrice"
        );
    }

    [Fact]
    public async Task Preview_refuses_a_price_below_one_cent()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, "U-1", 120.00m);

        await AssertRefusedAsync(
            await PreviewAsync(client, itemId, OwnRow(95.00m, 0m)),
            "InvalidPrice"
        );
    }

    [Fact]
    public async Task Preview_of_an_unknown_item_is_not_found()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await PreviewAsync(client, 4711, OwnRow(95.00m, 99.75m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
