using System.Net;
using System.Net.Http;
using System.Text.Json;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A price change that reaches a grave shows in its Verlauf: the
/// increase of an item's prices, and the edit of its default that pins the
/// price of every grave that billed the old one.</summary>
public class ItemPriceAuditTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<long> ItemAsync(ApiTestHost host, decimal defaultPrice)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem
        {
            Number = "1001",
            Description = "Grabpflege",
            VatRate = 19m,
            DefaultPrice = defaultPrice,
        };
        db.InvoiceItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static async Task<long> GraveAsync(
        HttpClient client,
        string number,
        long itemId,
        decimal? price,
        int recipients = 1
    )
    {
        var ids = new List<long>();
        for (var i = 0; i < recipients; i++)
        {
            var created = await client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(new { Name = $"Empfänger {number} {i}", Address = "Weg 1" })
            );
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = await ODataJson.ReadAsync(created);
            ids.Add(json.RootElement.GetProperty("Id").GetInt64());
        }

        var saved = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = number,
                    fieldNumber = "F-1",
                    siteName = "Grabstätte Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = "2040-12-31",
                    isCooperative = false,
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = price,
                        },
                    },
                    recipientIds = ids,
                    corrections = Array.Empty<object>(),
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var result = await ODataJson.ReadAsync(saved);
        return result.RootElement.GetProperty("Id").GetInt64();
    }

    private static async Task<List<AuditEntry>> EntriesAsync(ApiTestHost host, string action)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db
            .AuditEntries.AsNoTracking()
            .Where(e => e.Action == action)
            .OrderBy(e => e.Id)
            .ToListAsync();
    }

    private static List<AuditChange> Changes(AuditEntry entry) =>
        JsonSerializer.Deserialize<List<AuditChange>>(entry.ChangesJson, Web)!;

    private static AuditChange Change(AuditEntry entry, string field) =>
        Assert.Single(Changes(entry), c => c.Field == field);

    private static Dictionary<string, string> Details(AuditEntry entry) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(entry.DetailsJson!, Web)!;

    private static Task<HttpResponseMessage> ApplyAsync(
        HttpClient client,
        long itemId,
        object[] rows,
        object[]? corrections = null
    ) =>
        client.PostAsync(
            "/odata/ApplyItemPriceIncrease",
            ODataJson.Body(
                new
                {
                    ItemId = itemId,
                    Rows = rows,
                    Corrections = corrections ?? [],
                }
            )
        );

    private static object Row(bool isDefault, decimal old, decimal @new, params long[] graves) =>
        new
        {
            IsDefault = isDefault,
            OldPrice = old,
            NewPrice = @new,
            GraveIds = graves,
        };

    private static Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        long itemId,
        object fields
    ) =>
        client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({itemId})")
            {
                Content = ODataJson.Body(fields),
            }
        );

    [Fact]
    public async Task A_price_increase_records_on_a_grave_with_its_own_price()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(host, 120m);
        var grave = await GraveAsync(client, "G-1", itemId, 100m);

        var response = await ApplyAsync(client, itemId, [Row(false, 100m, 105m, grave)]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await EntriesAsync(host, AuditActions.ItemPriceIncreased));
        Assert.Equal(grave, entry.GraveId);
        Assert.Equal("1001 Grabpflege", Details(entry)["item"]);
        var price = Change(entry, "itemPrice");
        Assert.Equal("100.00", price.Old);
        Assert.Equal("105.00", price.New);
    }

    [Fact]
    public async Task A_price_increase_of_the_default_records_on_graves_billing_it()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(host, 100m);
        var grave = await GraveAsync(client, "G-1", itemId, null);

        var response = await ApplyAsync(client, itemId, [Row(true, 100m, 105m, grave)]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await EntriesAsync(host, AuditActions.ItemPriceIncreased));
        Assert.Equal(grave, entry.GraveId);
        var price = Change(entry, "itemPrice");
        Assert.Equal("100.00", price.Old);
        Assert.Equal("105.00", price.New);
    }

    [Fact]
    public async Task A_price_increase_records_changed_corrections()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(host, 120m);
        var grave = await GraveAsync(client, "G-1", itemId, 95m, recipients: 2);
        using var preview = await ODataJson.ReadAsync(
            await client.PostAsync(
                "/odata/PreviewItemPriceIncrease",
                ODataJson.Body(new { ItemId = itemId, Rows = new[] { Row(false, 95m, 99.75m) } })
            )
        );
        var confirmed = preview
            .RootElement.GetProperty("Corrections")
            .EnumerateArray()
            .Select(c =>
                (object)
                    new
                    {
                        GraveId = c.GetProperty("GraveId").GetInt64(),
                        Kind = c.GetProperty("Kind").GetString(),
                        Year = c.GetProperty("Year").ValueKind == JsonValueKind.Null
                            ? (int?)null
                            : c.GetProperty("Year").GetInt32(),
                        Half = c.GetProperty("Half").GetInt32(),
                        VatRate = c.GetProperty("VatRate").GetDecimal(),
                        ItemId = c.GetProperty("ItemId").GetInt64(),
                        CorrectedPrice = c.GetProperty("CorrectedPrice").GetDecimal(),
                    }
            )
            .ToArray();
        Assert.NotEmpty(confirmed);

        var response = await ApplyAsync(
            client,
            itemId,
            [Row(false, 95m, 99.75m, grave)],
            confirmed
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await EntriesAsync(host, AuditActions.ItemPriceIncreased));
        Assert.Contains(Changes(entry), c => c.Field == "correction");
    }

    [Fact]
    public async Task Editing_the_default_pins_it_and_records_why()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(host, 100m);
        var onDefault = await GraveAsync(client, "G-1", itemId, null);
        var other = await GraveAsync(client, "G-2", itemId, null);
        var own = await GraveAsync(client, "G-3", itemId, 90m);

        var response = await PatchAsync(client, itemId, new { DefaultPrice = 110m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entries = await EntriesAsync(host, AuditActions.ItemDefaultPriceChanged);
        Assert.Equal(
            new[] { onDefault, other }.OrderBy(i => i),
            entries.Select(e => e.GraveId!.Value).OrderBy(i => i)
        );
        Assert.DoesNotContain(entries, e => e.GraveId == own);
        foreach (var entry in entries)
        {
            var details = Details(entry);
            Assert.Equal("1001 Grabpflege", details["item"]);
            Assert.Equal("100.00", details["old"]);
            Assert.Equal("110.00", details["new"]);
            var source = Change(entry, "itemPriceSource");
            Assert.Equal("default", source.Old);
            Assert.Equal("own", source.New);
            Assert.DoesNotContain(Changes(entry), c => c.Field == "itemPrice");
        }
    }

    [Fact]
    public async Task Editing_a_default_no_grave_bills_writes_nothing()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await ItemAsync(host, 100m);
        await GraveAsync(client, "G-1", itemId, 90m);

        var response = await PatchAsync(client, itemId, new { DefaultPrice = 110m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await EntriesAsync(host, AuditActions.ItemDefaultPriceChanged));
    }
}
