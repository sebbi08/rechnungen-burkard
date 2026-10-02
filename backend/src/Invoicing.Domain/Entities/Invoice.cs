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

    /// <summary>How many recipients the grave's bill was divided by, on invoices
    /// that carry the grave's lines at 100 % and apply a share. Null on every
    /// other invoice — including grave invoices generated before that change,
    /// whose lines are already one recipient's share. The document renderer
    /// branches on it, so it is what keeps those older documents reproducible.</summary>
    public int? RecipientCount { get; set; }
    public long RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";

    /// <summary>What the document prints in its head and above the lines,
    /// snapshotted like the name and address so a later change to the recipient
    /// or the grave never alters an issued invoice. Null on invoices generated
    /// before these existed; the document leaves out whatever is missing. The
    /// grave values are set on grave invoices only.</summary>
    public string? RecipientNumber { get; set; }
    public string? GraveNumber { get; set; }
    public string? GraveSiteName { get; set; }
    public string? GraveFieldNumber { get; set; }
    public GravePosition? GravePosition { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public DateTime GeneratedAt { get; set; } // UTC

    /// <summary>The day the invoice was marked paid; null while it is unpaid.
    /// Nullability carries the whole meaning — there is no second flag to
    /// disagree with it. Only SetInvoicePaid writes it, and the UI reads it as
    /// paid/unpaid without ever showing the date.</summary>
    public DateOnly? PaidOn { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
    public List<Document> Documents { get; set; } = new();

    /// <summary>Net, VAT and gross of what this invoice actually bills — the
    /// recipient's share on a grave invoice, the whole of it on every other one.
    /// Derived from <see cref="Lines"/> and <see cref="RecipientCount"/> by the
    /// same calculation the PDF uses (<c>DocumentDataBuilder</c>), so a screen
    /// showing an invoice's total never disagrees with the document it links to.
    /// <c>AppDbContext</c> excludes them from the model, so they are never
    /// persisted and need no migration. Only InvoicesController's list Get()
    /// stamps them; they default to and stay 0 on an invoice read through the
    /// single-key Get(key) route or returned directly by CreateInvoice,
    /// CreateRevision, CreateGraveInvoiceRevision or SetInvoicePaid — harmless
    /// today since nothing reads totals from those paths, but worth knowing
    /// before something starts to.</summary>
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
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

    /// <summary>True when this line's price was entered as a gross — the amount
    /// the customer pays, VAT included. <see cref="UnitPrice"/> is still the net
    /// it was converted to; this records how that net was arrived at, so a
    /// revision dialog can show the gross again and take it back unchanged.</summary>
    public bool PriceIncludesVat { get; set; }
    public int LineNumber { get; set; }
}
