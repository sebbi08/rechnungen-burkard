using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Backup;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
        Assert.Equal("20261112142130_Quotes", root.GetProperty("lastMigration").GetString());
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

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string token) =>
        client.PostAsync("/odata/RestoreBackup", ODataJson.Body(new { token }));

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code
    )
    {
        Assert.Equal(status, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task CreateRecipientAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = name, Address = "Hauptstraße 1\n99999 Musterdorf" })
        );
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string[]> RecipientNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/odata/Recipients?$select=Name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(r => r.GetProperty("Name").GetString()!)
            .ToArray();
    }

    private static async Task SetStampWordAsync(HttpClient client, string word) =>
        (
            await client.PatchAsync("/odata/Settings", ODataJson.Body(new { StampWord = word }))
        ).EnsureSuccessStatusCode();

    private static async Task<string> StampWordAsync(HttpClient client)
    {
        using var json = await ODataJson.ReadAsync(await client.GetAsync("/odata/Settings"));
        return json.RootElement.GetProperty("StampWord").GetString()!;
    }

    private static string[] SafetyCopies(ApiTestHost host) =>
        Directory
            .GetFiles(Path.GetDirectoryName(host.DbFilePath)!, "*.before-restore-*")
            .Select(f => Path.GetFileName(f))
            .Order()
            .ToArray();

    private static long CountRecipients(string dbPath, string name)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Recipients WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);
        return (long)command.ExecuteScalar()!;
    }

    private static Job ActiveJob() =>
        new()
        {
            Type = "GraveInvoices",
            Status = JobStatus.Queued,
            TotalCount = 1,
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task Restore_ReplacesAllData_AndKeepsSafetyCopy()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SetStampWordAsync(client, "Vorher");
        await CreateRecipientAsync(client, "A");
        var zip = await DownloadAsync(client);
        await CreateRecipientAsync(client, "B");
        await SetStampWordAsync(client, "Nachher");
        var token = await StageAsync(client, zip);

        var response = await RestoreAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var names = await RecipientNamesAsync(client);
        Assert.Contains("A", names);
        Assert.DoesNotContain("B", names);
        Assert.Equal("Vorher", await StampWordAsync(client));
        // the replaced database, whole, with what was still in its write-ahead log
        var safetyCopy = host.DbFilePath + ".before-restore-20261006-120000";
        Assert.True(File.Exists(safetyCopy));
        Assert.Equal(1, CountRecipients(safetyCopy, "B"));
        Assert.DoesNotContain(StagingFiles(host), f => Path.GetFileName(f).StartsWith(token));
        using var connection = new SqliteConnection($"Data Source={host.DbFilePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar());
    }

    [Fact]
    public async Task Restore_Twice_KeepsBothSafetyCopies()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var zip = await DownloadAsync(client);

        var first = await RestoreAsync(client, await StageAsync(client, zip));
        var second = await RestoreAsync(client, await StageAsync(client, zip));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(
            new[]
            {
                "invoicing.db.before-restore-20261006-120000",
                "invoicing.db.before-restore-20261006-120000-2",
            },
            SafetyCopies(host)
        );
    }

    [Fact]
    public async Task Restore_OlderBackup_IsMigrated()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var path = Path.Combine(Path.GetTempPath(), $"backup-test-{Guid.NewGuid():N}.db");
        try
        {
            using (
                var db = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlite($"Data Source={path};Pooling=False")
                        .Options
                )
            )
            {
                db.GetService<IMigrator>().Migrate("20261101095804_RecipientEmailAndCemeteries");
            }
            var token = await StageAsync(client, BuildZip(path, ValidManifest));

            var response = await RestoreAsync(client, token);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var graves = await client.GetAsync("/odata/Graves?$select=ContractNumber");
            Assert.Equal(HttpStatusCode.OK, graves.StatusCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Restore_MigrationFails_PutsOldDatabaseBack()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var path = ExtractDb(await DownloadAsync(client));
        try
        {
            // the last migration then runs again on a schema that already has it
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "DELETE FROM __EFMigrationsHistory WHERE MigrationId = "
                    + "(SELECT MAX(MigrationId) FROM __EFMigrationsHistory)";
                Assert.Equal(1, command.ExecuteNonQuery());
            }
            await CreateRecipientAsync(client, "Live");
            var token = await StageAsync(client, BuildZip(path, ValidManifest));

            var response = await RestoreAsync(client, token);

            await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "RestoreFailed");
            Assert.Contains("Live", await RecipientNamesAsync(client));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            Assert.Empty(SafetyCopies(host));
            Assert.DoesNotContain(StagingFiles(host), f => Path.GetFileName(f).StartsWith(token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Restore_TokenUsedOrDiscarded_IsUnknown()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var zip = await DownloadAsync(client);
        var used = await StageAsync(client, zip);
        Assert.Equal(HttpStatusCode.NoContent, (await RestoreAsync(client, used)).StatusCode);

        var again = await RestoreAsync(client, used);

        await AssertErrorAsync(again, HttpStatusCode.NotFound, "BackupTokenUnknown");

        var discarded = await StageAsync(client, zip);
        await client.PostAsync("/odata/DiscardBackup", ODataJson.Body(new { token = discarded }));

        var afterDiscard = await RestoreAsync(client, discarded);

        await AssertErrorAsync(afterDiscard, HttpStatusCode.NotFound, "BackupTokenUnknown");
    }

    [Fact]
    public async Task Restore_WhileJobActive_Is409()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var token = await StageAsync(client, await DownloadAsync(client));
        await CreateRecipientAsync(client, "Live");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(ActiveJob());
            await db.SaveChangesAsync();
        }

        var response = await RestoreAsync(client, token);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "JobAlreadyRunning");
        Assert.Contains("Live", await RecipientNamesAsync(client));
        Assert.Empty(SafetyCopies(host));
    }

    [Fact]
    public async Task RequestsWhileGateClosed_Answer503()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var gate = host.Services.GetRequiredService<DatabaseGate>();

        await gate.CloseAsync(CancellationToken.None);
        var closed = await client.GetAsync("/odata/Recipients");
        var health = await client.GetAsync("/healthz");
        gate.Open();
        var open = await client.GetAsync("/odata/Recipients");

        await AssertErrorAsync(closed, HttpStatusCode.ServiceUnavailable, "RestoreInProgress");
        await AssertErrorAsync(health, HttpStatusCode.ServiceUnavailable, "RestoreInProgress");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    /// <summary>The restore request goes around the gate, so it has to keep off
    /// the database by itself while another restore is swapping: standing
    /// between moving the live file aside and moving the backup in, a query
    /// would open an empty database at the live path.</summary>
    [Fact]
    public async Task RestoreWhileAnotherIsSwapping_Answers503_AndTouchesNoDatabase()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var token = await StageAsync(client, await DownloadAsync(client));
        var gate = host.Services.GetRequiredService<DatabaseGate>();
        var aside = host.DbFilePath + ".test-aside";

        await gate.CloseAsync(CancellationToken.None);
        SqliteConnection.ClearAllPools();
        File.Move(host.DbFilePath, aside);
        HttpResponseMessage response;
        try
        {
            response = await RestoreAsync(client, token);
            Assert.False(File.Exists(host.DbFilePath), "the restore request opened the database");
        }
        finally
        {
            File.Delete(host.DbFilePath);
            File.Move(aside, host.DbFilePath);
            gate.Open();
        }

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "RestoreInProgress");
        // the token is still good once the other restore is done
        Assert.Equal(HttpStatusCode.NoContent, (await RestoreAsync(client, token)).StatusCode);
    }

    [Fact]
    public async Task JobRunner_StartsNoJobWhileGateClosed()
    {
        using var host = new ApiTestHost();
        var gate = host.Services.GetRequiredService<DatabaseGate>();
        await gate.CloseAsync(CancellationToken.None);
        long jobId;
        using (var scope = host.Services.CreateScope())
        {
            var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
            jobId = (await jobs.EnqueueAsync("Test", null, 10, CancellationToken.None)).Id;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(JobStatus.Queued, await JobStatusAsync(host, jobId));
        gate.Open();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await JobStatusAsync(host, jobId) != JobStatus.Succeeded)
        {
            Assert.True(DateTime.UtcNow < deadline, "the job did not run after the gate opened");
            await Task.Delay(50);
        }
    }

    private static async Task<JobStatus> JobStatusAsync(ApiTestHost host, long jobId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Jobs.SingleAsync(j => j.Id == jobId)).Status;
    }

    private const string GenerationHeader = "X-Data-Generation";
    private static readonly string StaleWriteName = $"Stale-{Guid.NewGuid():N}";

    private static async Task<string> GenerationAsync(HttpClient client)
    {
        var response = await client.GetAsync("/odata/Recipients?$top=1");
        return Assert.Single(response.Headers.GetValues(GenerationHeader));
    }

    private static async Task RestoreOwnBackupAsync(HttpClient client)
    {
        var token = await StageAsync(client, await DownloadAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, (await RestoreAsync(client, token)).StatusCode);
    }

    private static Task<HttpResponseMessage> PostRecipientAsync(
        HttpClient client,
        string? generation
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/odata/Recipients")
        {
            Content = ODataJson.Body(
                new { Name = StaleWriteName, Address = "Hauptstraße 1\n99999 Musterdorf" }
            ),
        };
        if (generation is not null)
        {
            request.Headers.Add(GenerationHeader, generation);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task Responses_CarryGeneration()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        Assert.False(string.IsNullOrEmpty(await GenerationAsync(client)));
        var missing = await client.GetAsync("/odata/NoSuchSet");
        Assert.True(missing.Headers.Contains(GenerationHeader));
    }

    [Fact]
    public async Task StaleGeneration_WriteRefused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = await GenerationAsync(client);
        await RestoreOwnBackupAsync(client);

        var response = await PostRecipientAsync(client, before);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "DataReplaced");
        Assert.NotEqual(before, Assert.Single(response.Headers.GetValues(GenerationHeader)));
        Assert.DoesNotContain(StaleWriteName, await RecipientNamesAsync(client));
    }

    [Fact]
    public async Task StaleGeneration_ReadAllowed()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var before = await GenerationAsync(client);
        await RestoreOwnBackupAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Recipients");
        request.Headers.Add(GenerationHeader, before);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task CurrentGeneration_WriteAllowed()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await RestoreOwnBackupAsync(client);

        var response = await PostRecipientAsync(client, await GenerationAsync(client));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task NoGenerationHeader_WriteAllowed()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await RestoreOwnBackupAsync(client);

        Assert.Equal(HttpStatusCode.Created, (await PostRecipientAsync(client, null)).StatusCode);
    }

    [Fact]
    public async Task GateClosed_503CarriesGeneration()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var generation = await GenerationAsync(client);
        var gate = host.Services.GetRequiredService<DatabaseGate>();

        await gate.CloseAsync(CancellationToken.None);
        var closed = await client.GetAsync("/odata/Recipients");
        gate.Open();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        Assert.Equal(generation, Assert.Single(closed.Headers.GetValues(GenerationHeader)));
    }

    [Fact]
    public async Task Load_ReadsWhatRestoreStored()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await RestoreOwnBackupAsync(client);
        var served = await GenerationAsync(client);

        // what a restart does: a new instance, the same file
        var restarted = new DataGeneration();
        using (var scope = host.Services.CreateScope())
        {
            restarted.Load(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        Assert.NotEqual(0, restarted.Current);
        Assert.Equal(served, restarted.Current.ToString());
    }

    [Fact]
    public async Task FreshDatabase_HasGenerationZero()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        Assert.Equal("0", await GenerationAsync(client));
        var loaded = new DataGeneration();
        using var scope = host.Services.CreateScope();
        loaded.Load(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.Equal(0, loaded.Current);
    }

    [Fact]
    public async Task Generation_IsStoredInDatabase()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await RestoreOwnBackupAsync(client);

        using var connection = new SqliteConnection($"Data Source={host.DbFilePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(await GenerationAsync(client), ((long)command.ExecuteScalar()!).ToString());
    }

    [Fact]
    public async Task Gate_CloseWaitsForThoseInside_AndRefusesNewcomers()
    {
        var gate = new DatabaseGate();
        Assert.True(gate.TryEnter());

        var closing = gate.CloseAsync(CancellationToken.None);

        Assert.True(gate.IsClosed);
        Assert.False(gate.TryEnter());
        Assert.False(closing.IsCompleted);
        gate.Exit();
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Open();
        Assert.False(gate.IsClosed);
        Assert.True(gate.TryEnter());
    }

    [Fact]
    public async Task Gate_CloseCancelled_OpensAgain()
    {
        var gate = new DatabaseGate();
        Assert.True(gate.TryEnter());
        using var cts = new CancellationTokenSource();

        var closing = gate.CloseAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
        Assert.False(gate.IsClosed);
        gate.Exit();
        // and it closes again afterwards
        await gate.CloseAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        gate.Open();
    }

    private static string[] TemporaryBackupFiles(ApiTestHost host) =>
        Directory
            .GetFiles(Path.GetDirectoryName(host.DbFilePath)!, "backup-*")
            .Select(f => Path.GetFileName(f))
            .ToArray();

    [Fact]
    public async Task Download_LeavesNoTemporaryFiles()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/DownloadBackup");
        await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(TemporaryBackupFiles(host));
    }

    /// <summary>The job check before the copy cannot see a job created between it
    /// and VACUUM INTO's snapshot, so the copy itself is asked again. Called on
    /// the service: the request's own check would answer first.</summary>
    [Fact]
    public async Task CreateBackup_CopyHoldingActiveJob_IsRefused()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Jobs.Add(ActiveJob());
        await db.SaveChangesAsync();
        var backup = scope.ServiceProvider.GetRequiredService<BackupService>();

        var e = await Assert.ThrowsAsync<BackupRejectedException>(() =>
            backup.CreateBackupAsync(CancellationToken.None)
        );

        Assert.Equal("JobAlreadyRunning", e.Code);
        Assert.Empty(TemporaryBackupFiles(host));
    }

    [Fact]
    public async Task Upload_BackupHoldingActiveJob_IsBackupDamaged()
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
                    "INSERT INTO Jobs (Type, Status, ProgressPercent, TotalCount, ProcessedCount, CreatedAt) "
                    + "VALUES ('GraveInvoices', 0, 0, 1, 0, '2026-10-06 12:00:00')";
                command.ExecuteNonQuery();
            }

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
    public async Task Upload_NotMultipart_IsArchiveRequired()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var content = new ByteArrayContent(await DownloadAsync(client));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/zip"
        );

        var response = await client.PostAsync("/odata/UploadBackup", content);

        await AssertRejectedAsync(response, "ArchiveRequired");
    }

    [Fact]
    public async Task Upload_FileAfterOtherFields_IsStaged()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var form = new MultipartFormDataContent
        {
            { new StringContent("x"), "other" },
            { new ByteArrayContent(await DownloadAsync(client)), "file", "sicherung.zip" },
        };

        var response = await client.PostAsync("/odata/UploadBackup", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A directory under the name of the database's journal: the
    /// restored database's migration fails beside it, and the rollback cannot
    /// delete it, so putting the old database back fails too.</summary>
    [Fact]
    public async Task Restore_RollbackFails_IsReported_KeepsGateClosed_AndLeavesMarker()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var path = ExtractDb(await DownloadAsync(client));
        HttpResponseMessage response;
        try
        {
            // the migration fails as in Restore_MigrationFails_PutsOldDatabaseBack
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "DELETE FROM __EFMigrationsHistory WHERE MigrationId = "
                    + "(SELECT MAX(MigrationId) FROM __EFMigrationsHistory)";
                command.ExecuteNonQuery();
            }
            await CreateRecipientAsync(client, "Live");
            var token = await StageAsync(client, BuildZip(path, ValidManifest));
            Directory.CreateDirectory(host.DbFilePath + "-journal");

            response = await RestoreAsync(client, token);
        }
        finally
        {
            File.Delete(path);
        }

        await AssertErrorAsync(
            response,
            HttpStatusCode.InternalServerError,
            "RestoreRollbackFailed"
        );
        await AssertErrorAsync(
            await client.GetAsync("/odata/Recipients"),
            HttpStatusCode.ServiceUnavailable,
            "RestoreInProgress"
        );
        var safetyCopy = host.DbFilePath + ".before-restore-20261006-120000";
        Assert.Equal(1, CountRecipients(safetyCopy, "Live"));
        var marker = host.DbFilePath + ".restore-incomplete";
        Assert.True(File.Exists(marker));
        Assert.Contains(safetyCopy, File.ReadAllText(marker));
    }

    /// <summary>After a failed rollback the live path holds whatever the swap
    /// left, or nothing; starting on it would migrate and seed an empty
    /// database and open the gate again.</summary>
    [Fact]
    public void Startup_WithIncompleteRestoreMarker_Refuses()
    {
        using var host = new ApiTestHost();
        File.WriteAllText(host.DbFilePath + ".restore-incomplete", "x");

        var e = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("restore-incomplete", e.ToString());
        Assert.False(File.Exists(host.DbFilePath), "the database was created or migrated");
    }

    [Fact]
    public void Startup_SweepsLeftoversOfDownloadsAndOldUploads()
    {
        using var host = new ApiTestHost();
        var directory = Path.GetDirectoryName(host.DbFilePath)!;
        var staging = Directory
            .CreateDirectory(Path.Combine(directory, "restore-staging"))
            .FullName;
        static string Touch(string dir, string name)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "x");
            return path;
        }
        var hex = Guid.NewGuid().ToString("N");
        var leftovers = new[]
        {
            Touch(directory, $"backup-{hex}.db"),
            Touch(directory, $"backup-{hex}.db-journal"),
            Touch(directory, $"backup-{hex}.zip"),
        };
        var oldUpload = Touch(staging, $"{hex}.db");
        File.SetLastWriteTimeUtc(oldUpload, DateTime.UtcNow.AddHours(-2));
        var freshUpload = Touch(staging, $"{Guid.NewGuid():N}.db");
        var ownFile = Touch(directory, "backup-mine.db");

        using var client = host.CreateClient();

        Assert.All(leftovers, f => Assert.False(File.Exists(f), f));
        Assert.False(File.Exists(oldUpload));
        Assert.True(File.Exists(freshUpload));
        Assert.True(File.Exists(ownFile));
    }
}
