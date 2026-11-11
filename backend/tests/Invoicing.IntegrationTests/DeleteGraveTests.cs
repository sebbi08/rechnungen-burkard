using System.Net;
using System.Net.Http;
using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
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
}
