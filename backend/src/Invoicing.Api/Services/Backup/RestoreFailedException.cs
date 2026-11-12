namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>A restore that failed after it had started swapping files.
/// <see cref="RolledBack"/> says whether the replaced database was put back;
/// when it was not, the instance has to be repaired by hand.</summary>
public sealed class RestoreFailedException(Exception inner, bool rolledBack)
    : Exception("Restoring the backup failed.", inner)
{
    public bool RolledBack { get; } = rolledBack;
}
