using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Every write path of a grave leaves an audit entry with what changed,
/// and a refused save leaves none.</summary>
public class GraveAuditTests
{
    private sealed record Seed(long ItemId, long AnnaId, long BerndId);

    private static async Task<Seed> SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "1001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 100m,
        };
        var anna = new Recipient
        {
            Number = "E-0001",
            Name = "Anna Beispiel",
            Address = "A 1",
        };
        var bernd = new Recipient
        {
            Number = "E-0002",
            Name = "Bernd Beispiel",
            Address = "B 2",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(anna, bernd);
        await db.SaveChangesAsync();
        return new Seed(item.Id, anna.Id, bernd.Id);
    }

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client,
        Seed seed,
        long? graveId = null,
        string start = "2020-01",
        string end = "2040-12",
        int count = 1,
        long[]? recipientIds = null,
        bool cooperative = false,
        long? succeeds = null
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-500",
                    fieldNumber = "F-1",
                    siteName = "Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = start,
                    endDate = end,
                    isCooperative = cooperative,
                    contractNumber = cooperative ? "V-1" : null,
                    succeedsGraveId = succeeds,
                    items = new[]
                    {
                        new
                        {
                            itemId = seed.ItemId,
                            count,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = recipientIds ?? [seed.AnnaId],
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

    private static async Task<List<AuditEntry>> EntriesAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    private static List<AuditChange> Changes(AuditEntry entry) =>
        JsonSerializer.Deserialize<List<AuditChange>>(
            entry.ChangesJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;

    private static AuditChange Change(AuditEntry entry, string field) =>
        Assert.Single(Changes(entry), c => c.Field == field);

    [Fact]
    public async Task Creating_a_grave_records_its_values()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();

        var graveId = await SavedIdAsync(await SaveAsync(client, seed));

        var entry = Assert.Single(await EntriesAsync(host));
        Assert.Equal(AuditActions.GraveCreated, entry.Action);
        Assert.Equal(graveId, entry.GraveId);
        Assert.Equal("G-500", Change(entry, "graveNumber").New);
        Assert.Equal("1001 Grabpflege", Change(entry, "itemAdded").Label);
        Assert.Equal("E-0001 Anna Beispiel", Change(entry, "recipientAdded").New);
    }

    [Fact]
    public async Task Editing_a_grave_records_old_and_new()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var graveId = await SavedIdAsync(await SaveAsync(client, seed));

        await SavedIdAsync(await SaveAsync(client, seed, graveId, end: "2041-12", count: 2));

        var entries = await EntriesAsync(host);
        Assert.Equal(2, entries.Count);
        var edit = entries[1];
        Assert.Equal(AuditActions.GraveEdited, edit.Action);
        var end = Change(edit, "end");
        Assert.Equal(("2040-12", "2041-12"), (end.Old, end.New));
        var count = Change(edit, "itemCount");
        Assert.Equal(("1", "2"), (count.Old, count.New));
        Assert.Equal(2, Changes(edit).Count);
    }

    [Fact]
    public async Task Saving_unchanged_writes_no_entry()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var graveId = await SavedIdAsync(await SaveAsync(client, seed));

        await SavedIdAsync(await SaveAsync(client, seed, graveId));

        var entry = Assert.Single(await EntriesAsync(host));
        Assert.Equal(AuditActions.GraveCreated, entry.Action);
    }

    [Fact]
    public async Task Changing_recipients_records_added_and_removed()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var graveId = await SavedIdAsync(await SaveAsync(client, seed));

        await SavedIdAsync(await SaveAsync(client, seed, graveId, recipientIds: [seed.BerndId]));

        var edit = (await EntriesAsync(host))[1];
        Assert.Equal("E-0002 Bernd Beispiel", Change(edit, "recipientAdded").New);
        Assert.Equal("E-0001 Anna Beispiel", Change(edit, "recipientRemoved").Old);
        Assert.Equal(2, Changes(edit).Count);
    }

    [Fact]
    public async Task A_hand_over_records_on_both_graves()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var predecessorId = await SavedIdAsync(await SaveAsync(client, seed));

        var successorId = await SavedIdAsync(
            await SaveAsync(
                client,
                seed,
                start: "2030-03",
                cooperative: true,
                succeeds: predecessorId
            )
        );

        var entries = await EntriesAsync(host);
        var created = Assert.Single(entries, e =>
            e.GraveId == successorId && e.Action == AuditActions.GraveCreated
        );
        Assert.Equal("""{"from":"2030-03"}""", created.DetailsJson);
        var handedOver = Assert.Single(entries, e =>
            e.GraveId == predecessorId && e.Action == AuditActions.GraveHandedOver
        );
        Assert.Equal("""{"from":"2030-03"}""", handedOver.DetailsJson);
        var end = Change(handedOver, "end");
        Assert.Equal(("2040-12", "2030-02"), (end.Old, end.New));
    }

    [Fact]
    public async Task A_refused_save_writes_nothing()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var graveId = await SavedIdAsync(await SaveAsync(client, seed));
        var before = (await EntriesAsync(host)).Count;

        var response = await SaveAsync(
            client,
            seed,
            graveId,
            end: "2041-12",
            recipientIds: Enumerable.Range(1, 40).Select(i => (long)i).ToArray()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TooManyRecipients", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, (await EntriesAsync(host)).Count);
    }

    [Fact]
    public async Task Deleting_and_restoring_are_recorded()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var graveId = await SavedIdAsync(await SaveAsync(client, seed));

        var deleted = await client.PostAsync("/odata/DeleteGrave", ODataJson.Body(new { graveId }));
        Assert.True(deleted.IsSuccessStatusCode);
        var restored = await client.PostAsync(
            "/odata/RestoreGrave",
            ODataJson.Body(new { graveId })
        );
        Assert.True(restored.IsSuccessStatusCode);

        var entries = await EntriesAsync(host);
        Assert.Equal(
            [AuditActions.GraveCreated, AuditActions.GraveDeleted, AuditActions.GraveRestored],
            entries.Select(e => e.Action)
        );
        Assert.All(entries.Skip(1), e => Assert.Equal("[]", e.ChangesJson));
    }

    private static MultipartFormDataContent TwoGraveArchive()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(name).Open(),
                    new UTF8Encoding(false)
                );
                writer.Write(text);
            }

            Add(
                "recipients.csv",
                "RecipientNumber,Name,Address\r\nE-1000,Anna Beispiel,\"A 1\n99999 Ort\"\r\n"
            );
            Add(
                "items.csv",
                "ItemNumber,Description,VatRate,Price,Quarter\r\n9001,Grabpflege,7,120.00,\r\n"
            );
            Add(
                "graves.csv",
                "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate\r\n"
                    + "IMP-1,27,Eins,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
                    + "IMP-2,28,Zwei,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            );
            Add(
                "mapping.csv",
                "Type,GraveNumber,Reference,Count,UnitPrice\r\n"
                    + "Item,IMP-1,9001,1,\r\nItem,IMP-2,9001,1,\r\n"
                    + "Recipient,IMP-1,E-1000,,\r\nRecipient,IMP-2,E-1000,,\r\n"
            );
        }

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(memory.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "import.zip");
        return content;
    }

    [Fact]
    public async Task An_import_records_each_grave()
    {
        using var host = new ApiTestHost();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            await db.SaveChangesAsync();
        }

        using var client = host.CreateClient();
        using var content = TwoGraveArchive();

        var response = await client.PostAsync("/odata/ImportArchive", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await EntriesAsync(host);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(AuditActions.GraveImported, e.Action));
        Assert.Equal(2, entries.Select(e => e.GraveId).Distinct().Count());
    }
}
