using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A backup is a ZIP of a manifest and a consistent copy of the whole
/// database.</summary>
public class BackupTests
{
    private static ZipArchive OpenZip(byte[] zip) =>
        new(new MemoryStream(zip), ZipArchiveMode.Read);

    private static string ExtractDb(byte[] zip)
    {
        var path = Path.Combine(Path.GetTempPath(), $"backup-test-{Guid.NewGuid():N}.db");
        using var archive = OpenZip(zip);
        using var entry = archive.GetEntry("invoicing.db")!.Open();
        using var file = File.Create(path);
        entry.CopyTo(file);
        return path;
    }

    [Fact]
    public async Task Download_IsZipWithManifestAndDatabase()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/DownloadBackup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(
            new Regex(@"^rechnungen-sicherung-2026-10-06-\d{4}\.zip$"),
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
        );

        using var archive = OpenZip(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            new[] { "invoicing.db", "manifest.json" },
            archive.Entries.Select(e => e.FullName).Order().ToArray()
        );
        using var reader = archive.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(reader);
        var root = manifest.RootElement;
        Assert.Equal("rechnungen-burkard-backup", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(
            "20261111213228_GraveContractNumberAndDeletedAt",
            root.GetProperty("lastMigration").GetString()
        );
    }

    [Fact]
    public async Task Download_ContainsWriteJustMade()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var created = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new { Name = "Backup Probe", Address = "Hauptstraße 1\n99999 Musterdorf" }
            )
        );
        created.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/odata/DownloadBackup");
        var dbPath = ExtractDb(await response.Content.ReadAsByteArrayAsync());
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Recipients WHERE Name = 'Backup Probe'";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Download_WhileJobActive_Is409()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(
                new Job
                {
                    Type = "GraveInvoices",
                    Status = JobStatus.Running,
                    TotalCount = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/odata/DownloadBackup");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "JobAlreadyRunning",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    private static readonly object ValidManifest = new
    {
        format = "rechnungen-burkard-backup",
        formatVersion = 1,
        appVersion = "1.0.0",
        lastMigration = "x",
        createdAtUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
    };

    private static byte[] BuildZip(string dbPath, object manifest)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest));
            }
            using var entry = archive.CreateEntry("invoicing.db").Open();
            using var file = File.OpenRead(dbPath);
            file.CopyTo(entry);
        }
        return stream.ToArray();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] zip)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(zip), "file", "sicherung.zip" },
        };
        return client.PostAsync("/odata/UploadBackup", form);
    }

    private static async Task<string> StageAsync(HttpClient client, byte[] zip)
    {
        var response = await UploadAsync(client, zip);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client) =>
        await (await client.GetAsync("/odata/DownloadBackup")).Content.ReadAsByteArrayAsync();

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static string[] StagingFiles(ApiTestHost host)
    {
        var dir = Path.Combine(Path.GetDirectoryName(host.DbFilePath)!, "restore-staging");
        return Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
    }

    private static async Task SaveCooperativeGraveAsync(HttpClient client)
    {
        using var recipient = await ODataJson.ReadAsync(
            await client.PostAsync(
                "/odata/Recipients",
                ODataJson.Body(
                    new { Name = "Anna Beispiel", Address = "Hauptstraße 1\n99999 Musterdorf" }
                )
            )
        );
        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId = (long?)null,
                    graveNumber = "K-1",
                    fieldNumber = "F-1",
                    siteName = "Test",
                    cemeteryId = TestCemetery.Id,
                    position = "SingleInside",
                    startDate = "2026-01",
                    endDate = (string?)null,
                    isCooperative = true,
                    contractNumber = "V-1",
                    succeedsGraveId = (long?)null,
                    items = Array.Empty<object>(),
                    recipientIds = new[] { recipient.RootElement.GetProperty("Id").GetInt64() },
                    corrections = Array.Empty<object>(),
                }
            )
        );
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Upload_OwnBackup_ReturnsSummary()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        await SaveCooperativeGraveAsync(client);

        var response = await UploadAsync(client, await DownloadAsync(client));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Matches(
            new Regex("^[0-9a-f]{32}$"),
            json.RootElement.GetProperty("token").GetString()
        );
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("graves").GetInt32());
        Assert.True(summary.GetProperty("recipients").GetInt32() >= 1);
        Assert.NotEmpty(summary.GetProperty("appVersion").GetString()!);
        Assert.False(summary.GetProperty("newerInvoicesHere").GetBoolean());
    }

    [Fact]
    public async Task Upload_ImportArchive_IsBackupInvalid()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        var template = await client.GetByteArrayAsync("/odata/ImportTemplate");

        await AssertRejectedAsync(await UploadAsync(client, template), "BackupInvalid");
        Assert.Empty(StagingFiles(host).Except(before));
    }

    [Fact]
    public async Task Upload_NotAZip_IsBackupInvalid()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);

        await AssertRejectedAsync(await UploadAsync(client, "hello"u8.ToArray()), "BackupInvalid");
        Assert.Empty(StagingFiles(host).Except(before));
    }

    [Fact]
    public async Task Upload_WithoutFile_IsArchiveRequired()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);

        var response = await client.PostAsync(
            "/odata/UploadBackup",
            new MultipartFormDataContent { { new StringContent("x"), "other" } }
        );

        await AssertRejectedAsync(response, "ArchiveRequired");
    }

    [Fact]
    public async Task Upload_GarbageDatabase_IsBackupDamaged()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        var path = Path.Combine(Path.GetTempPath(), $"backup-test-{Guid.NewGuid():N}.db");
        var garbage = new byte[4096];
        Random.Shared.NextBytes(garbage);
        File.WriteAllBytes(path, garbage);
        try
        {
            await AssertRejectedAsync(
                await UploadAsync(client, BuildZip(path, ValidManifest)),
                "BackupDamaged"
            );
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Empty(StagingFiles(host).Except(before));
    }

    [Fact]
    public async Task Upload_UnknownMigration_IsBackupFromNewerVersion()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        var path = ExtractDb(await DownloadAsync(client));
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO __EFMigrationsHistory VALUES ('29991231000000_Future', '10.0.0')";
                command.ExecuteNonQuery();
            }

            await AssertRejectedAsync(
                await UploadAsync(client, BuildZip(path, ValidManifest)),
                "BackupFromNewerVersion"
            );
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Empty(StagingFiles(host).Except(before));
    }

    [Fact]
    public async Task Upload_WhileJobActive_Is409()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        var zip = await DownloadAsync(client);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(
                new Job
                {
                    Type = "GraveInvoices",
                    Status = JobStatus.Running,
                    TotalCount = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await UploadAsync(client, zip);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "JobAlreadyRunning",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Upload_LiveHasNewerInvoice_FlagsIt()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = StagingFiles(host);
        var zip = await DownloadAsync(client);
        long recipientId;
        long itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 7m });
            var item = new InvoiceItem
            {
                Number = "4001",
                Description = "Grabpflege",
                VatRate = 7m,
                DefaultPrice = 120.00m,
            };
            var recipient = new Recipient
            {
                Number = "E-0004",
                Name = "Familie Mahr",
                Address = "Musterstraße 1\n12345 Musterstadt",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            recipientId = recipient.Id;
            itemId = item.Id;
        }
        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await UploadAsync(client, zip);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.True(
            json.RootElement.GetProperty("summary").GetProperty("newerInvoicesHere").GetBoolean()
        );
    }

    [Fact]
    public async Task Discard_RemovesStagingFile_AndUnknownTokenIsNoOp()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var token = await StageAsync(client, await DownloadAsync(client));
        Assert.Contains(StagingFiles(host), f => f.EndsWith($"{token}.db"));

        var discarded = await client.PostAsync(
            "/odata/DiscardBackup",
            ODataJson.Body(new { token })
        );
        var unknown = await client.PostAsync(
            "/odata/DiscardBackup",
            ODataJson.Body(new { token = "../../x" })
        );

        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.DoesNotContain(StagingFiles(host), f => f.EndsWith($"{token}.db"));
    }
}
