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
    private readonly InvoiceDocumentFactory _documentFactory;
    private readonly IClock _clock;

    public InvoiceService(
        AppDbContext db,
        InvoiceNumberService numbers,
        InvoiceDocumentFactory documentFactory,
        IClock clock
    )
    {
        _db = db;
        _numbers = numbers;
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

        var lines = await BuildLinesAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(recipient, settings, lines, ct);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // unique (Year, Sequence) race — drop the failed attempt's tracked
                // inserts (they stay Added otherwise and poison every retry),
                // then retry with a fresh number
                _db.ChangeTracker.Clear();
            }
        }
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

        var lines = await BuildLinesAsync(lineInputs, ct);
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

        var now = DateTime.UtcNow;
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

    private async Task<List<InvoiceLine>> BuildLinesAsync(
        IReadOnlyList<InvoiceLineInput> inputs,
        CancellationToken ct
    )
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db
            .InvoiceItems.Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var result = new List<InvoiceLine>(inputs.Count);
        foreach (var (input, index) in inputs.Select((l, i) => (l, i)))
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException(
                    $"ItemNotFound:{input.ItemId}",
                    $"The item {input.ItemId} does not exist."
                );
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice = input.UnitPrice ?? item.DefaultPrice;
            if (unitPrice is null)
            {
                throw new ValidationException(
                    "PriceRequired",
                    $"A price is required for item '{item.Number}'."
                );
            }

            // the document prints the price to the cent and multiplies the
            // unrounded one, so 3 x 10,125 showed 10,13 and a total of 30,38
            if (input.UnitPrice is { } typed && !GraveBilling.IsWholeCents(typed))
            {
                throw new ValidationException(
                    "InvalidPrice",
                    $"The price for item '{item.Number}' has more than two decimals."
                );
            }

            result.Add(
                new InvoiceLine
                {
                    ItemId = item.Id,
                    ItemNumber = item.Number,
                    Description = item.Description,
                    Count = input.Count,
                    // a line priced VAT-included is entered as the gross the
                    // customer pays; the line itself is always net
                    UnitPrice = VatInclusivePrice.NetOf(
                        unitPrice.Value,
                        item.VatRate,
                        input.PriceIncludesVat
                    ),
                    VatRate = item.VatRate,
                    PriceIncludesVat = input.PriceIncludesVat,
                    LineNumber = index + 1,
                }
            );
        }

        return result;
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

        var (number, year, sequence) = await _numbers.AllocateAsync(ct);
        var now = DateTime.UtcNow;

        var invoice = new Invoice
        {
            RevisionNumber = 0,
            Year = year,
            Sequence = sequence,
            InvoiceNumber = number,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            Lines = lines,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        invoice.RootId = invoice.Id;

        invoice.Documents = _documentFactory.Build(invoice, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice;
    }
}
