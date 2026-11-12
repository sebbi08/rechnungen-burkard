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
    private readonly JobService _jobs;
    private readonly DatabaseGate _gate;
    private readonly DataGeneration _generation;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        AppDbContext db,
        IClock clock,
        JobService jobs,
        DatabaseGate gate,
        DataGeneration generation,
        IServiceScopeFactory scopes,
        ILogger<BackupService> logger
    )
    {
        _db = db;
        _clock = clock;
        _jobs = jobs;
        _gate = gate;
        _generation = generation;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>The live database file. Read off the context, not the
    /// configuration: tests swap the connection string.</summary>
    public string DatabasePath => DatabasePathOf(_db);

    public static string DatabasePathOf(AppDbContext db) =>
        Path.GetFullPath(
            new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource
        );

    private static string DirectoryOf(string databasePath) =>
        Path.GetDirectoryName(Path.GetFullPath(databasePath))!;

    /// <summary>The backup as a ZIP in a temporary file beside the database,
    /// positioned at its start. Closing the stream deletes the file; a file left
    /// by a crash is swept at the next start (<see cref="SweepLeftovers"/>).
    /// Written to disk rather than memory: the backup has no small size limit,
    /// and building it whole first keeps the answer an error if it fails.</summary>
    public async Task<Stream> CreateBackupAsync(CancellationToken ct)
    {
        var path = Path.Combine(DirectoryOf(DatabasePath), $"backup-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose
        );
        try
        {
            await WriteBackupAsync(stream, ct);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>Writes a ZIP of <c>manifest.json</c> and a consistent copy of the
    /// database to <paramref name="output"/>. The copy is made with
    /// <c>VACUUM INTO</c> on a fresh connection, which includes what is still in
    /// the write-ahead log.</summary>
    private async Task WriteBackupAsync(Stream output, CancellationToken ct)
    {
        var temp = Path.Combine(DirectoryOf(DatabasePath), $"backup-{Guid.NewGuid():N}.db");
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

            // The caller asked before the copy, but a job created between that
            // and the copy's snapshot is in it, and a restored instance would
            // refuse every backup and restore until a restart.
            await using (
                var copy = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Pooling=False")
            )
            {
                await copy.OpenAsync(ct);
                if (await BackupStaging.HasActiveJobsAsync(copy, ct))
                {
                    throw new BackupRejectedException("JobAlreadyRunning");
                }
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

    private string StagingDirectory => StagingDirectoryOf(DatabasePath);

    private static string StagingDirectoryOf(string databasePath) =>
        Path.Combine(DirectoryOf(databasePath), "restore-staging");

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
            DeleteSet(path);
        }
    }

    /// <summary>Replaces the live database with the one staged under
    /// <paramref name="token"/>, behind the closed <see cref="DatabaseGate"/>.
    /// The replaced database stays beside it as a safety copy. If anything goes
    /// wrong once the swap has begun, the replaced database is put back and
    /// <see cref="RestoreFailedException"/> is thrown. Either way the staged
    /// file is gone afterwards.</summary>
    public async Task RestoreAsync(string token, CancellationToken ct)
    {
        // Asked before closing the gate as well, so that a refusal does not hold
        // up everyone else while the requests inside drain. This request goes
        // around the gate's middleware, so it passes the gate here itself: while
        // another restore is swapping, a query would open whatever is at the
        // live path, or create an empty database there.
        if (!_gate.TryEnter())
        {
            throw new BackupRejectedException("RestoreInProgress");
        }
        try
        {
            CheckRestorable(token);
        }
        finally
        {
            _gate.Exit();
        }

        // Another restore may close the gate after the check above; then this
        // waits for it to open again, and the check below runs on its result.
        await _gate.CloseAsync(ct);
        var reopen = true;
        try
        {
            // and again behind it: a discard, a second restore of the same
            // token or a new job may have come in meanwhile
            var staged = CheckRestorable(token);
            Swap(staged, ref reopen);
        }
        finally
        {
            if (reopen)
            {
                _gate.Open();
            }
        }
    }

    private string CheckRestorable(string token)
    {
        var staged = StagedPath(token) ?? throw new BackupRejectedException("BackupTokenUnknown");
        if (_jobs.HasAnyActive())
        {
            throw new BackupRejectedException("JobAlreadyRunning");
        }

        return staged;
    }

    /// <summary>The swap itself. Runs with the gate closed and nobody inside, and
    /// is not cancellable: once files move, it finishes or rolls back. Leaves
    /// <paramref name="reopen"/> false only when the rollback failed too, so that
    /// nothing reads or writes whatever is at the live path until someone has
    /// looked; and then leaves the marker that keeps the app from starting on it
    /// (<see cref="RefuseStartAfterIncompleteRestore"/>).</summary>
    private void Swap(string staged, ref bool reopen)
    {
        var live = Path.GetFullPath(DatabasePath);
        var aside = SafetyCopyPath(live);
        var movedAside = new List<(string From, string To)>();

        // Nobody is inside the gate, so every pooled connection is idle (this
        // request's context closed its connection after its last query).
        // Closing them checkpoints the write-ahead log into the file and
        // removes it; whatever is left of it moves along with the file.
        SqliteConnection.ClearAllPools();
        try
        {
            MoveSet(live, aside, movedAside);
        }
        catch (Exception e)
        {
            // nothing new is in place: put back the part of the set that moved
            _logger.LogError(e, "Restoring a backup failed while moving the live database aside");
            var rolledBack = true;
            try
            {
                MoveBack(movedAside);
            }
            catch (Exception rollback)
            {
                rolledBack = reopen = false;
                RollbackFailed(rollback, live, aside);
            }
            DeleteSetQuietly(staged);
            throw new RestoreFailedException(e, rolledBack);
        }

        // from here on every file at the live path belongs to the backup
        try
        {
            MoveSet(staged, live, null);
            // MoveSet skips what is missing; migrating nothing would create an
            // empty database and call it a restore
            if (!File.Exists(live))
            {
                throw new IOException("The staged backup did not arrive at the database path.");
            }
            // a context of its own: the request's has seen the old database
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
            DatabaseSeeder.Seed(db);
            // last, and before the gate opens: no request sees the restored data
            // under the old name
            _generation.Renew(db);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Restoring a backup failed; putting the replaced database back");
            var rolledBack = true;
            try
            {
                SqliteConnection.ClearAllPools();
                DeleteSet(live);
                MoveBack(movedAside);
            }
            catch (Exception rollback)
            {
                rolledBack = reopen = false;
                RollbackFailed(rollback, live, aside);
            }
            DeleteSetQuietly(staged);
            throw new RestoreFailedException(e, rolledBack);
        }

        _logger.LogInformation(
            "Restored a backup; the replaced database was kept as {SafetyCopy}",
            Path.GetFileName(aside)
        );
    }

    private const string RecoverySection =
        "README.md, section \"Recovering from a failed restore\"";

    /// <summary>Next to the database while a failed rollback has not been
    /// repaired by hand. Holds the path of the safety copy.</summary>
    public static string IncompleteRestoreMarker(string databasePath) =>
        Path.GetFullPath(databasePath) + ".restore-incomplete";

    /// <summary>Logs the failed rollback and leaves the marker. The marker is what
    /// survives a restart: without it the next start would migrate and seed
    /// whatever is at the live path, or a new empty database.</summary>
    private void RollbackFailed(Exception e, string live, string aside)
    {
        _logger.LogCritical(
            e,
            "Restoring a backup failed and the replaced database could not be put back. "
                + "It is kept as {SafetyCopy} (with its -wal and -shm files, if any) beside "
                + "the database. Every request is refused, and the app will not start again, "
                + "until it has been put back by hand as described in {Section}.",
            aside,
            RecoverySection
        );
        try
        {
            File.WriteAllText(IncompleteRestoreMarker(live), aside + Environment.NewLine);
        }
        catch (Exception marker) when (marker is IOException or UnauthorizedAccessException)
        {
            _logger.LogCritical(
                marker,
                "The marker {Marker} could not be written; do not restart the app before "
                    + "putting the replaced database back by hand",
                IncompleteRestoreMarker(live)
            );
        }
    }

    /// <summary>Called at startup, before anything opens the database: refuses to
    /// start while a failed rollback has not been repaired by hand.</summary>
    public static void RefuseStartAfterIncompleteRestore(string databasePath, ILogger logger)
    {
        var marker = IncompleteRestoreMarker(databasePath);
        if (!File.Exists(marker))
        {
            return;
        }

        logger.LogCritical(
            "A restore failed and the replaced database was not put back; it is named in "
                + "{Marker}. Put it back by hand as described in {Section}, delete the marker, "
                + "and start the app again.",
            marker,
            RecoverySection
        );
        throw new InvalidOperationException(
            $"Refusing to start: a restore was not completed ({marker}). "
                + $"See {RecoverySection}."
        );
    }

    private static readonly Regex DownloadLeftover = new(
        @"^backup-[0-9a-f]{32}\.(db|zip)(-journal|-wal|-shm)?$"
    );

    /// <summary>Called at startup: removes the temporary files of downloads a
    /// crash interrupted, and staged uploads older than an hour. Nothing is in
    /// use yet, so every download leftover can go.</summary>
    public static void SweepLeftovers(string databasePath, ILogger logger)
    {
        var directory = DirectoryOf(databasePath);
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "backup-*"))
            {
                if (DownloadLeftover.IsMatch(Path.GetFileName(file)))
                {
                    DeleteQuietly(file, logger);
                }
            }
        }

        SweepStaging(StagingDirectoryOf(databasePath), logger);
    }

    /// <summary>Staged uploads, and the ZIPs they came in, are kept for an
    /// hour.</summary>
    private static void SweepStaging(string staging, ILogger logger)
    {
        if (!Directory.Exists(staging))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(staging))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1))
            {
                DeleteQuietly(file, logger);
            }
        }
    }

    private static void DeleteQuietly(string file, ILogger logger)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "A leftover backup file could not be deleted");
        }
    }

    /// <summary>The database file and the files SQLite keeps beside it under its
    /// name. They only make sense together, so they move together.</summary>
    private static readonly string[] SetSuffixes = ["", "-wal", "-shm", "-journal"];

    private static IEnumerable<string> Set(string path) => SetSuffixes.Select(s => path + s);

    /// <summary>Moves those files of <paramref name="from"/>'s set that exist,
    /// never over an existing file. Records each move in
    /// <paramref name="moved"/> as it happens.</summary>
    private static void MoveSet(string from, string to, List<(string From, string To)>? moved)
    {
        foreach (var suffix in SetSuffixes)
        {
            if (File.Exists(from + suffix))
            {
                File.Move(from + suffix, to + suffix, overwrite: false);
                moved?.Add((from + suffix, to + suffix));
            }
        }
    }

    private static void MoveBack(List<(string From, string To)> moved)
    {
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            File.Move(moved[i].To, moved[i].From, overwrite: false);
        }
    }

    private static void DeleteSet(string path)
    {
        foreach (var file in Set(path))
        {
            File.Delete(file);
        }
    }

    private void DeleteSetQuietly(string path)
    {
        try
        {
            DeleteSet(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the staging folder is swept of old files on the next upload or start
            _logger.LogWarning(e, "A staged backup could not be deleted");
        }
    }

    /// <summary>Beside the live database, named after the local time, as
    /// <c>scripts/reset-dev-db.sh</c> keeps the old one. A second restore within
    /// the same second gets a numbered name rather than failing.</summary>
    private string SafetyCopyPath(string live)
    {
        var stem = $"{live}.before-restore-{_clock.GetLocalNow():yyyyMMdd-HHmmss}";
        var path = stem;
        for (var n = 2; Set(path).Any(File.Exists); n++)
        {
            path = $"{stem}-{n}";
        }

        return path;
    }

    /// <summary>Saves an uploaded backup, checks it and leaves the database in the
    /// staging folder under a new token. A rejected upload leaves nothing behind.
    /// The ZIP goes to disk first: uploads can be gigabytes.</summary>
    public async Task<StagedBackup> StageAsync(Stream upload, CancellationToken ct)
    {
        Directory.CreateDirectory(StagingDirectory);
        SweepStaging(StagingDirectory, _logger);

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
            var liveQuote = await _db.Quotes.Select(q => (DateTime?)q.GeneratedAt).MaxAsync(ct);
            var summary = await BackupStaging.CheckAsync(
                dbPath,
                manifest,
                _db.Database.GetMigrations().ToHashSet(),
                live,
                liveQuote,
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
