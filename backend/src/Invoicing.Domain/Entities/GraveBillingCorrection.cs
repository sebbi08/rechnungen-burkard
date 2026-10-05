namespace Burkard.Invoicing.Domain.Entities;

/// <summary>A price the rounding correction raised for one block of a grave's
/// term and one VAT rate, as confirmed when the grave was saved. Generation
/// bills it, and checks before a run that it still matches the rule.</summary>
public sealed class GraveBillingCorrection
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public BillingExampleKind Kind { get; set; }

    /// <summary>The concrete year of a start or end block; null for a full half.</summary>
    public int? Year { get; set; }
    public int Half { get; set; }
    public decimal VatRate { get; set; }
    public long ItemId { get; set; }
    public InvoiceItem Item { get; set; } = null!;
    public decimal EnteredPrice { get; set; }
    public decimal CorrectedPrice { get; set; }
}
