using System.Net;
using System.Net.Http;
using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A cooperative grave is listed but never invoiced, so saving one asks
/// for no recipient, no price and no all-year item.</summary>
public class CooperativeGraveTests
{
    private sealed record Seed(long PricedItem, long UnpricedItem, long Anna, long Bert, long Carl);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var priced = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4001",
                        Description = "Grabpflege",
                        VatRate = 7.0m,
                        DefaultPrice = 120.00m,
                    }
                )
            )
        );
        var unpriced = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4002",
                        Description = "Sonderleistung",
                        VatRate = 7.0m,
                        DefaultPrice = (decimal?)null,
                    }
                )
            )
        );
        return new Seed(
            priced,
            unpriced,
            await RecipientAsync(client, "Anna Beispiel"),
            await RecipientAsync(client, "Bert Beispiel"),
            await RecipientAsync(client, "Carl Beispiel")
        );
    }

    private static Task<long> RecipientAsync(HttpClient client, string name) =>
        ReadIdAsync(
            client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(new { Name = name, Address = "Hauptstraße 1\n99999 Musterdorf" })
            )
        );

    private static async Task<long> ReadIdAsync(Task<HttpResponseMessage> call)
    {
        using var json = await ODataJson.ReadAsync(await call);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client,
        long? graveId,
        bool? isCooperative,
        object[] items,
        long[] recipientIds,
        object[]? corrections = null
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "K-001",
                    fieldNumber = "F-1",
                    siteName = "Genossenschaft Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = (string?)null,
                    isCooperative,
                    items,
                    recipientIds,
                    corrections = corrections ?? [],
                }
            )
        );

    private static object Link(long itemId) =>
        new
        {
            itemId,
            count = 1,
            unitPrice = (decimal?)null,
        };

    private static async Task<long> SavedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<JsonDocument> GetGraveAsync(HttpClient client, long id) =>
        await ODataJson.ReadAsync(await client.GetAsync($"/odata/Graves({id})"));

    [Fact]
    public async Task A_cooperative_grave_saves_without_recipients_or_items()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var id = await SavedIdAsync(await SaveAsync(client, null, true, [], []));

        using var grave = await GetGraveAsync(client, id);
        Assert.True(grave.RootElement.GetProperty("IsCooperative").GetBoolean());
        Assert.False(grave.RootElement.GetProperty("HasInvoices").GetBoolean());
        Assert.Equal(0, grave.RootElement.GetProperty("Items").GetArrayLength());
        Assert.Equal(0, grave.RootElement.GetProperty("Recipients").GetArrayLength());
    }

    [Fact]
    public async Task A_cooperative_grave_may_link_an_item_without_a_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(seed.UnpricedItem)], [])
        );

        using var grave = await GetGraveAsync(client, id);
        Assert.Equal(1, grave.RootElement.GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public async Task A_cooperative_grave_stores_no_corrections()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        // 100,00 EUR over three recipients does not divide, which would call
        // for corrections on an ordinary grave
        var item = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4003",
                        Description = "Teilbar nicht",
                        VatRate = 7.0m,
                        DefaultPrice = 100.00m,
                    }
                )
            )
        );
        var id = await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(item)], [seed.Anna, seed.Bert, seed.Carl])
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.GraveBillingCorrections.Where(c => c.GraveId == id).ToListAsync());
    }

    [Fact]
    public async Task A_never_invoiced_cooperative_grave_may_change_its_recipients()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(await SaveAsync(client, null, true, [], [seed.Anna]));
        await SavedIdAsync(await SaveAsync(client, id, true, [], [seed.Bert]));

        using var grave = await GetGraveAsync(client, id);
        var recipients = grave.RootElement.GetProperty("Recipients");
        Assert.Equal(1, recipients.GetArrayLength());
        Assert.Equal(
            seed.Bert,
            recipients[0].GetProperty("Recipient").GetProperty("Id").GetInt64()
        );
    }

    [Fact]
    public async Task Unticking_a_cooperative_grave_without_recipients_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(await SaveAsync(client, null, true, [], []));
        await AssertRefusedAsync(
            await SaveAsync(client, id, false, [Link(seed.PricedItem)], []),
            "NoRecipients"
        );

        // with a recipient the ordinary rules go on: no all-year item, no save
        var quarter = await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "4004",
                        Description = "Quartal",
                        VatRate = 7.0m,
                        DefaultPrice = 10.00m,
                        Quarter = "Summer",
                    }
                )
            )
        );
        await AssertRefusedAsync(
            await SaveAsync(client, id, false, [Link(quarter)], [seed.Anna]),
            "AllYearItemRequired"
        );
    }

    [Fact]
    public async Task Unticking_applies_the_ordinary_rules()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(await SaveAsync(client, null, true, [], [seed.Anna]));
        await SavedIdAsync(
            await SaveAsync(client, id, false, [Link(seed.PricedItem)], [seed.Anna])
        );

        using var grave = await GetGraveAsync(client, id);
        Assert.False(grave.RootElement.GetProperty("IsCooperative").GetBoolean());
    }

    [Fact]
    public async Task The_flag_cannot_change_once_the_grave_has_invoices()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(
            await SaveAsync(client, null, false, [Link(seed.PricedItem)], [seed.Anna])
        );
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using (var grave = await GetGraveAsync(client, id))
        {
            Assert.True(grave.RootElement.GetProperty("HasInvoices").GetBoolean());
        }

        await AssertRefusedAsync(
            await SaveAsync(client, id, true, [Link(seed.PricedItem)], [seed.Anna]),
            "CooperativeFlagFixed"
        );
    }

    [Fact]
    public async Task An_ordinary_grave_keeps_its_rules()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        await AssertRefusedAsync(
            await SaveAsync(client, null, null, [Link(seed.PricedItem)], []),
            "NoRecipients"
        );
    }
}
