using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
}
