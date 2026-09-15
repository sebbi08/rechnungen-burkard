using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceItemTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public InvoiceItemTests(ApiTestHost host) => _host = host;

    private async Task SeedVatRateAsync(HttpClient client) =>
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));

    private async Task<long> CreateItemAsync(HttpClient client, string number, decimal? price)
    {
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = number,
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = price,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await GetCreatedIdAsync(created);
    }

    /// <summary>A grave that bills the item to one recipient, saved the way the
    /// dialog saves it. Answers the grave's id.</summary>
    private async Task<long> LinkGraveAsync(
        HttpClient client,
        long itemId,
        string graveNumber,
        decimal? unitPrice
    )
    {
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = $"Empfänger {graveNumber}", Address = "Weg 1" })
        );
        Assert.Equal(HttpStatusCode.Created, recipient.StatusCode);
        var saved = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber,
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice,
                        },
                    },
                    recipientIds = new[] { await GetCreatedIdAsync(recipient) },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        return await GetCreatedIdAsync(saved);
    }

    /// <summary>A grave billing an item that has no price at all: nothing can
    /// build one any more, so the one this test needs is written directly.</summary>
    private async Task<long> LinkGraveDirectlyAsync(long itemId, string graveNumber)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = new Burkard.Invoicing.Domain.Entities.Grave
        {
            GraveNumber = graveNumber,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        db.GraveItems.Add(
            new Burkard.Invoicing.Domain.Entities.GraveItem
            {
                GraveId = grave.Id,
                ItemId = itemId,
                Count = 1,
            }
        );
        await db.SaveChangesAsync();
        return grave.Id;
    }

    private static async Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        long itemId,
        object fields
    )
    {
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({itemId})")
        {
            Content = ODataJson.Body(fields),
        };
        return await client.SendAsync(patch);
    }

    private async Task<decimal?> LinkPriceAsync(long graveId, long itemId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.GraveItems.SingleAsync(l => l.GraveId == graveId && l.ItemId == itemId);
        return link.UnitPrice;
    }

    private async Task<decimal?> DefaultPriceAsync(long itemId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.InvoiceItems.SingleAsync(i => i.Id == itemId)).DefaultPrice;
    }

    [Fact]
    public async Task Create_item_without_default_price_stores_null_price()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = (decimal?)null,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("DefaultPrice").ValueKind);
    }

    [Fact]
    public async Task Create_item_with_unknown_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "1002",
                    Description = "Unknown rate",
                    VatRate = 99m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "VatRateNotConfigured",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_item_with_duplicate_number_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var first = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "2001",
                    Description = "First",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "2001",
                    Description = "Second",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal(
            "ItemNumberExists",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_item_with_empty_description_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3001",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = await GetCreatedIdAsync(created);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({id})")
        {
            Content = ODataJson.Body(new { Description = "" }),
        };
        var response = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "ItemDescriptionRequired",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_item_with_unconfigured_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3002",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = await GetCreatedIdAsync(created);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({id})")
        {
            Content = ODataJson.Body(new { VatRate = 99m }),
        };
        var response = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "VatRateNotConfigured",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    // Prices are cents. A third decimal would be judged rounded by the grave
    // split check and billed unrounded by generation.
    [Fact]
    public async Task Create_item_with_a_price_beyond_cents_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3003",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 120.004m,
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidPrice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_item_with_a_price_beyond_cents_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "3004",
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 120.00m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = await GetCreatedIdAsync(created);
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({id})")
        {
            Content = ODataJson.Body(new { DefaultPrice = 120.004m }),
        };
        var response = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidPrice",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    // An item's VAT rate is fixed once a grave bills it: the rate is read from
    // the item at generation and never copied onto the link, so changing it
    // would move every linked grave's split at once.
    [Fact]
    public async Task Patch_vat_rate_of_a_linked_item_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        var itemId = await CreateItemAsync(client, "3005", 100.00m);
        await LinkGraveAsync(client, itemId, "I-3005", null);

        var response = await PatchAsync(client, itemId, new { VatRate = 19.0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "VatRateImmutable",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_vat_rate_of_an_unlinked_item_is_accepted()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        var itemId = await CreateItemAsync(client, "3006", 100.00m);

        var response = await PatchAsync(client, itemId, new { VatRate = 19.0m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // A changed default applies to graves linked from now on. A grave that was
    // billing the old default keeps it, written onto the link as its own price,
    // so nothing already set up changes what it bills.
    [Fact]
    public async Task Patch_default_price_keeps_linked_graves_on_the_old_price()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var itemId = await CreateItemAsync(client, "3007", 100.00m);
        var onDefault = await LinkGraveAsync(client, itemId, "I-3007-A", null);
        var ownPrice = await LinkGraveAsync(client, itemId, "I-3007-B", 95.00m);

        var response = await PatchAsync(client, itemId, new { DefaultPrice = 120.00m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(120.00m, await DefaultPriceAsync(itemId));
        Assert.Equal(100.00m, await LinkPriceAsync(onDefault, itemId));
        Assert.Equal(95.00m, await LinkPriceAsync(ownPrice, itemId));
    }

    [Fact]
    public async Task Clearing_the_default_price_keeps_linked_graves_on_the_old_price()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var itemId = await CreateItemAsync(client, "3008", 100.00m);
        var onDefault = await LinkGraveAsync(client, itemId, "I-3008", null);

        var response = await PatchAsync(client, itemId, new { DefaultPrice = (decimal?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await DefaultPriceAsync(itemId));
        Assert.Equal(100.00m, await LinkPriceAsync(onDefault, itemId));
    }

    [Fact]
    public async Task Setting_a_default_price_where_there_was_none_leaves_links_following_it()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var itemId = await CreateItemAsync(client, "3009", null);
        var unpriced = await LinkGraveDirectlyAsync(itemId, "I-3009");

        var response = await PatchAsync(client, itemId, new { DefaultPrice = 50.00m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await LinkPriceAsync(unpriced, itemId));
    }

    [Fact]
    public async Task Create_item_with_scale_equivalent_vat_rate_is_accepted()
    {
        using var client = _host.CreateClient();
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));

        // only "19" is configured; "19.0" must be normalized and accepted
        var response = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "4001",
                    Description = "Grabpflege",
                    VatRate = 19.0m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(19m, json.RootElement.GetProperty("VatRate").GetDecimal());
    }

    private static async Task<long> GetCreatedIdAsync(HttpResponseMessage created)
    {
        using var json = await ODataJson.ReadAsync(created);
        return json.RootElement.GetProperty("Id").GetInt64();
    }
}
