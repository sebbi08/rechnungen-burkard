namespace Burkard.Invoicing.Api.Audit;

/// <summary>Whose Verlauf an entry belongs to: a grave, or an invoice chain by
/// its RootId.</summary>
public abstract record AuditSubject
{
    private AuditSubject() { }

    public sealed record Grave(long GraveId) : AuditSubject;

    public sealed record Invoice(long RootId) : AuditSubject;
}
