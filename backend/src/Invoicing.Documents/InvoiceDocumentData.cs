namespace Burkard.Invoicing.Documents;

public sealed class InvoiceDocumentData
{
    public string InvoiceNumber { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";

    public string? RecipientNumber { get; set; }

    /// <summary>Set on grave invoices only. <see cref="GravePosition"/> is the
    /// member name ("DoubleOutside"); DocumentTexts turns it into the short
    /// form the invoice prints.</summary>
    public string? GraveNumber { get; set; }
    public string? GraveSiteName { get; set; }
    public string? GraveFieldNumber { get; set; }
    public string? GravePosition { get; set; }

    public string? ShareText { get; set; }

    /// <summary>A free text printed above the first line, starting where the
    /// descriptions start. Empty prints nothing; never set on grave invoices.</summary>
    public string IntroText { get; set; } = "";
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";

    /// <summary>The document is a quote: headed Angebot instead of Rechnung.
    /// <see cref="InvoiceNumber"/> then holds the quote's number. A quote is
    /// never rendered as a copy.</summary>
    public bool IsQuote { get; set; }
    public IReadOnlyList<LineData> Lines { get; set; } = Array.Empty<LineData>();
    public IReadOnlyList<VatSummary> VatSummaries { get; set; } = Array.Empty<VatSummary>();
    public decimal NetTotal { get; set; }
    public decimal TotalVat { get; set; }
    public decimal GrossTotal { get; set; }

    /// <summary>Set when the lines are the whole grave's and the recipient pays a
    /// share of them. Null on every other invoice, including grave invoices
    /// generated before that change, whose lines are already the share.</summary>
    public ShareData? Share { get; set; }

    /// <summary>One recipient's part of a grave bill: the share of the net, the
    /// VAT worked out from that share per rate, and what they owe.</summary>
    public sealed record ShareData(
        decimal Percent,
        decimal NetTotal,
        IReadOnlyList<VatSummary> VatSummaries,
        decimal VatTotal,
        decimal GrossTotal
    );

    public sealed record LineData(
        int Position,
        string ItemNumber,
        string Description,
        int Count,
        decimal UnitPrice,
        decimal VatRate,
        decimal LineTotal,
        decimal VatAmount,
        // the price was entered gross; UnitPrice is still the net
        bool PriceIncludesVat = false,
        // printed under the description, in that column; empty prints nothing
        string Comment = "",
        // the gross per unit as entered, on a line that was entered VAT-included
        // since lines became gross-led; null on every other line, and then the
        // unit price is worked out from UnitPrice as before
        decimal? GrossUnitPrice = null
    );

    public sealed record VatSummary(
        decimal Rate,
        decimal NetTotal,
        decimal VatAmount,
        decimal GrossTotal
    );
}
