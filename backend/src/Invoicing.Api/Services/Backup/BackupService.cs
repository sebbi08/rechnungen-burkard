using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services.Backup;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public BackupService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The live database file. Read off the context, not the
    /// configuration: tests swap the connection string.</summary>
    public string DatabasePath =>
        new SqliteConnectionStringBuilder(_db.Database.GetConnectionString()).DataSource;

    /// <summary>Writes a ZIP of <c>manifest.json</c> and a consistent copy of the
    /// database to <paramref name="output"/>. The copy is made with
    /// <c>VACUUM INTO</c> on a fresh connection, which includes what is still in
    /// the write-ahead log.</summary>
    public async Task WriteBackupAsync(Stream output, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!;
        var temp = Path.Combine(directory, $"backup-{Guid.NewGuid():N}.db");
        try
        {
            await using (
                var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False")
            )
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", temp);
                await command.ExecuteNonQueryAsync(ct);
            }

            var manifest = new BackupManifest(
                BackupManifest.FormatName,
                BackupManifest.CurrentFormatVersion,
                AppVersion(),
                _db.Database.GetAppliedMigrations().Last(),
                _clock.GetUtcNow()
            );

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var manifestEntry = archive.CreateEntry(BackupManifest.ManifestEntry);
            await using (var stream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, manifest, ManifestJson, ct);
            }

            var databaseEntry = archive.CreateEntry(
                BackupManifest.DatabaseEntry,
                CompressionLevel.Optimal
            );
            await using var entryStream = databaseEntry.Open();
            await using var file = new FileStream(
                temp,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true
            );
            await file.CopyToAsync(entryStream, ct);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // the assembly Program lives in; the entry assembly would be the test host's
    // under test
    private static string AppVersion() =>
        typeof(BackupService)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
