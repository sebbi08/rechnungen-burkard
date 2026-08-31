namespace Burkard.Invoicing.Domain.Entities;

public sealed class Invoice
{
    public long Id { get; set; }
    public long RootId { get; set; } // = Id for originals; shared by revisions
    public int RevisionNumber { get; set; } // 0 = original

    /// <summary>True when no other invoice shares this RootId with a higher
    /// RevisionNumber. Lists and bulk downloads show the latest revision only,
    /// which cannot be expressed as an OData $filter without this flag.</summary>
    public bool IsLatestRevision { get; set; } = true;
    public int Year { get; set; } // numbering year = generation year
    public int Sequence { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public long? GraveId { get; set; }
    public int? PeriodYear { get; set; } // invoiced half-year period (grave invoices)
    public int? PeriodHalf { get; set; }
    public decimal? SharePercent { get; set; } // grave split share, e.g. 50 or 33.3333
    public long RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public string Language { get; set; } = "de"; // document language at generation time
    public DateTime GeneratedAt { get; set; } // UTC

    /// <summary>The day the invoice was marked paid; null while it is unpaid.
    /// Nullability carries the whole meaning — there is no second flag to
    /// disagree with it. Only SetInvoicePaid writes it, and the UI reads it as
    /// paid/unpaid without ever showing the date.</summary>
    public DateOnly? PaidOn { get; set; }
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
    /// generated before this column existed.</summary>
    public decimal? GraveUnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public int LineNumber { get; set; }
}
