namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>The <c>manifest.json</c> at the root of a backup archive: what the
/// archive is and which app and schema wrote it.</summary>
public sealed record BackupManifest(
    string Format,
    int FormatVersion,
    string AppVersion,
    string LastMigration,
    DateTime CreatedAtUtc
)
{
    public const string FormatName = "rechnungen-burkard-backup";
    public const int CurrentFormatVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string DatabaseEntry = "invoicing.db";
}
