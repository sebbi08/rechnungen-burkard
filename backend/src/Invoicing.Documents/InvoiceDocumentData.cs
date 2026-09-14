namespace Burkard.Invoicing.Documents;

public sealed class InvoiceDocumentData
{
    public string InvoiceNumber { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? ShareText { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public string Language { get; set; } = DocumentTexts.DefaultLanguage;
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
        decimal VatAmount
    );

    public sealed record VatSummary(
        decimal Rate,
        decimal NetTotal,
        decimal VatAmount,
        decimal GrossTotal
    );
}
