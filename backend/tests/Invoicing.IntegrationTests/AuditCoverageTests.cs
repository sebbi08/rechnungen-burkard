using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Requests and the generation job run with the audit guard armed, so
/// a write path that forgot to declare its action answers 500 here. Every
/// path the application has runs once and must leave an entry behind.</summary>
public class AuditCoverageTests
{
    private sealed record Seed(long ItemId, long AnnaId, long BerndId, long CarlaId);

    private static async Task<Seed> SeedAsync(ApiTestHost host)
    {
        // a scope of its own is not armed: seeding is no write path of the app
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
        var carla = new Recipient
        {
            Number = "E-0003",
            Name = "Carla Beispiel",
            Address = "C 3",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.AddRange(anna, bernd, carla);
        await db.SaveChangesAsync();
        return new Seed(item.Id, anna.Id, bernd.Id, carla.Id);
    }

    private static async Task<int> EntryCountAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditEntries.CountAsync();
    }

    private static object[] Lines(long itemId, int count) =>
        [
            new
            {
                itemId,
                count,
                unitPrice = (decimal?)null,
            },
        ];

    private static Task<HttpResponseMessage> SaveGraveAsync(
        HttpClient client,
        Seed seed,
        long? graveId,
        int count
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber = "G-700",
                    fieldNumber = "F-1",
                    siteName = "Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2020-01",
                    endDate = "2040-12",
                    isCooperative = false,
                    items = Lines(seed.ItemId, count),
                    recipientIds = new[] { seed.AnnaId, seed.BerndId },
                    corrections = Array.Empty<object>(),
                }
            )
        );

    private static MultipartFormDataContent Archive()
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

            Add("recipients.csv", "RecipientNumber,Name,Address\r\nE-1000,Dora Beispiel,D 4\r\n");
            Add(
                "items.csv",
                "ItemNumber,Description,VatRate,Price,Quarter\r\n9001,Blumen,7,20.00,\r\n"
            );
            Add(
                "graves.csv",
                "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate\r\n"
                    + "IMP-1,27,Eins,Bergfriedhof,DoubleOutside,2026-01-01,2040-12-31\r\n"
            );
            Add(
                "mapping.csv",
                "Type,GraveNumber,Reference,Count,UnitPrice\r\n"
                    + "Item,IMP-1,9001,1,\r\nRecipient,IMP-1,E-1000,,\r\n"
            );
        }

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(memory.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "import.zip");
        return content;
    }

    private static async Task<long> IdOfAsync(HttpResponseMessage response)
    {
        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    [Fact]
    public async Task Every_write_path_is_declared()
    {
        using var host = new ApiTestHost();
        var seed = await SeedAsync(host);
        using var client = host.CreateClient();
        var entries = await EntryCountAsync(host);

        // runs one write path: it must succeed and add at least one entry
        async Task<HttpResponseMessage> Step(string name, Func<Task<HttpResponseMessage>> call)
        {
            var response = await call();
            Assert.True(
                response.IsSuccessStatusCode,
                $"{name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"
            );
            var now = await EntryCountAsync(host);
            Assert.True(now > entries, $"{name} left no audit entry");
            entries = now;
            return response;
        }

        var graveId = await IdOfAsync(
            await Step("create grave", () => SaveGraveAsync(client, seed, null, 1))
        );
        await Step("edit grave", () => SaveGraveAsync(client, seed, graveId, 2));
        await Step("delete grave", () =>
            client.PostAsync("/odata/DeleteGrave", ODataJson.Body(new { graveId }))
        );
        await Step("restore grave", () =>
            client.PostAsync("/odata/RestoreGrave", ODataJson.Body(new { graveId }))
        );
        await Step("default price", () =>
            client.SendAsync(
                new HttpRequestMessage(HttpMethod.Patch, $"/odata/InvoiceItems({seed.ItemId})")
                {
                    Content = ODataJson.Body(new { DefaultPrice = 110m }),
                }
            )
        );
        await Step("price increase", () =>
            client.PostAsync(
                "/odata/ApplyItemPriceIncrease",
                ODataJson.Body(
                    new
                    {
                        ItemId = seed.ItemId,
                        Rows = new[]
                        {
                            new
                            {
                                IsDefault = false,
                                OldPrice = 100m,
                                NewPrice = 105m,
                                GraveIds = new[] { graveId },
                            },
                        },
                        Corrections = Array.Empty<object>(),
                    }
                )
            )
        );

        var invoiceId = await IdOfAsync(
            await Step("create invoice", () =>
                client.PostAsync(
                    "/odata/CreateInvoice",
                    ODataJson.Body(
                        new { recipientId = seed.CarlaId, lines = Lines(seed.ItemId, 1) }
                    )
                )
            )
        );
        var revisionId = await IdOfAsync(
            await Step("revise invoice", () =>
                client.PostAsync(
                    "/odata/CreateRevision",
                    ODataJson.Body(
                        new
                        {
                            invoiceId,
                            recipientId = seed.CarlaId,
                            lines = Lines(seed.ItemId, 2),
                        }
                    )
                )
            )
        );
        await Step("set paid", () =>
            client.PostAsync(
                "/odata/SetInvoicePaid",
                ODataJson.Body(new { invoiceId = revisionId, paid = true })
            )
        );

        var quoteId = await IdOfAsync(
            await client.PostAsync(
                "/odata/CreateQuote",
                ODataJson.Body(new { recipientId = seed.AnnaId, lines = Lines(seed.ItemId, 1) })
            )
        );
        await Step("convert quote", () =>
            client.PostAsync("/odata/ConvertQuote", ODataJson.Body(new { quoteId }))
        );

        // the job runs on its own scope; wait for it, then look at what it wrote
        await GraveGenerationHelper.RunAsync(client, 2031, 1);
        var afterJob = await EntryCountAsync(host);
        Assert.True(afterJob > entries, "generation left no audit entry");
        entries = afterJob;

        List<Invoice> generated;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            generated = await db
                .Invoices.AsNoTracking()
                .Where(i => i.RecipientCount == 2 && i.IsLatestRevision)
                .OrderBy(i => i.Id)
                .ToListAsync();
        }

        Assert.Equal(2, generated.Count);
        var anna = generated.Single(i => i.RecipientId == seed.AnnaId);
        var bernd = generated.Single(i => i.RecipientId == seed.BerndId);
        await Step("grave invoice revision with swap", () =>
            client.PostAsync(
                "/odata/CreateGraveInvoiceRevision",
                ODataJson.Body(
                    new
                    {
                        invoiceId = anna.Id,
                        lines = Lines(seed.ItemId, 2),
                        replacements = new[]
                        {
                            new { invoiceId = bernd.Id, recipientId = seed.CarlaId },
                        },
                    }
                )
            )
        );

        List<long> latest;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            latest = await db
                .Invoices.Where(i => i.RecipientCount == 2 && i.IsLatestRevision)
                .Select(i => i.Id)
                .ToListAsync();
        }

        await Step("mark paid", () =>
            client.PostAsync("/odata/MarkInvoicesPaid", ODataJson.Body(new { invoiceIds = latest }))
        );

        using var archive = Archive();
        await Step("import", () => client.PostAsync("/odata/ImportArchive", archive));
    }
}
