namespace Burkard.Invoicing.Domain.Entities;

public sealed class InvoiceItem
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal VatRate { get; set; }
    public decimal? DefaultPrice { get; set; }

    /// <summary>The quarter this item is billed in; null bills it all year.
    /// Fixed once a grave bills the item, like <see cref="VatRate"/>.</summary>
    public BillingQuarter? Quarter { get; set; }
}
