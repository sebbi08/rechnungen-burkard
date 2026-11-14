namespace Burkard.Invoicing.Domain.Entities;

/// <summary>Where a quote stands. It belongs to the whole chain of versions and
/// is kept equal on every row of it. Stored by name.</summary>
public enum QuoteState
{
    Open,
    Declined,
    Converted,
}

/// <summary>One version of a quote, shaped like an <see cref="Invoice"/> row but
/// stored in tables of its own, so nothing that reads invoices — number
/// allocation, lists, bulk downloads — can ever pick one up.</summary>
public sealed class Quote
{
    public long Id { get; set; }
    public long RootId { get; set; } // = Id for originals; shared by revisions
    public int RevisionNumber { get; set; } // 0 = original

    /// <summary>True when no other quote shares this RootId with a higher
    /// RevisionNumber, as on <see cref="Invoice.IsLatestRevision"/>.</summary>
    public bool IsLatestRevision { get; set; } = true;

    /// <summary>The local year the original was created in, kept by every
    /// revision; it and <see cref="Sequence"/> come from
    /// <see cref="QuoteNumberCounter"/>.</summary>
    public int Year { get; set; }
    public int Sequence { get; set; }
    public string QuoteNumber { get; set; } = "";
    public QuoteState State { get; set; } = QuoteState.Open;

    /// <summary>The invoice the quote became, the same on every row of the
    /// chain; null until it is converted. A plain column: the invoice outlives
    /// nothing here and nothing cascades.</summary>
    public long? InvoiceId { get; set; }
    public long RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? RecipientNumber { get; set; }

    /// <summary>The free text above the lines, as on <see cref="Invoice.IntroText"/>.</summary>
    public string IntroText { get; set; } = "";
    public string EndingText { get; set; } = "";
    public DateTime GeneratedAt { get; set; } // local wall-clock, like Invoice's
    public List<QuoteLine> Lines { get; set; } = new();
    public QuoteDocument? Document { get; set; }

    /// <summary>Net, VAT and gross as the PDF prints them, and the number of the
    /// invoice the quote became. Derived at read time, never persisted
    /// (<c>AppDbContext</c> ignores them), like <see cref="Invoice.NetTotal"/>.</summary>
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public string? InvoiceNumber { get; set; }
}

/// <summary>A line of a quote version: what <see cref="InvoiceLine"/> snapshots,
/// apart from the grave split a quote never has.</summary>
public sealed class QuoteLine
{
    public long Id { get; set; }
    public long QuoteId { get; set; }
    public long? ItemId { get; set; }
    public string ItemNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Count { get; set; }
    public decimal UnitPrice { get; set; } // always net, as on InvoiceLine
    public decimal VatRate { get; set; }
    public bool PriceIncludesVat { get; set; }

    /// <summary>The gross per unit as entered, as on
    /// <see cref="InvoiceLine.GrossUnitPrice"/>; null on net lines and on lines
    /// that existed before the column did.</summary>
    public decimal? GrossUnitPrice { get; set; }

    /// <summary>The note under the line, as on <see cref="InvoiceLine.Comment"/>.</summary>
    public string Comment { get; set; } = "";
    public int LineNumber { get; set; }
}

/// <summary>The one PDF of a quote version. A quote has no stamped copy.</summary>
public sealed class QuoteDocument
{
    public long Id { get; set; }
    public long QuoteId { get; set; }
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>The last quote sequence given out in a year. Counting here rather
/// than from the highest stored number means a deleted quote's number is never
/// given again — that quote may already have reached a customer.</summary>
public sealed class QuoteNumberCounter
{
    public int Year { get; set; }
    public int LastSequence { get; set; }
}
