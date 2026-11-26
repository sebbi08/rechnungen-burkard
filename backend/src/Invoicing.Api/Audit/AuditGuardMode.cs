namespace Burkard.Invoicing.Api.Audit;

/// <summary>What <see cref="AuditGuardInterceptor"/> does with an audited change
/// that no action declared. Development and the tests throw, so a write path
/// that forgot to declare itself fails there; Production records an
/// <see cref="AuditActions.Unattributed"/> entry and logs a warning rather than
/// blocking the user's work.</summary>
public enum AuditGuardMode
{
    Throw,
    Record,
}
