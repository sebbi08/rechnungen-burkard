namespace Burkard.Invoicing.Domain.Entities;

/// <summary>One line of a Verlauf: what one action did to one grave or one
/// invoice chain. Exactly one of <see cref="GraveId"/> and
/// <see cref="InvoiceRootId"/> is set. Neither is a foreign key: an entry must
/// never block or cascade a write to what it describes.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>One of the keys in <c>AuditActions</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>A JSON object of strings that qualifies the action (the month a
    /// hand-over takes effect, the half-year a run invoiced), or null.</summary>
    public string? DetailsJson { get; set; }
    public long? GraveId { get; set; }

    /// <summary>The chain's RootId, so all revisions of an invoice share one
    /// Verlauf.</summary>
    public long? InvoiceRootId { get; set; }

    /// <summary>A JSON array of <c>AuditChange</c>s, with values already
    /// resolved to what a person reads at the time of writing.</summary>
    public string ChangesJson { get; set; } = "[]";
}
