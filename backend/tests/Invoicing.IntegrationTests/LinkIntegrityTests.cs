using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

// Fresh host per test: the same grave/item/recipient ids would otherwise
// collide with earlier tests in a shared database (see GraveTests).
public class LinkIntegrityTests
{
    private async Task<(long ItemId, long RecipientId)> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "9001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 100.00m,
                }
            )
        );
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = "Familie Mahr", Address = "Altstraße 1\n11111 Altstadt" })
        );
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        return (
            itemJson.RootElement.GetProperty("Id").GetInt64(),
            recipientJson.RootElement.GetProperty("Id").GetInt64()
        );
    }

    /// <summary>A grave billing the item to the recipients, saved the way the
    /// dialog saves it.</summary>
    private static Task<HttpResponseMessage> SaveGraveAsync(
        HttpClient client,
        long itemId,
        params long[] recipientIds
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "L-001",
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
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds,
                }
            )
        );

    [Fact]
    public async Task Deleting_item_linked_to_grave_is_rejected_with_ItemInUse()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);
        var saved = await SaveGraveAsync(client, itemId, recipientId);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var delete = await client.DeleteAsync($"/odata/InvoiceItems({itemId})");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        using var json = await ODataJson.ReadAsync(delete);
        Assert.Equal(
            "ItemInUse",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Deleting_recipient_linked_to_grave_is_rejected_with_RecipientInUse()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);
        var saved = await SaveGraveAsync(client, itemId, recipientId);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var delete = await client.DeleteAsync($"/odata/Recipients({recipientId})");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        using var json = await ODataJson.ReadAsync(delete);
        Assert.Equal(
            "RecipientInUse",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Saving_a_grave_with_the_same_recipient_twice_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);

        var saved = await SaveGraveAsync(client, itemId, recipientId, recipientId);

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        using var json = await ODataJson.ReadAsync(saved);
        Assert.Equal(
            "GraveRecipientLinkExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
}
