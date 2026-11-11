using System.IO.Compression;
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

/// <summary>A grave is deleted softly and can be restored until an invoice run has
/// finished since: the run would have skipped it, so it is not the same state any
/// more.</summary>
public class DeleteGraveTests
{
    private static async Task<long> RecipientAsync(HttpClient client)
    {
        using var json = await ODataJson.ReadAsync(
            await client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(
                    new { Name = "Anna Beispiel", Address = "Hauptstraße 1\n99999 Musterdorf" }
                )
            )
        );
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    // a cooperative grave needs neither items nor recipients, so most tests need no seed
    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client,
        string number = "K-1",
        string start = "2026-01",
        long? succeeds = null,
        long? graveId = null,
        bool cooperative = true,
        long[]? recipientIds = null
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = number,
                    fieldNumber = "F-1",
                    siteName = "Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = start,
                    endDate = (string?)null,
                    isCooperative = cooperative,
                    contractNumber = cooperative ? "V-1" : null,
                    succeedsGraveId = succeeds,
                    items = Array.Empty<object>(),
                    recipientIds = recipientIds ?? [],
                    corrections = Array.Empty<object>(),
                }
            )
        );

    private static async Task<long> SavedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, long id) =>
        client.PostAsync("/odata/DeleteGrave", ODataJson.Body(new { graveId = id }));

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, long id) =>
        client.PostAsync("/odata/RestoreGrave", ODataJson.Body(new { graveId = id }));

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<JsonDocument> GetGraveAsync(HttpClient client, long id) =>
        await ODataJson.ReadAsync(await client.GetAsync($"/odata/Graves({id})"));

    [Fact]
    public async Task Deleting_marks_the_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));

        var response = await DeleteAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(id, json.RootElement.GetProperty("Id").GetInt64());
        using var grave = await GetGraveAsync(client, id);
        Assert.NotEqual(JsonValueKind.Null, grave.RootElement.GetProperty("DeletedAt").ValueKind);
        Assert.True(grave.RootElement.GetProperty("CanRestore").GetBoolean());
        await AssertRefusedAsync(await DeleteAsync(client, id), "GraveDeleted");
    }

    [Fact]
    public async Task An_unknown_grave_is_not_found()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        await AssertRefusedAsync(await DeleteAsync(client, 999), "GraveNotFound");
        await AssertRefusedAsync(await RestoreAsync(client, 999), "GraveNotFound");
    }

    [Fact]
    public async Task A_deleted_grave_cannot_be_saved()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));
        await DeleteAsync(client, id);

        await AssertRefusedAsync(await SaveAsync(client, graveId: id), "GraveDeleted");
        // the hand-over runs between the two kinds, so the successor is ordinary
        var anna = await RecipientAsync(client);
        await AssertRefusedAsync(
            await SaveAsync(
                client,
                start: "2026-07",
                succeeds: id,
                cooperative: false,
                recipientIds: [anna]
            ),
            "GraveDeleted"
        );
    }

    [Fact]
    public async Task Restoring_brings_the_grave_back()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));
        await DeleteAsync(client, id);

        var response = await RestoreAsync(client, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(id, json.RootElement.GetProperty("Id").GetInt64());
        using var grave = await GetGraveAsync(client, id);
        Assert.Equal(JsonValueKind.Null, grave.RootElement.GetProperty("DeletedAt").ValueKind);
        Assert.False(grave.RootElement.GetProperty("CanRestore").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(client, id)).StatusCode);
    }

    [Fact]
    public async Task Restoring_an_undeleted_grave_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));

        await AssertRefusedAsync(await RestoreAsync(client, id), "GraveNotDeleted");
    }

    [Fact]
    public async Task A_succeeded_run_for_any_half_year_closes_the_restore_window()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));
        await DeleteAsync(client, id);
        // an ordinary grave with a price, so the run has something to invoice
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        using var item = await ODataJson.ReadAsync(
            await client.PostAsync(
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
        var itemId = item.RootElement.GetProperty("Id").GetInt64();
        var anna = await RecipientAsync(client);
        await SavedIdAsync(
            await client.PostAsync(
                "/odata/SaveGrave",
                ODataJson.Body(
                    new
                    {
                        graveNumber = "O-1",
                        fieldNumber = "F-2",
                        siteName = "Test",
                        cemeteryId = TestCemetery.Id,
                        position = "SingleInside",
                        startDate = "2026-01",
                        endDate = (string?)null,
                        isCooperative = false,
                        items = new[]
                        {
                            new
                            {
                                itemId,
                                count = 1,
                                unitPrice = (decimal?)null,
                            },
                        },
                        recipientIds = new[] { anna },
                        corrections = Array.Empty<object>(),
                    }
                )
            )
        );

        await GraveGenerationHelper.RunAsync(client, 2027, 1);

        await AssertRefusedAsync(await RestoreAsync(client, id), "RestoreWindowClosed");
        using var grave = await GetGraveAsync(client, id);
        Assert.False(grave.RootElement.GetProperty("CanRestore").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, grave.RootElement.GetProperty("DeletedAt").ValueKind);
    }

    [Fact]
    public async Task A_failed_run_leaves_the_window_open()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SavedIdAsync(await SaveAsync(client));
        await DeleteAsync(client, id);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(
                new Job
                {
                    Type = "GraveInvoices",
                    Status = JobStatus.Failed,
                    CreatedAt = DateTime.UtcNow,
                    FinishedAt = DateTime.UtcNow.AddMinutes(1),
                }
            );
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await RestoreAsync(client, id)).StatusCode);
    }

    [Fact]
    public async Task A_deleted_number_is_free_and_restoring_into_an_overlap_is_refused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var old = await SavedIdAsync(await SaveAsync(client));
        await DeleteAsync(client, old);

        await SavedIdAsync(await SaveAsync(client));

        await AssertRefusedAsync(await RestoreAsync(client, old), "GraveNumberInUse");
        using var grave = await GetGraveAsync(client, old);
        Assert.NotEqual(JsonValueKind.Null, grave.RootElement.GetProperty("DeletedAt").ValueKind);
    }

    /// <summary>An ordinary grave that bills one item for 2026, written straight
    /// to the database so the test controls what is stored with it: no
    /// corrections, so a price that does not divide is outdated until saved.</summary>
    private static async Task<long> SeedBillableAsync(
        ApiTestHost host,
        string number,
        int recipients,
        decimal price
    )
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.VatRates.AnyAsync(v => v.Rate == 7m))
        {
            db.VatRates.Add(new VatRate { Rate = 7m });
        }

        var item = new InvoiceItem
        {
            Number = $"I-{number}",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = price,
        };
        var grave = new Grave
        {
            GraveNumber = number,
            FieldNumber = "F-1",
            SiteName = "Test",
            CemeteryId = TestCemetery.Id,
            Position = GravePosition.SingleInside,
            StartDate = new DateOnly(2026, 1, 1),
            Items = [new GraveItem { Item = item, Count = 1 }],
        };
        for (var n = 1; n <= recipients; n++)
        {
            grave.Recipients.Add(
                new GraveRecipient
                {
                    Recipient = new Recipient
                    {
                        Number = $"E-{number}-{n}",
                        Name = $"Empfänger {n}",
                        Address = "Weg 1",
                    },
                }
            );
        }

        db.Graves.Add(grave);
        await db.SaveChangesAsync();
        return grave.Id;
    }

    private static async Task<List<long>> InvoiceIdsOfAsync(ApiTestHost host, long graveId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.Where(i => i.GraveId == graveId).Select(i => i.Id).ToListAsync();
    }

    [Fact]
    public async Task A_run_never_invoices_a_deleted_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        // 100,00 EUR over 3 recipients does not divide, and nothing is stored
        var id = await SeedBillableAsync(host, "D-1", recipients: 3, price: 100m);
        var run = () =>
            client.PostAsync(
                "/odata/GenerateGraveInvoices",
                ODataJson.Body(new { year = 2026, half = 1 })
            );
        await AssertRefusedAsync(await run(), "CorrectionsOutdated");

        await DeleteAsync(client, id);

        // the precheck ignores the deleted grave, and with only it due the run has
        // nothing to do, which is refused like a half-year already invoiced
        var response = await run();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "GravesAlreadyInvoiced",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Empty(await db.GraveInvoiceRuns.ToListAsync());
    }

    [Fact]
    public async Task A_run_invoices_the_other_graves_and_skips_the_deleted_one()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var deleted = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        var kept = await SeedBillableAsync(host, "D-2", recipients: 1, price: 100m);
        await DeleteAsync(client, deleted);

        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        Assert.Empty(await InvoiceIdsOfAsync(host, deleted));
        Assert.Single(await InvoiceIdsOfAsync(host, kept));
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(kept, (await db.GraveInvoiceRuns.SingleAsync()).GraveId);
    }

    [Fact]
    public async Task The_generator_refuses_a_deleted_grave_directly()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        await DeleteAsync(client, id);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync(g => g.Id == id);
        var generator = scope.ServiceProvider.GetRequiredService<GraveInvoiceGenerator>();

        var result = await generator.GenerateForGraveAsync(grave, 2026, 1, CancellationToken.None);

        Assert.Null(result.Invoices);
        Assert.Equal("Deleted", result.SkipReason);
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    [Fact]
    public async Task The_overview_leaves_deleted_graves_out()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var invoiced = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        var open = await SeedBillableAsync(host, "D-2", recipients: 1, price: 100m);
        var kept = await SeedBillableAsync(host, "D-3", recipients: 1, price: 100m);
        await DeleteAsync(client, invoiced);
        await DeleteAsync(client, open);

        using var json = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1")
        );

        var numbers = json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(r => r.GetProperty("GraveNumber").GetString())
            .ToList();
        Assert.Equal(["D-3"], numbers);
        Assert.NotEqual(0, kept);
    }

    [Fact]
    public async Task Item_graves_leave_deleted_graves_out()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var deleted = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        var kept = await SeedBillableAsync(host, "D-2", recipients: 1, price: 100m);
        long deletedItem,
            keptItem;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            deletedItem = (await db.GraveItems.SingleAsync(l => l.GraveId == deleted)).ItemId;
            keptItem = (await db.GraveItems.SingleAsync(l => l.GraveId == kept)).ItemId;
        }

        await DeleteAsync(client, deleted);

        using var withDeleted = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/ItemGraves?itemId={deletedItem}")
        );
        Assert.False(withDeleted.RootElement.GetProperty("Linked").GetBoolean());
        Assert.Empty(withDeleted.RootElement.GetProperty("Groups").EnumerateArray());
        using var other = await ODataJson.ReadAsync(
            await client.GetAsync($"/odata/ItemGraves?itemId={keptItem}")
        );
        Assert.True(other.RootElement.GetProperty("Linked").GetBoolean());
    }

    [Fact]
    public async Task The_bulk_download_leaves_out_invoices_of_deleted_graves()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var deleted = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        var kept = await SeedBillableAsync(host, "D-2", recipients: 1, price: 100m);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        await DeleteAsync(client, deleted);

        var response = await client.PostAsync(
            "/odata/BulkDownloadDocuments",
            ODataJson.Body(
                new
                {
                    kind = "Normal",
                    year = 2026,
                    half = 1,
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<string> expected;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoiceId = (await InvoiceIdsOfAsync(host, kept)).Single();
            expected = await db
                .Documents.Where(d => d.InvoiceId == invoiceId && d.Kind == DocumentKind.Normal)
                .Select(d => d.FileName)
                .ToListAsync();
        }

        Assert.Single(expected);
        using var zip = new ZipArchive(
            await response.Content.ReadAsStreamAsync(),
            ZipArchiveMode.Read
        );
        Assert.Equal(expected, zip.Entries.Select(e => e.Name).ToList());
    }

    [Fact]
    public async Task Invoices_of_a_deleted_grave_cannot_be_marked_paid_or_revised()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var id = await SeedBillableAsync(host, "D-1", recipients: 1, price: 100m);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);
        var invoiceId = (await InvoiceIdsOfAsync(host, id)).Single();
        long itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            itemId = (await db.GraveItems.SingleAsync(l => l.GraveId == id)).ItemId;
        }

        await DeleteAsync(client, id);

        await AssertRefusedAsync(
            await client.PostAsync(
                "/odata/SetInvoicePaid",
                ODataJson.Body(new { invoiceId, paid = true })
            ),
            "GraveDeleted"
        );
        await AssertRefusedAsync(
            await client.PostAsync(
                "/odata/CreateGraveInvoiceRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId,
                        lines = new[]
                        {
                            new
                            {
                                itemId,
                                count = 2,
                                unitPrice = (decimal?)null,
                            },
                        },
                    }
                )
            ),
            "GraveDeleted"
        );
        // the dialog's read still answers, so the invoice stays viewable
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/odata/GraveInvoiceGroup?invoiceId={invoiceId}")).StatusCode
        );
    }
}
