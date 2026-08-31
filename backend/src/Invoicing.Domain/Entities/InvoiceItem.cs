namespace Burkard.Invoicing.Domain.Entities;

public sealed class InvoiceItem
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal VatRate { get; set; }

    /// <summary>True when prices entered for this item are gross — the amount
    /// the customer pays, VAT included. The line is still billed net: the VAT is
    /// taken back out when the line is built.</summary>
    public bool PriceIncludesVat { get; set; }
    public decimal? DefaultPrice { get; set; }
}
