namespace Burkard.Invoicing.Domain.Entities;

public enum DocumentKind
{
    Normal = 0,
    Copy = 1,
}

public sealed class Document
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public DocumentKind Kind { get; set; }
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime GeneratedAt { get; set; }
}
