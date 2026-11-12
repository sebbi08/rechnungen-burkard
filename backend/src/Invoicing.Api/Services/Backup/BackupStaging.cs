using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Burkard.Invoicing.Api.Services.Backup;

public sealed record BackupSummary(
    DateTime CreatedAtUtc,
    string AppVersion,
    int Graves,
    int Recipients,
    int Invoices,
    bool NewerInvoicesHere
);

public sealed record StagedBackup(string Token, BackupSummary Summary);

/// <summary>The checks on an uploaded backup, all of them on files on disk.</summary>
internal static class BackupStaging
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the manifest and copies the database out of the archive to
    /// <paramref name="databaseTarget"/>. Rejects what is not a backup.</summary>
    public static async Task<BackupManifest> ExtractAsync(
        string zipPath,
        string databaseTarget,
        CancellationToken ct
    )
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException)
        {
            throw new BackupRejectedException("BackupInvalid");
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry(BackupManifest.ManifestEntry);
            var databaseEntry = archive.GetEntry(BackupManifest.DatabaseEntry);
            if (manifestEntry is null || databaseEntry is null)
            {
                throw new BackupRejectedException("BackupInvalid");
            }

            BackupManifest? manifest;
            try
            {
                await using var stream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                    stream,
                    ManifestJson,
                    ct
                );
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw new BackupRejectedException("BackupInvalid");
            }

            if (
                manifest is null
                || manifest.Format != BackupManifest.FormatName
                || manifest.FormatVersion != BackupManifest.CurrentFormatVersion
            )
            {
                throw new BackupRejectedException("BackupInvalid");
            }

            try
            {
                await using var source = databaseEntry.Open();
                await using var target = new FileStream(
                    databaseTarget,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true
                );
                await source.CopyToAsync(target, ct);
            }
            catch (InvalidDataException)
            {
                throw new BackupRejectedException("BackupDamaged");
            }

            return manifest;
        }
    }

    /// <summary>Checks the staged database and counts what is in it.
    /// <paramref name="knownMigrations"/> are the ones this build has.</summary>
    public static async Task<BackupSummary> CheckAsync(
        string databasePath,
        BackupManifest manifest,
        IReadOnlySet<string> knownMigrations,
        DateTime? liveLatestInvoice,
        CancellationToken ct
    )
    {
        try
        {
            // not pooled and read-only: the file is moved or deleted afterwards
            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly;Pooling=False"
            );
            await connection.OpenAsync(ct);

            if (await Scalar(connection, "PRAGMA integrity_check", ct) is not "ok")
            {
                throw new BackupRejectedException("BackupDamaged");
            }

            var history = await Scalar(
                connection,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'",
                ct
            );
            if (history is null)
            {
                throw new BackupRejectedException("BackupInvalid");
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (!knownMigrations.Contains(reader.GetString(0)))
                    {
                        throw new BackupRejectedException("BackupFromNewerVersion");
                    }
                }
            }

            // a download refuses such a copy; one that holds a queued or running
            // job anyway would block every backup and restore after restoring
            if (await HasActiveJobsAsync(connection, ct))
            {
                throw new BackupRejectedException("BackupDamaged");
            }

            var graves = Convert.ToInt32(
                await Scalar(connection, "SELECT COUNT(*) FROM Graves", ct)
            );
            var recipients = Convert.ToInt32(
                await Scalar(connection, "SELECT COUNT(*) FROM Recipients", ct)
            );
            var invoices = Convert.ToInt32(
                await Scalar(connection, "SELECT COUNT(DISTINCT RootId) FROM Invoices", ct)
            );
            var backupLatest = await Scalar(
                connection,
                "SELECT MAX(GeneratedAt) FROM Invoices",
                ct
            );
            var newer =
                liveLatestInvoice is { } live
                && (
                    backupLatest is not string text
                    || live
                        > DateTime.Parse(
                            text,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind
                        )
                );

            return new BackupSummary(
                manifest.CreatedAtUtc,
                manifest.AppVersion,
                graves,
                recipients,
                invoices,
                newer
            );
        }
        catch (SqliteException)
        {
            throw new BackupRejectedException("BackupDamaged");
        }
    }

    /// <summary>Whether the database on <paramref name="connection"/> holds a job
    /// that is queued or running.</summary>
    public static async Task<bool> HasActiveJobsAsync(
        SqliteConnection connection,
        CancellationToken ct
    ) =>
        Convert.ToInt64(
            await Scalar(
                connection,
                $"SELECT EXISTS (SELECT 1 FROM Jobs WHERE Status IN "
                    + $"({(int)JobStatus.Queued}, {(int)JobStatus.Running}))",
                ct
            )
        ) != 0;

    private static async Task<object?> Scalar(
        SqliteConnection connection,
        string sql,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }
}
