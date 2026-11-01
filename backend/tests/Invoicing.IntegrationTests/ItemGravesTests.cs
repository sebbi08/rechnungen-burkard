using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Which graves bill an item, and at what price. The item dialog shows
/// this next to the default price, so a change can be judged against what the
/// graves already bill.</summary>
public class ItemGravesTests
{
    private static async Task<long> CreateAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsync(path, ODataJson.Body(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<long> ItemAsync(HttpClient client, decimal? defaultPrice)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        return await CreateAsync(
            client,
            "/odata/InvoiceItems",
            new
            {
                Number = "U-1",
                Description = "Grabpflege",
                VatRate = 7.0m,
                DefaultPrice = defaultPrice,
            }
        );
    }

    /// <summary>A grave billing the item to one recipient at the given price,
    /// saved the way the dialog saves it.</summary>
    private static async Task LinkAsync(
        HttpClient client,
        long itemId,
        string graveNumber,
        decimal? unitPrice,
        bool gross = false
    )
    {
        var recipientId = await CreateAsync(
            client,
            "/odata/Recipients",
            new { Name = $"Empfänger {graveNumber}", Address = "Weg 1" }
        );
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
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice,
                            priceIncludesVat = gross,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Fact]
    public async Task Graves_are_grouped_by_the_price_they_bill_with_the_default_groups_first()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, 100.00m);
        await LinkAsync(client, itemId, "G-4", 130.00m);
        // an own price equal to the default is the default, as far as the
        // dialog is concerned: it copies the default into the field
        await LinkAsync(client, itemId, "G-2", 100.00m);
        await LinkAsync(client, itemId, "G-3", 95.00m);
        await LinkAsync(client, itemId, "G-5", 100.00m, gross: true);
        await LinkAsync(client, itemId, "G-1", null);

        var response = await client.GetAsync($"/odata/ItemGraves?itemId={itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.True(json.RootElement.GetProperty("Linked").GetBoolean());
        Assert.Equal(100.00m, json.RootElement.GetProperty("DefaultPrice").GetDecimal());
        var groups = json
            .RootElement.GetProperty("Groups")
            .EnumerateArray()
            .Select(g =>
                (
                    Price: g.GetProperty("UnitPrice").GetDecimal(),
                    Gross: g.GetProperty("PriceIncludesVat").GetBoolean(),
                    IsDefault: g.GetProperty("IsDefault").GetBoolean(),
                    Graves: g.GetProperty("Graves")
                        .EnumerateArray()
                        .Select(x => x.GetProperty("GraveNumber").GetString())
                        .ToArray()
                )
            )
            .ToArray();

        Assert.Collection(
            groups,
            g =>
            {
                Assert.Equal((100.00m, false, true), (g.Price, g.Gross, g.IsDefault));
                Assert.Equal(new[] { "G-1", "G-2" }, g.Graves);
            },
            g =>
            {
                Assert.Equal((100.00m, true, true), (g.Price, g.Gross, g.IsDefault));
                Assert.Equal(new[] { "G-5" }, g.Graves);
            },
            g =>
            {
                Assert.Equal((95.00m, false, false), (g.Price, g.Gross, g.IsDefault));
                Assert.Equal(new[] { "G-3" }, g.Graves);
            },
            g =>
            {
                Assert.Equal((130.00m, false, false), (g.Price, g.Gross, g.IsDefault));
                Assert.Equal(new[] { "G-4" }, g.Graves);
            }
        );
        // each grave carries its id, so the dialog can link to it
        var first = json.RootElement.GetProperty("Groups")[0].GetProperty("Graves")[0];
        Assert.True(first.GetProperty("Id").GetInt64() > 0);
    }

    [Fact]
    public async Task An_item_no_grave_bills_is_not_linked()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(client, 100.00m);

        var response = await client.GetAsync($"/odata/ItemGraves?itemId={itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.False(json.RootElement.GetProperty("Linked").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("Groups").EnumerateArray());
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/ItemGraves?itemId=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
