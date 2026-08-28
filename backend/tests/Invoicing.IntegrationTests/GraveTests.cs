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
        var item = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "3001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = 120.00m,
        }));
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Anna Beispiel",
            Address = "Hauptstraße 1\n99999 Musterdorf",
        }));
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        return (itemJson.RootElement.GetProperty("Id").GetInt64(),
                recipientJson.RootElement.GetProperty("Id").GetInt64());
    }

    [Fact]
    public async Task Create_grave_with_item_and_recipient_and_search_by_number()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);

        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "A-001",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        Assert.Equal(HttpStatusCode.Created, grave.StatusCode);
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();
        Assert.NotEqual(default, graveJson.RootElement.GetProperty("CreatedAt").GetString());

        var linkItem = await client.PostAsync("/odata/GraveItems", ODataJson.Body(new
        {
            GraveId = graveId,
            ItemId = itemId,
            Count = 2,
        }));
        Assert.Equal(HttpStatusCode.Created, linkItem.StatusCode);

        var linkRecipient = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new
        {
            GraveId = graveId,
            RecipientId = recipientId,
        }));
        Assert.Equal(HttpStatusCode.Created, linkRecipient.StatusCode);

        var search = await client.GetAsync("/odata/SearchGraves?term=A-001");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Single(searchJson.RootElement.GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task Search_graves_by_recipient_name_matches_case_insensitively()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, recipientId) = await SeedAsync(client);

        await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "B-002",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        // fetch grave id from listing
        var list = await client.GetAsync("/odata/Graves?$filter=GraveNumber eq 'B-002'");
        using var listJson = await ODataJson.ReadAsync(list);
        var graveId = listJson.RootElement.GetProperty("value")[0].GetProperty("Id").GetInt64();
        await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new { GraveId = graveId, RecipientId = recipientId }));

        var search = await client.GetAsync("/odata/SearchGraves?term=beispiel");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Single(searchJson.RootElement.GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task Search_graves_by_recipient_name_with_umlauts_matches_case_insensitively()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var recipient = await client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Özdemir",
            Address = "Umlautweg 1\n99999 Musterdorf",
        }));
        Assert.Equal(HttpStatusCode.Created, recipient.StatusCode);
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        var recipientId = recipientJson.RootElement.GetProperty("Id").GetInt64();

        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "U-001",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        Assert.Equal(HttpStatusCode.Created, grave.StatusCode);
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();

        var link = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new
        {
            GraveId = graveId,
            RecipientId = recipientId,
        }));
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        foreach (var term in new[] { "özdemir", "ÖZDEMIR" })
        {
            var search = await client.GetAsync("/odata/SearchGraves?term=" + Uri.EscapeDataString(term));
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            using var searchJson = await ODataJson.ReadAsync(search);
            var value = searchJson.RootElement.GetProperty("value").EnumerateArray().ToList();
            Assert.Single(value);
            Assert.Equal(graveId, value[0].GetProperty("Id").GetInt64());
            Assert.Equal("Özdemir", value[0].GetProperty("RecipientNames")[0].GetString());
        }
    }

    [Fact]
    public async Task Create_grave_item_with_zero_count_is_rejected()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, _) = await SeedAsync(client);
        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "C-003",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();

        var link = await client.PostAsync("/odata/GraveItems", ODataJson.Body(new
        {
            GraveId = graveId,
            ItemId = itemId,
            Count = 0,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
    }
}
