namespace Burkard.Invoicing.Domain.Entities;

public sealed class InvoiceItem
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal VatRate { get; set; }
    public decimal? DefaultPrice { get; set; }
}
