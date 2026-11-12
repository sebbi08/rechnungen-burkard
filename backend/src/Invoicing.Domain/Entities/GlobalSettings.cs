namespace Burkard.Invoicing.Domain.Entities;

public sealed class GlobalSettings
{
    public long Id { get; set; }

    /// <summary>What ordinary invoices print after their lines, including
    /// those converted from a quote.</summary>
    public string InvoiceEndingText { get; set; } = "";

    public string QuoteEndingText { get; set; } = "";

    /// <summary>What grave invoices for January to June print.</summary>
    public string GraveFirstHalfEndingText { get; set; } = "";

    /// <summary>What grave invoices for July to December print.</summary>
    public string GraveSecondHalfEndingText { get; set; } = "";

    public string StampWord { get; set; } = "Kopie";

    /// <summary>The ending text of a grave invoice, by the half-year it bills
    /// rather than the day it is made: a first half-year's invoice revised in
    /// August still prints the first half-year's text.</summary>
    public string GraveEndingText(int half) =>
        half == 1 ? GraveFirstHalfEndingText : GraveSecondHalfEndingText;
}
