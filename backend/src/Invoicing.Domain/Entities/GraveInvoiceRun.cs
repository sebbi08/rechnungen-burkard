namespace Burkard.Invoicing.Domain.Entities;

public sealed class GraveInvoiceRun
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public int Year { get; set; }
    public int Half { get; set; } // 1 = Jan–Jun, 2 = Jul–Dec
    public long? JobId { get; set; }
    public DateTime CreatedAt { get; set; }
}
