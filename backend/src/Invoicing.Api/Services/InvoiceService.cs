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

        return await PersistAsync(recipient, settings, lines, ct);
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

        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException(
                "RecipientAddressRequired",
                "The recipient address is required."
            );
        }

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

    private async Task<Invoice> PersistAsync(
        Recipient recipient,
        GlobalSettings settings,
        List<InvoiceLine> lines,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException(
                "RecipientAddressRequired",
                "The recipient address is required."
            );
        }

        // SQLite opens the transaction with BEGIN IMMEDIATE, so it holds the
        // write lock from before the highest number is read until the invoice is
        // in: creates at once take turns rather than read the same number. (They
        // used to read it first and retry on the unique index, and a request that
        // lost the race three times in a row came back as a 500.)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
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
            Lines = lines,
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        invoice.RootId = invoice.Id;

        invoice.Documents = _documentFactory.Build(invoice, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice;
    }
}
