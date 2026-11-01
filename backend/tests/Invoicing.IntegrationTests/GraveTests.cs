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
    public async Task Create_grave_with_item_and_recipient_and_search_by_number()
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
        var (itemId, recipientId) = await SeedAsync(client);
        await SaveGraveAsync(client, "B-002", AllYearLink(itemId), recipientId);

        var search = await client.GetAsync("/odata/SearchGraves?term=beispiel");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Single(searchJson.RootElement.GetProperty("value").EnumerateArray());
    }

    // "Anna Beispiel" is the recipient, "B-002" the grave: the words of a term
    // are looked for one by one, in whichever field holds them.
    [Theory]
    [InlineData("Beispiel Anna", 1)]
    [InlineData("b-002  beispiel", 1)]
    [InlineData("Beispiel Schmidt", 0)]
    public async Task Search_graves_takes_every_word_of_the_term_in_any_order(
        string term,
        int expected
    )
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);
        await SaveGraveAsync(client, "B-002", AllYearLink(itemId), recipientId);

        var search = await client.GetAsync(
            "/odata/SearchGraves?term=" + Uri.EscapeDataString(term)
        );
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Equal(expected, searchJson.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Search_graves_by_recipient_name_with_umlauts_matches_case_insensitively()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (itemId, _) = await SeedAsync(client);

        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = "Özdemir", Address = "Umlautweg 1\n99999 Musterdorf" })
        );
        Assert.Equal(HttpStatusCode.Created, recipient.StatusCode);
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        var recipientId = recipientJson.RootElement.GetProperty("Id").GetInt64();
        var graveId = await SaveGraveAsync(client, "U-001", AllYearLink(itemId), recipientId);

        foreach (var term in new[] { "özdemir", "ÖZDEMIR" })
        {
            var search = await client.GetAsync(
                "/odata/SearchGraves?term=" + Uri.EscapeDataString(term)
            );
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            using var searchJson = await ODataJson.ReadAsync(search);
            var value = searchJson.RootElement.GetProperty("value").EnumerateArray().ToList();
            Assert.Single(value);
            Assert.Equal(graveId, value[0].GetProperty("Id").GetInt64());
            Assert.Equal("Özdemir", value[0].GetProperty("RecipientNames")[0].GetString());
        }
    }

    [Fact]
    public async Task A_grave_item_link_carries_its_vat_included_flag()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));

        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "L-100",
                    Description = "Grabpflege",
                    VatRate = 19.0m,
                    DefaultPrice = 120.00m,
                }
            )
        );
        using var itemJson = await ODataJson.ReadAsync(item);
        var itemId = itemJson.RootElement.GetProperty("Id").GetInt64();
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = "Anna Beispiel", Address = "Hauptstraße 1" })
        );
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        var recipientId = recipientJson.RootElement.GetProperty("Id").GetInt64();

        // 120.00 gross at 19 % to one recipient: 100.84 net plus 19.16 VAT
        var graveId = await SaveGraveAsync(
            client,
            "L-1",
            new object[]
            {
                new
                {
                    itemId,
                    count = 1,
                    unitPrice = (decimal?)null,
                    priceIncludesVat = true,
                },
            },
            recipientId
        );

        var links = await client.GetAsync($"/odata/GraveItems?$filter=GraveId eq {graveId}");
        using var linksJson = await ODataJson.ReadAsync(links);
        var link = Assert.Single(linksJson.RootElement.GetProperty("value").EnumerateArray());
        Assert.True(link.GetProperty("PriceIncludesVat").GetBoolean());
    }
}
