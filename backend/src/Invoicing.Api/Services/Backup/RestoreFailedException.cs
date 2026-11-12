namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>A restore that failed after it had started swapping files. The
/// replaced database was put back unless the log says otherwise.</summary>
public sealed class RestoreFailedException(Exception inner)
    : Exception("Restoring the backup failed.", inner);
