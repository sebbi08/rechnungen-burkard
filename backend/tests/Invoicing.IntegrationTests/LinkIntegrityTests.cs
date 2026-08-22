using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

// Fresh host per test: the same grave/item/recipient ids would otherwise
// collide with earlier tests in a shared database (see GraveTests).
public class LinkIntegrityTests
{
    private async Task<(long GraveId, long ItemId, long RecipientId)> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var item = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "9001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = 100.00m,
        }));
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Familie Mahr",
            Address = "Altstraße 1\n11111 Altstadt",
        }));
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "L-001",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        using var graveJson = await ODataJson.ReadAsync(grave);
        return (graveJson.RootElement.GetProperty("Id").GetInt64(),
                itemJson.RootElement.GetProperty("Id").GetInt64(),
                recipientJson.RootElement.GetProperty("Id").GetInt64());
    }

    [Fact]
    public async Task Deleting_item_linked_to_grave_is_rejected_with_ItemInUse()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId, _) = await SeedAsync(client);

        var link = await client.PostAsync("/odata/GraveItems", ODataJson.Body(new
        {
            GraveId = graveId,
            ItemId = itemId,
            Count = 1,
        }));
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        var delete = await client.DeleteAsync($"/odata/InvoiceItems({itemId})");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        using var json = await ODataJson.ReadAsync(delete);
        Assert.Equal("ItemInUse", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleting_recipient_linked_to_grave_is_rejected_with_RecipientInUse()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, _, recipientId) = await SeedAsync(client);

        var link = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new
        {
            GraveId = graveId,
            RecipientId = recipientId,
        }));
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        var delete = await client.DeleteAsync($"/odata/Recipients({recipientId})");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        using var json = await ODataJson.ReadAsync(delete);
        Assert.Equal("RecipientInUse", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Duplicate_grave_item_link_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId, _) = await SeedAsync(client);

        var payload = new { GraveId = graveId, ItemId = itemId, Count = 1 };
        var first = await client.PostAsync("/odata/GraveItems", ODataJson.Body(payload));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync("/odata/GraveItems", ODataJson.Body(payload));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("GraveItemLinkExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Duplicate_grave_recipient_link_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, _, recipientId) = await SeedAsync(client);

        var payload = new { GraveId = graveId, RecipientId = recipientId };
        var first = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(payload));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(payload));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("GraveRecipientLinkExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
