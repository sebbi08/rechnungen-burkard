using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>The grave dialog collects the grave, its items and its recipients
/// client-side and saves them in one request.</summary>
public class SaveGraveTests
{
    private async Task<(long PricedItemId, long UnpricedItemId, long RecipientId, long OtherRecipientId)> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var priced = await ReadIdAsync(client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "4001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = 120.00m,
        })));
        var unpriced = await ReadIdAsync(client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "4002",
            Description = "Sonderleistung",
            VatRate = 7.0m,
            DefaultPrice = (decimal?)null,
        })));
        var recipient = await ReadIdAsync(client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Anna Beispiel",
            Address = "Hauptstraße 1\n99999 Musterdorf",
        })));
        var other = await ReadIdAsync(client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Bert Beispiel",
            Address = "Nebenweg 2\n99999 Musterdorf",
        })));
        return (priced, unpriced, recipient, other);
    }

    private static async Task<long> ReadIdAsync(Task<HttpResponseMessage> call)
    {
        using var json = await ODataJson.ReadAsync(await call);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task Save_creates_a_grave_with_its_items_and_recipients_in_one_call()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, other) = await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveId = (long?)null,
            graveNumber = "S-001",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = pricedItem, count = 2, unitPrice = (decimal?)null } },
            recipientIds = new[] { recipient, other },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var graveId = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves
            .Include(g => g.Items)
            .Include(g => g.Recipients)
            .SingleAsync(g => g.Id == graveId);
        Assert.Equal("S-001", grave.GraveNumber);
        Assert.NotEqual(default, grave.CreatedAt);
        var item = Assert.Single(grave.Items);
        Assert.Equal(pricedItem, item.ItemId);
        Assert.Equal(2, item.Count);
        Assert.Null(item.UnitPrice);
        Assert.Equal(new[] { recipient, other }.OrderBy(id => id),
            grave.Recipients.Select(r => r.RecipientId).OrderBy(id => id));
    }

    [Fact]
    public async Task Save_replaces_the_items_of_an_existing_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, unpricedItem, recipient, _) = await SeedAsync(client);

        var created = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-002",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = pricedItem, count = 1, unitPrice = (decimal?)null } },
            recipientIds = new[] { recipient },
        }));
        using var createdJson = await ODataJson.ReadAsync(created);
        var graveId = createdJson.RootElement.GetProperty("Id").GetInt64();

        // the same item stays (so the unique (GraveId, ItemId) index sees a re-add),
        // the second one is new; the recipient set is unchanged, since a grave's
        // recipients are fixed once it exists (see GraveRecipientImmutabilityTests)
        var updated = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveId,
            graveNumber = "S-002b",
            startDate = "2027-01-01",
            endDate = "2041-12-31",
            items = new object[]
            {
                new { itemId = pricedItem, count = 3, unitPrice = (decimal?)null },
                new { itemId = unpricedItem, count = 1, unitPrice = (decimal?)45.50m },
            },
            recipientIds = new[] { recipient },
        }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves
            .Include(g => g.Items)
            .Include(g => g.Recipients)
            .SingleAsync(g => g.Id == graveId);
        Assert.Equal("S-002b", grave.GraveNumber);
        Assert.Equal(2, grave.Items.Count);
        Assert.Equal(3, grave.Items.Single(i => i.ItemId == pricedItem).Count);
        Assert.Equal(45.50m, grave.Items.Single(i => i.ItemId == unpricedItem).UnitPrice);
        Assert.Equal(recipient, Assert.Single(grave.Recipients).RecipientId);
        // the replaced item links are gone, not orphaned
        Assert.Equal(2, await db.GraveItems.CountAsync());
        Assert.Equal(1, await db.GraveRecipients.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_an_item_that_has_neither_a_link_price_nor_a_default_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, unpricedItem, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-003",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = unpricedItem, count = 1, unitPrice = (decimal?)null } },
            recipientIds = new[] { recipient },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("PriceRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Graves.AnyAsync(g => g.GraveNumber == "S-003"));
    }

    [Fact]
    public async Task Save_accepts_an_unpriced_item_when_the_link_carries_a_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (_, unpricedItem, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-004",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = unpricedItem, count = 1, unitPrice = (decimal?)33.00m } },
            recipientIds = new[] { recipient },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.GraveItems.SingleAsync();
        Assert.Equal(33.00m, link.UnitPrice);
    }

    [Theory]
    [InlineData("", "2026-01-01", "GraveNumberRequired")]
    [InlineData("S-005", "not-a-date", "InvalidDate")]
    public async Task Save_rejects_invalid_grave_fields(string graveNumber, string startDate, string expectedCode)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber,
            startDate,
            endDate = "2040-12-31",
            items = new List<object>(),
            recipientIds = new List<long>(),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Save_rejects_a_non_positive_count()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-006",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = pricedItem, count = 0, unitPrice = (decimal?)null } },
            recipientIds = new[] { recipient },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("InvalidCount", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Save_rejects_the_same_item_twice()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var response = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-007",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[]
            {
                new { itemId = pricedItem, count = 1, unitPrice = (decimal?)null },
                new { itemId = pricedItem, count = 2, unitPrice = (decimal?)null },
            },
            recipientIds = new[] { recipient },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("GraveItemLinkExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Grave_details_expose_the_link_price_for_the_dialog()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (pricedItem, _, recipient, _) = await SeedAsync(client);

        var created = await client.PostAsync("/odata/SaveGrave", ODataJson.Body(new
        {
            graveNumber = "S-008",
            startDate = "2026-01-01",
            endDate = "2040-12-31",
            items = new[] { new { itemId = pricedItem, count = 1, unitPrice = (decimal?)99.00m } },
            recipientIds = new[] { recipient },
        }));
        using var createdJson = await ODataJson.ReadAsync(created);
        var graveId = createdJson.RootElement.GetProperty("Id").GetInt64();

        var details = await client.GetAsync($"/odata/Graves({graveId})");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailsJson = await ODataJson.ReadAsync(details);
        var link = detailsJson.RootElement.GetProperty("Items")[0];
        Assert.Equal(99.00m, link.GetProperty("UnitPrice").GetDecimal());
        Assert.Equal(120.00m, link.GetProperty("Item").GetProperty("DefaultPrice").GetDecimal());
    }
}
