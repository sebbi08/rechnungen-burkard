using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveTests
{
    private async Task<(long ItemId, long RecipientId)> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 120.00m,
                }
            )
        );
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new { Name = "Anna Beispiel", Address = "Hauptstraße 1\n99999 Musterdorf" }
            )
        );
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        return (
            itemJson.RootElement.GetProperty("Id").GetInt64(),
            recipientJson.RootElement.GetProperty("Id").GetInt64()
        );
    }

    /// <summary>A grave saved the way the dialog saves it: in one request with
    /// its links. Answers the grave's id.</summary>
    private static async Task<long> SaveGraveAsync(
        HttpClient client,
        string graveNumber,
        object[] items,
        params long[] recipientIds
    )
    {
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
                    items,
                    recipientIds,
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var json = await ODataJson.ReadAsync(saved);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    // The sets are read-only: a grave is written through SaveGrave, in one
    // request with its links, which is where the checks live. Writing a grave
    // or a link directly would put a grave together past every one of them.
    [Theory]
    [InlineData("/odata/Graves")]
    [InlineData("/odata/GraveItems")]
    [InlineData("/odata/GraveRecipients")]
    public async Task Writing_a_grave_set_directly_is_not_offered(string path)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            path,
            ODataJson.Body(
                new
                {
                    GraveNumber = "X-1",
                    GraveId = 1,
                    ItemId = 1,
                }
            )
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static object[] AllYearLink(long itemId) =>
        new object[]
        {
            new
            {
                itemId,
                count = 1,
                unitPrice = (decimal?)null,
            },
        };

    [Fact]
    public async Task Create_grave_with_item_and_recipient_and_find_it_in_the_overview()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);

        var graveId = await SaveGraveAsync(
            client,
            "A-001",
            new object[]
            {
                new
                {
                    itemId,
                    count = 2,
                    unitPrice = (decimal?)null,
                },
            },
            recipientId
        );

        var grave = await client.GetAsync($"/odata/Graves({graveId})");
        Assert.Equal(HttpStatusCode.OK, grave.StatusCode);
        using var graveJson = await ODataJson.ReadAsync(grave);
        Assert.NotEqual(default, graveJson.RootElement.GetProperty("CreatedAt").GetString());
        Assert.Equal(
            2,
            graveJson.RootElement.GetProperty("Items")[0].GetProperty("Count").GetInt32()
        );

        var status = await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var statusJson = await ODataJson.ReadAsync(status);
        var row = Assert.Single(statusJson.RootElement.GetProperty("value").EnumerateArray());
        Assert.Equal(graveId, row.GetProperty("Id").GetInt64());
        Assert.Equal("A-001", row.GetProperty("GraveNumber").GetString());
    }
}
