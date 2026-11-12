using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceLineInput
{
    public long ItemId { get; set; }
    public int Count { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool PriceIncludesVat { get; set; }
}

public sealed class InvoiceService
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly LineBuilder _lines;
    private readonly InvoiceDocumentFactory _documentFactory;
    private readonly IClock _clock;

    public InvoiceService(
        AppDbContext db,
        InvoiceNumberService numbers,
        LineBuilder lines,
        InvoiceDocumentFactory documentFactory,
        IClock clock
    )
    {
        _db = db;
        _numbers = numbers;
        _lines = lines;
        _documentFactory = documentFactory;
        _clock = clock;
    }

    public async Task<Invoice> CreateAsync(
        long recipientId,
        IReadOnlyList<InvoiceLineInput> lineInputs,
        CancellationToken ct
    )
    {
        var recipient =
            await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

        var lines = await _lines.BuildAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        RequireAddress(recipient);

        // SQLite opens the transaction with BEGIN IMMEDIATE, so it holds the
        // write lock from before the highest number is read until the invoice is
        // in: creates at once take turns rather than read the same number. (They
        // used to read it first and retry on the unique index, and a request that
        // lost the race three times in a row came back as a 500.)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var invoice = await PersistAsync(recipient, settings, lines, quoteId: null, ct);
        await tx.CommitAsync(ct);
        return invoice;
    }

    /// <summary>Turns the latest version of an open quote into an ordinary new
    /// invoice: the next invoice number, today's date, the current ending text
    /// and stamp word and both PDFs, as <see cref="CreateAsync"/> gives them,
    /// with the lines exactly as they were quoted and the recipient as it is
    /// now. Every version of the quote becomes converted and points at the
    /// invoice; the invoice points back at the quote's root.</summary>
    public async Task<Invoice> CreateFromQuoteAsync(long quoteId, CancellationToken ct)
    {
        // Everything happens inside the one transaction that allocates the
        // number. Being BEGIN IMMEDIATE, it makes conversions take turns with
        // each other and with every other write to quotes: the state read here
        // cannot change before it is written, so a quote converts once. A
        // refusal throws before anything is saved, and the rollback leaves no
        // invoice behind and so uses no number.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var rootId =
            await _db
                .Quotes.Where(q => q.Id == quoteId)
                .Select(q => (long?)q.RootId)
                .SingleOrDefaultAsync(ct)
            ?? throw new ValidationException("QuoteNotFound", "The quote does not exist.");
        var chain = await _db.Quotes.Where(q => q.RootId == rootId).ToListAsync(ct);
        var latest = chain.Single(q => q.IsLatestRevision);
        if (latest.State != QuoteState.Open)
        {
            throw new ValidationException("QuoteNotOpen", "The quote is not open.");
        }

        var recipient =
            await _db.Recipients.FindAsync(new object[] { latest.RecipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");
        RequireAddress(recipient);
        var settings = await _db.GlobalSettings.FirstAsync(ct);

        // as quoted, not through LineBuilder: a price or description changed on
        // the item since does not reach the invoice
        var lines = await _db
            .QuoteLines.Where(l => l.QuoteId == latest.Id)
            .OrderBy(l => l.LineNumber)
            .Select(l => new InvoiceLine
            {
                ItemId = l.ItemId,
                ItemNumber = l.ItemNumber,
                Description = l.Description,
                Count = l.Count,
                UnitPrice = l.UnitPrice,
                VatRate = l.VatRate,
                PriceIncludesVat = l.PriceIncludesVat,
                LineNumber = l.LineNumber,
            })
            .ToListAsync(ct);

        var invoice = await PersistAsync(recipient, settings, lines, rootId, ct);

        foreach (var quote in chain)
        {
            quote.State = QuoteState.Converted;
            quote.InvoiceId = invoice.Id;
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice;
    }

    public async Task<Invoice> CreateRevisionAsync(
        long invoiceId,
        long recipientId,
        IReadOnlyList<InvoiceLineInput> lineInputs,
        CancellationToken ct
    )
    {
        var edited =
            await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        // A grave invoice is one recipient's share of one grave bill: revising it
        // alone would leave the other recipients disagreeing with it. Grave
        // invoices must go through CreateGraveInvoiceRevision, which revises every
        // recipient of the grave and half-year at once.
        if (edited.GraveId is not null)
        {
            throw new ValidationException(
                "GraveInvoiceRevisionRequired",
                "A grave invoice must be revised for all of its recipients at once."
            );
        }

        // A paid invoice is settled; revising it would move the ground under a
        // payment that already happened. The chain is what matters, not the row
        // that was passed: only its latest revision can carry a mark, and an
        // older revision may well be the one handed in.
        if (await _db.Invoices.AnyAsync(i => i.RootId == edited.RootId && i.PaidOn != null, ct))
        {
            throw new ValidationException(
                "InvoicePaid",
                "The invoice is paid and cannot be revised."
            );
        }

        var recipient =
            await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

        var lines = await _lines.BuildAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        RequireAddress(recipient);

        // The revision number continues the whole chain, not just the row that was
        // edited: editing an older revision (or the same one twice) must still
        // produce the next free number instead of colliding on (Year, Sequence,
        // RevisionNumber).
        var lastRevision =
            await _db
                .Invoices.Where(i => i.RootId == edited.RootId)
                .MaxAsync(i => (int?)i.RevisionNumber, ct)
            ?? edited.RevisionNumber;
        var revisionNumber = lastRevision + 1;

        var now = _clock.GetLocalNow();
        var baseNumber = InvoiceNumberFormatter.StripRevision(edited.InvoiceNumber);

        var revision = new Invoice
        {
            RootId = edited.RootId,
            RevisionNumber = revisionNumber,
            Year = edited.Year,
            Sequence = edited.Sequence,
            InvoiceNumber = InvoiceNumberFormatter.ForRevision(baseNumber, revisionNumber),
            GraveId = edited.GraveId,
            PeriodYear = edited.PeriodYear,
            PeriodHalf = edited.PeriodHalf,
            SharePercent = edited.SharePercent,
            // a revision is still the invoice the quote became
            QuoteId = edited.QuoteId,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            RecipientNumber = recipient.Number,
            // not a grave revision (those go through GraveInvoiceRevisionService),
            // so whatever grave values the edited invoice had stay as they were
            GraveNumber = edited.GraveNumber,
            GraveSiteName = edited.GraveSiteName,
            GraveFieldNumber = edited.GraveFieldNumber,
            GravePosition = edited.GravePosition,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            Lines = lines,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // the chain has exactly one latest revision, and it is now this one
        var superseded = await _db
            .Invoices.Where(i => i.RootId == edited.RootId && i.IsLatestRevision)
            .ToListAsync(ct);
        foreach (var invoice in superseded)
        {
            invoice.IsLatestRevision = false;
        }

        _db.Invoices.Add(revision);
        await _db.SaveChangesAsync(ct);

        revision.Documents = _documentFactory.Build(revision, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return revision;
    }

    /// <summary>Marks an invoice paid on the current local date — "today" here is
    /// the user's day, not UTC's — or takes the mark back. Marking an invoice
    /// that already carries a date keeps that date, so a double click cannot
    /// move a recorded payment day.</summary>
    public async Task<Invoice> SetPaidAsync(long invoiceId, bool paid, CancellationToken ct)
    {
        var invoice =
            await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        // Lists show latest revisions only. A superseded row can only be reached
        // deliberately, and a mark on it would sit where nothing displays it.
        if (!invoice.IsLatestRevision)
        {
            throw new ValidationException(
                "NotLatestRevision",
                "Only the latest revision of an invoice can be marked as paid."
            );
        }

        if (paid)
        {
            invoice.PaidOn ??= DateOnly.FromDateTime(_clock.GetLocalNow());
        }
        else
        {
            invoice.PaidOn = null;
        }

        await _db.SaveChangesAsync(ct);
        return invoice;
    }

    /// <summary>Marks several invoices paid at once, all or none: an unknown id
    /// or a superseded revision among them refuses the whole set before anything
    /// is written. Like <see cref="SetPaidAsync"/>, an invoice that already
    /// carries a day keeps it.</summary>
    public async Task MarkPaidAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken ct)
    {
        var invoices = await _db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct);
        if (invoices.Count != invoiceIds.Distinct().Count())
        {
            throw new ValidationException("InvoiceNotFound", "An invoice does not exist.");
        }

        if (invoices.Any(i => !i.IsLatestRevision))
        {
            throw new ValidationException(
                "NotLatestRevision",
                "Only the latest revision of an invoice can be marked as paid."
            );
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow());
        foreach (var invoice in invoices)
        {
            invoice.PaidOn ??= today;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void RequireAddress(Recipient recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException(
                "RecipientAddressRequired",
                "The recipient address is required."
            );
        }
    }

    /// <summary>Inserts a new original invoice with the next number and its
    /// PDFs. The caller opens the transaction, before reading anything the
    /// invoice depends on, and commits it: the number read here is only safe
    /// while that transaction holds SQLite's write lock.</summary>
    private async Task<Invoice> PersistAsync(
        Recipient recipient,
        GlobalSettings settings,
        List<InvoiceLine> lines,
        long? quoteId,
        CancellationToken ct
    )
    {
        var (number, year, sequence) = await _numbers.AllocateAsync(ct);
        // the wall-clock here, like the number's year: the day it names is the
        // issue date the document prints, and until one or two in the morning
        // the UTC day is still yesterday
        var now = _clock.GetLocalNow();

        var invoice = new Invoice
        {
            RevisionNumber = 0,
            Year = year,
            Sequence = sequence,
            InvoiceNumber = number,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            RecipientNumber = recipient.Number,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            QuoteId = quoteId,
            Lines = lines,
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        invoice.RootId = invoice.Id;

        invoice.Documents = _documentFactory.Build(invoice, now);

        await _db.SaveChangesAsync(ct);
        return invoice;
    }
}
