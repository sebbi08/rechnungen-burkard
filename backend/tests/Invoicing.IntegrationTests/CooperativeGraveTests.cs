using System.Net;
using System.Net.Http;
using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
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

    [Fact]
    public async Task A_run_never_invoices_a_cooperative_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var cooperative = await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(seed.PricedItem)], [seed.Anna])
        );
        var ordinary = await SavedIdAsync(
            await SaveOrdinaryAsync(client, "K-002", seed.PricedItem, seed.Bert)
        );

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Invoices.Where(i => i.GraveId == cooperative).ToListAsync());
        Assert.NotEmpty(await db.Invoices.Where(i => i.GraveId == ordinary).ToListAsync());
        Assert.Empty(await db.GraveInvoiceRuns.Where(r => r.GraveId == cooperative).ToListAsync());
    }

    [Fact]
    public async Task A_run_with_only_cooperative_graves_due_is_refused_as_nothing_open()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(seed.PricedItem)], [seed.Anna])
        );

        var response = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 })
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "GravesAlreadyInvoiced",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task The_correction_precheck_ignores_cooperative_graves()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        // 100,00 EUR over three recipients would need a correction, and a
        // cooperative grave stores none
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
        await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(item)], [seed.Anna, seed.Bert, seed.Carl])
        );
        await SavedIdAsync(await SaveOrdinaryAsync(client, "K-002", seed.PricedItem, seed.Anna));

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.GraveInvoiceRuns.CountAsync());
    }

    [Fact]
    public async Task The_generator_refuses_a_cooperative_grave_directly()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);

        var id = await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(seed.PricedItem)], [seed.Anna])
        );

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync(g => g.Id == id);
        var generator = scope.ServiceProvider.GetRequiredService<GraveInvoiceGenerator>();

        var result = await generator.GenerateForGraveAsync(grave, 2026, 1, CancellationToken.None);

        Assert.Null(result.Invoices);
        Assert.Equal("Cooperative", result.SkipReason);
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    private static Task<HttpResponseMessage> SaveTermAsync(
        HttpClient client,
        string number,
        string start,
        string? end,
        bool isCooperative,
        object[] items,
        long[] recipientIds
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = number,
                    fieldNumber = "F-1",
                    siteName = "Term Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = start,
                    endDate = end,
                    isCooperative,
                    items,
                    recipientIds,
                    corrections = Array.Empty<object>(),
                }
            )
        );

    private static async Task<JsonDocument> StatusAsync(HttpClient client, int year, int half) =>
        await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/GraveInvoicingStatus?year={year}&half={half}")
        );

    private static JsonElement Row(JsonDocument status, string number) =>
        status
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Single(r => r.GetProperty("GraveNumber").GetString() == number);

    [Fact]
    public async Task The_overview_leaves_cooperative_graves_out()
    {
        // The overview is the list of graves the business bills: a cooperative
        // grave has a list of its own and is not shown here, billable links or not.
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        await SavedIdAsync(await SaveTermAsync(client, "K-010", "2026-01-01", null, true, [], []));
        await SavedIdAsync(
            await SaveTermAsync(
                client,
                "K-011",
                "2026-01-01",
                null,
                true,
                [Link(seed.PricedItem)],
                [seed.Anna]
            )
        );
        await SavedIdAsync(await SaveOrdinaryAsync(client, "A-001", seed.PricedItem, seed.Bert));

        foreach (var half in new[] { 1, 2 })
        {
            using var status = await StatusAsync(client, 2026, half);
            var numbers = status
                .RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(r => r.GetProperty("GraveNumber").GetString())
                .ToList();
            Assert.Equal(["A-001"], numbers);
        }
    }

    [Fact]
    public async Task An_unticked_grave_shows_open()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var id = await SavedIdAsync(
            await SaveAsync(client, null, true, [Link(seed.PricedItem)], [seed.Anna])
        );
        // invoiced for H1 2026, with a number that sorts before the unticked grave's
        await SavedIdAsync(await SaveOrdinaryAsync(client, "A-001", seed.PricedItem, seed.Bert));
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        await SavedIdAsync(
            await SaveAsync(client, id, false, [Link(seed.PricedItem)], [seed.Anna])
        );

        using var status = await StatusAsync(client, 2026, 1);
        var rows = status.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal(
            ["K-001", "A-001"],
            rows.Select(r => r.GetProperty("GraveNumber").GetString()).ToList()
        );
        Assert.False(rows[0].GetProperty("Invoiced").GetBoolean());
        Assert.True(rows[1].GetProperty("Invoiced").GetBoolean());
    }

    [Fact]
    public async Task Item_graves_mark_cooperative_graves()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        await SavedIdAsync(await SaveAsync(client, null, true, [Link(seed.PricedItem)], []));
        await SavedIdAsync(await SaveOrdinaryAsync(client, "K-002", seed.PricedItem, seed.Anna));

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/ItemGraves?itemId={seed.PricedItem}")
        );
        var graves = json
            .RootElement.GetProperty("Groups")
            .EnumerateArray()
            .SelectMany(g => g.GetProperty("Graves").EnumerateArray())
            .ToDictionary(
                g => g.GetProperty("GraveNumber").GetString()!,
                g => g.GetProperty("IsCooperative").GetBoolean()
            );
        Assert.True(graves["K-001"]);
        Assert.False(graves["K-002"]);
    }

    [Fact]
    public async Task The_graves_set_filters_cooperative_graves()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        await SavedIdAsync(await SaveAsync(client, null, true, [], [seed.Anna]));
        await SavedIdAsync(await SaveOrdinaryAsync(client, "K-002", seed.PricedItem, seed.Bert));

        var response = await client.GetAsync(
            "/odata/Graves?$filter=IsCooperative eq true"
                + "&$expand=Cemetery,Recipients($expand=Recipient)&$orderby=GraveNumber"
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var rows = json.RootElement.GetProperty("value");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("K-001", rows[0].GetProperty("GraveNumber").GetString());
        Assert.Equal(TestCemetery.Id, rows[0].GetProperty("Cemetery").GetProperty("Id").GetInt64());
        Assert.Equal(
            "Anna Beispiel",
            rows[0]
                .GetProperty("Recipients")[0]
                .GetProperty("Recipient")
                .GetProperty("Name")
                .GetString()
        );
    }

    private static Task<HttpResponseMessage> SaveSuccessorAsync(
        HttpClient client,
        long? succeedsGraveId,
        string start,
        bool isCooperative,
        object[] items,
        long[] recipientIds,
        string number = "K-001"
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    succeedsGraveId,
                    graveNumber = number,
                    fieldNumber = "F-1",
                    siteName = "Nachfolger",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = start,
                    endDate = (string?)null,
                    isCooperative,
                    items,
                    recipientIds,
                    corrections = Array.Empty<object>(),
                }
            )
        );

    private static async Task<string?> EndDateAsync(HttpClient client, long id)
    {
        using var grave = await GetGraveAsync(client, id);
        return grave.RootElement.GetProperty("EndDate").GetString();
    }

    [Fact]
    public async Task A_successor_ends_its_predecessor_the_month_before()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );

        var successor = await SavedIdAsync(
            await SaveSuccessorAsync(
                client,
                cooperative,
                "2026-07-01",
                false,
                [Link(seed.PricedItem)],
                [seed.Anna]
            )
        );

        Assert.StartsWith("2026-06-01", await EndDateAsync(client, cooperative));
        Assert.Null(await EndDateAsync(client, successor));

        await GraveGenerationHelper.RunAsync(client, 2026, 2);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Invoices.Where(i => i.GraveId == cooperative).ToListAsync());
            Assert.NotEmpty(await db.Invoices.Where(i => i.GraveId == successor).ToListAsync());
        }

        // the cooperative predecessor is not in the overview; the successor is,
        // from the half-year it starts in
        using var before = await StatusAsync(client, 2026, 1);
        Assert.Equal(0, before.RootElement.GetProperty("value").GetArrayLength());
        using var after = await StatusAsync(client, 2026, 2);
        var row = Row(after, "K-001");
        Assert.Equal(successor, row.GetProperty("Id").GetInt64());
    }

    [Fact]
    public async Task A_successor_of_the_same_kind_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );

        await AssertRefusedAsync(
            await SaveSuccessorAsync(client, cooperative, "2026-07-01", true, [], []),
            "SuccessorSameKind"
        );
        Assert.Null(await EndDateAsync(client, cooperative));
    }

    [Fact]
    public async Task A_successor_starting_at_or_before_the_predecessor_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );

        foreach (var start in new[] { "2024-01-01", "2023-06-01" })
        {
            await AssertRefusedAsync(
                await SaveSuccessorAsync(
                    client,
                    cooperative,
                    start,
                    false,
                    [Link(seed.PricedItem)],
                    [seed.Anna]
                ),
                "SuccessorStartTooEarly"
            );
        }

        Assert.Null(await EndDateAsync(client, cooperative));
    }

    [Fact]
    public async Task A_successor_inside_an_invoiced_half_year_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var ordinary = await SavedIdAsync(
            await SaveOrdinaryAsync(client, "K-001", seed.PricedItem, seed.Anna)
        );
        await GraveGenerationHelper.RunAsync(client, 2026, 2);

        await AssertRefusedAsync(
            await SaveSuccessorAsync(client, ordinary, "2026-10-01", true, [], []),
            "SuccessorStartInvoiced"
        );
        Assert.Null(await EndDateAsync(client, ordinary));

        await SavedIdAsync(await SaveSuccessorAsync(client, ordinary, "2027-01-01", true, [], []));
        Assert.StartsWith("2026-12-01", await EndDateAsync(client, ordinary));
    }

    [Fact]
    public async Task Invoices_of_the_predecessor_stay_usable()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var ordinary = await SavedIdAsync(
            await SaveOrdinaryAsync(client, "K-001", seed.PricedItem, seed.Anna)
        );
        await GraveGenerationHelper.RunAsync(client, 2026, 2);
        await SavedIdAsync(await SaveSuccessorAsync(client, ordinary, "2027-01-01", true, [], []));

        long invoiceId;
        long documentId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = (await db.Invoices.SingleAsync(i => i.GraveId == ordinary)).Id;
            documentId = (
                await db.Documents.SingleAsync(d =>
                    d.InvoiceId == invoiceId && d.Kind == DocumentKind.Normal
                )
            ).Id;
        }

        var download = await client.GetAsync($"/odata/Documents({documentId})/$value");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);

        var revision = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId = seed.PricedItem,
                            count = 1,
                            unitPrice = (decimal?)120.00m,
                        },
                    },
                }
            )
        );
        Assert.True(revision.IsSuccessStatusCode, await revision.Content.ReadAsStringAsync());

        // a paid grave invoice is not revised any more, so payment comes after
        long revisedId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            revisedId = (
                await db
                    .Invoices.Where(i => i.GraveId == ordinary)
                    .OrderByDescending(i => i.Id)
                    .FirstAsync()
            ).Id;
        }

        var paid = await client.PostAsync(
            "/odata/SetInvoicePaid",
            ODataJson.Body(new { invoiceId = revisedId, paid = true })
        );
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_half_year_with_invoices_shows_invoiced()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var ordinary = await SavedIdAsync(
            await SaveOrdinaryAsync(client, "K-001", seed.PricedItem, seed.Anna)
        );
        await GraveGenerationHelper.RunAsync(client, 2026, 2);
        await SavedIdAsync(await SaveSuccessorAsync(client, ordinary, "2027-01-01", true, [], []));

        using var status = await StatusAsync(client, 2026, 2);
        var row = Row(status, "K-001");
        Assert.True(row.GetProperty("Invoiced").GetBoolean());
        Assert.Equal(ordinary, row.GetProperty("Id").GetInt64());
        // the cooperative successor is not listed in its own half-years
        using var later = await StatusAsync(client, 2027, 1);
        Assert.Equal(0, later.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task A_failed_successor_leaves_the_predecessor_alone()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );

        await AssertRefusedAsync(
            await SaveSuccessorAsync(
                client,
                cooperative,
                "2026-07-01",
                false,
                [Link(seed.PricedItem)],
                []
            ),
            "NoRecipients"
        );
        Assert.Null(await EndDateAsync(client, cooperative));
    }

    [Fact]
    public async Task A_successor_needs_an_existing_predecessor_with_the_same_number()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var seed = await SeedAsync(client);
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );
        object[] items = [Link(seed.PricedItem)];

        await AssertRefusedAsync(
            await SaveSuccessorAsync(client, 9999, "2026-07-01", false, items, [seed.Anna]),
            "GraveNotFound"
        );
        await AssertRefusedAsync(
            await SaveSuccessorAsync(
                client,
                cooperative,
                "2026-07-01",
                false,
                items,
                [seed.Anna],
                " k-002 "
            ),
            "SuccessorNumberDiffers"
        );
        // trimmed and case-insensitive like the overlap check
        await SavedIdAsync(
            await SaveSuccessorAsync(
                client,
                cooperative,
                "2026-07-01",
                false,
                items,
                [seed.Anna],
                " k-001 "
            )
        );
    }

    [Fact]
    public async Task Taking_over_is_only_for_a_new_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var cooperative = await SavedIdAsync(
            await SaveTermAsync(client, "K-001", "2024-01-01", null, true, [], [])
        );

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = cooperative,
                    succeedsGraveId = cooperative,
                    graveNumber = "K-001",
                    fieldNumber = "F-1",
                    siteName = "X",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2024-01-01",
                    endDate = (string?)null,
                    isCooperative = true,
                    items = Array.Empty<object>(),
                    recipientIds = Array.Empty<long>(),
                    corrections = Array.Empty<object>(),
                }
            )
        );
        await AssertRefusedAsync(response, "SuccessorOnlyOnCreate");
    }

    private static async Task<long> SeedItemAt19Async(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));
        return await ReadIdAsync(
            client.PostAsync(
                "/odata/InvoiceItems",
                ODataJson.Body(
                    new
                    {
                        Number = "5001",
                        Description = "Pflege",
                        VatRate = 19.0m,
                        DefaultPrice = (decimal?)null,
                    }
                )
            )
        );
    }

    // 3 recipients, care 60,00 all year: the part-year start and end blocks of
    // a term bill 40,00 and need a correction to 40,02
    private static async Task<long> SaveCorrectedAsync(
        HttpClient client,
        long item,
        long[] recipients,
        string? end,
        object[] corrections
    ) =>
        await SavedIdAsync(
            await client.PostAsync(
                "/odata/SaveGrave",
                ODataJson.Body(
                    new
                    {
                        graveId = (long?)null,
                        graveNumber = "K-1",
                        fieldNumber = "1",
                        siteName = "Korrigiert",
                        cemeteryId = TestCemetery.Id,
                        position = "SingleInside",
                        startDate = "2026-03",
                        endDate = end,
                        isCooperative = false,
                        items = new[]
                        {
                            new
                            {
                                itemId = item,
                                count = 1,
                                unitPrice = (decimal?)60.00m,
                                priceIncludesVat = false,
                            },
                        },
                        recipientIds = recipients,
                        corrections,
                    }
                )
            )
        );

    private static object Correction(string kind, int year, int half, long item) =>
        new
        {
            kind,
            year,
            half,
            vatRate = 19.0m,
            itemId = item,
            correctedPrice = 40.02m,
        };

    private static async Task<long[]> ThreeRecipientsAsync(HttpClient client)
    {
        var ids = new List<long>();
        foreach (var name in new[] { "Anna", "Bert", "Carl" })
        {
            ids.Add(await RecipientAsync(client, name));
        }

        return [.. ids];
    }

    [Fact]
    public async Task A_successor_that_changes_the_predecessors_corrections_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await ThreeRecipientsAsync(client);
        // term 03/2026-10/2028: start H1 2026 and end H2 2028 both need a correction
        var id = await SaveCorrectedAsync(
            client,
            item,
            recipients,
            "2028-10",
            [Correction("Start", 2026, 1, item), Correction("End", 2028, 2, item)]
        );

        // ending in 12/2026 leaves no end block, so its correction would go
        await AssertRefusedAsync(
            await SaveSuccessorAsync(client, id, "2027-01-01", true, [], [], "K-1"),
            "SuccessorChangesCorrections"
        );
        Assert.StartsWith("2028-10-01", await EndDateAsync(client, id));
    }

    [Fact]
    public async Task A_start_block_beyond_the_generation_window_does_not_stop_a_hand_over()
    {
        // H2 2027: a start block in H1 2026 can no longer be invoiced, so it is
        // not evaluated -- but the row stored while it could be is still there
        using var host = new ApiTestHost { Now = new DateTime(2027, 10, 6, 12, 0, 0) };
        using var client = host.CreateClient();
        var item = await SeedItemAt19Async(client);
        var recipients = await ThreeRecipientsAsync(client);
        var id = await SaveCorrectedAsync(client, item, recipients, null, []);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GraveBillingCorrections.Add(
                new Burkard.Invoicing.Domain.Entities.GraveBillingCorrection
                {
                    GraveId = id,
                    Kind = Burkard.Invoicing.Domain.BillingExampleKind.Start,
                    Year = 2026,
                    Half = 1,
                    VatRate = 19.0m,
                    ItemId = item,
                    EnteredPrice = 40.00m,
                    CorrectedPrice = 40.02m,
                }
            );
            await db.SaveChangesAsync();
        }

        await SavedIdAsync(await SaveSuccessorAsync(client, id, "2028-01-01", true, [], [], "K-1"));
        Assert.StartsWith("2027-12-01", await EndDateAsync(client, id));
    }

    private static Task<HttpResponseMessage> SaveOrdinaryAsync(
        HttpClient client,
        string number,
        long itemId,
        long recipientId
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = number,
                    fieldNumber = "F-1",
                    siteName = "Ordentlich",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01-01",
                    endDate = (string?)null,
                    isCooperative = false,
                    items = new[] { Link(itemId) },
                    recipientIds = new[] { recipientId },
                    corrections = Array.Empty<object>(),
                }
            )
        );
}
