using System.ComponentModel.DataAnnotations.Schema;

namespace Burkard.Invoicing.Domain.Entities;

public sealed class Invoice
{
    public long Id { get; set; }
    public long RootId { get; set; }          // = Id for originals; shared by revisions
    public int RevisionNumber { get; set; }   // 0 = original
    public int Year { get; set; }             // numbering year = generation year
    public int Sequence { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public long? GraveId { get; set; }
    public int? PeriodYear { get; set; }      // invoiced half-year period (grave invoices)
    public int? PeriodHalf { get; set; }
    public decimal? SharePercent { get; set; } // grave split share, e.g. 50 or 33.3333
    public long RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public string Language { get; set; } = "de"; // document language at generation time
    public DateTime GeneratedAt { get; set; } // UTC
    public List<InvoiceLine> Lines { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
}

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long? ItemId { get; set; }
    public string ItemNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Count { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>On a grave invoice, the line's price before it was split across
    /// the grave's recipients. Null on non-grave invoices and on grave invoices
    /// generated before this column existed.
    /// <see cref="NotMappedAttribute"/> until the migration that backs it with a
    /// column lands (a later task); without it, EF Core's model-vs-migrations
    /// check on <c>Database.Migrate()</c> fails startup everywhere.</summary>
    [NotMapped]
    public decimal? GraveUnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public int LineNumber { get; set; }
}
