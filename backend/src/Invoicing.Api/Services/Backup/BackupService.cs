using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private string StagingDirectory =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, "restore-staging");

    private static readonly Regex TokenPattern = new("^[0-9a-f]{32}$");

    /// <summary>The staged database for <paramref name="token"/>, or null when the
    /// token is malformed or nothing is staged under it.</summary>
    public string? StagedPath(string token)
    {
        if (token is null || !TokenPattern.IsMatch(token))
        {
            return null;
        }

        var path = Path.Combine(StagingDirectory, $"{token}.db");
        return File.Exists(path) ? path : null;
    }

    public void Discard(string token)
    {
        if (StagedPath(token) is { } path)
        {
            File.Delete(path);
        }
    }

    /// <summary>Saves an uploaded backup, checks it and leaves the database in the
    /// staging folder under a new token. A rejected upload leaves nothing behind.
    /// The ZIP goes to disk first: uploads can be gigabytes.</summary>
    public async Task<StagedBackup> StageAsync(Stream upload, CancellationToken ct)
    {
        Directory.CreateDirectory(StagingDirectory);
        foreach (var old in Directory.GetFiles(StagingDirectory))
        {
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddHours(-1))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException) { }
            }
        }

        var token = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(StagingDirectory, $"upload-{token}.zip");
        var dbPath = Path.Combine(StagingDirectory, $"{token}.db");
        var keep = false;
        try
        {
            await using (var zip = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
            {
                await upload.CopyToAsync(zip, ct);
            }

            var manifest = await BackupStaging.ExtractAsync(zipPath, dbPath, ct);
            File.Delete(zipPath);
            var live = await _db.Invoices.Select(i => (DateTime?)i.GeneratedAt).MaxAsync(ct);
            var summary = await BackupStaging.CheckAsync(
                dbPath,
                manifest,
                _db.Database.GetMigrations().ToHashSet(),
                live,
                ct
            );
            keep = true;
            return new StagedBackup(token, summary);
        }
        finally
        {
            File.Delete(zipPath);
            if (!keep)
            {
                File.Delete(dbPath);
            }
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
