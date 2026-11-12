namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>A backup that cannot be used. <see cref="Code"/> is one of the stable
/// codes the frontend translates.</summary>
public sealed class BackupRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
