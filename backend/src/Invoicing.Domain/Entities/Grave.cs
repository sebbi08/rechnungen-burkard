namespace Burkard.Invoicing.Domain.Entities;

public sealed class Grave
{
    public long Id { get; set; }
    public string GraveNumber { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly CreatedAt { get; set; }
    public List<GraveItem> Items { get; set; } = new();
    public List<GraveRecipient> Recipients { get; set; } = new();
}

public sealed class GraveItem
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long ItemId { get; set; }
    public int Count { get; set; }
    public InvoiceItem Item { get; set; } = null!;
}

public sealed class GraveRecipient
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
}
