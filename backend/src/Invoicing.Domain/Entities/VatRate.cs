namespace Burkard.Invoicing.Domain.Entities;

public sealed class VatRate
{
    public long Id { get; set; }
    public decimal Rate { get; set; }
}
