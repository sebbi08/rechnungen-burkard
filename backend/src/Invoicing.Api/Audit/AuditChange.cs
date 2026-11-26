namespace Burkard.Invoicing.Api.Audit;

/// <summary>One field of a subject, old and new, in invariant form (see
/// <see cref="AuditValues"/>); the frontend formats them by
/// <see cref="Type"/>: text, money, month, date, int, bool, position,
/// priceSource or invoice. <see cref="Label"/> names the item a change is
/// about; <see cref="Period"/> the block of a rounding correction, as
/// "&lt;Kind&gt;:&lt;Year|-&gt;-&lt;Half&gt;".</summary>
public sealed record AuditChange(
    string Field,
    string? Label,
    string? Old,
    string? New,
    string Type,
    string? Period = null
);
