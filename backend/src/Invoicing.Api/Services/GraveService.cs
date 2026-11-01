using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class GraveItemInput
{
    public long ItemId { get; set; }
    public int Count { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool PriceIncludesVat { get; set; }
}

/// <summary>One correction the editor showed and the user confirmed. The kind
/// travels as its name; one that is not a kind matches nothing.</summary>
public sealed class GraveCorrectionInput
{
    public string Kind { get; set; } = "";
    public int? Year { get; set; }
    public int Half { get; set; }
    public decimal VatRate { get; set; }
    public long ItemId { get; set; }
    public decimal CorrectedPrice { get; set; }
}

public sealed record GraveFields(
    string GraveNumber,
    string FieldNumber,
    string SiteName,
    long? CemeteryId,
    GravePosition? Position,
    DateOnly StartDate,
    DateOnly? EndDate
);

public sealed class GraveService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public GraveService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Creates or updates a grave together with its full set of item and
    /// recipient links in a single transaction, so the dialog can collect
    /// everything before anything is persisted.</summary>
    public async Task<Grave> SaveAsync(
        long? graveId,
        GraveFields fields,
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
        IReadOnlyList<GraveCorrectionInput> corrections,
        CancellationToken ct
    )
    {
        await ValidateFieldsAsync(graveId, fields, ct);
        var evaluation = await ValidateAsync(items, recipientIds, fields, ct);

        // These come first: without recipients nothing is evaluated, so the
        // corrections check below would fail on every retry and hide this.
        // A grave created without recipients could never be billed: generation
        // would skip it, and once created its recipient set cannot change. A
        // grave that predates this rule keeps its empty set and stays editable.
        if (graveId is null && recipientIds.Count == 0)
        {
            throw new ValidationException(
                "NoRecipients",
                "The grave needs at least one recipient."
            );
        }

        // A grave's recipients are fixed at creation: the even split across them is
        // baked into every invoice already generated, and changing the set would
        // make a later revision disagree with what was sent out.
        if (graveId is { } existingId)
        {
            var stored = await _db
                .GraveRecipients.Where(r => r.GraveId == existingId)
                .Select(r => r.RecipientId)
                .ToListAsync(ct);
            if (!stored.OrderBy(id => id).SequenceEqual(recipientIds.OrderBy(id => id)))
            {
                throw new ValidationException(
                    "GraveRecipientsImmutable",
                    "A grave's recipients cannot be changed."
                );
            }
        }

        // corrections are a promise the user confirmed: a price the editor did
        // not show is never stored
        var confirmed = corrections.Select(c => new CorrectionKey(
            Enum.TryParse<BillingExampleKind>(c.Kind, out var kind) && Enum.IsDefined(kind)
                ? kind
                : (BillingExampleKind)(-1),
            c.Year,
            c.Half,
            c.VatRate,
            c.ItemId,
            c.CorrectedPrice
        ));
        var computed = evaluation?.CorrectionKeys ?? [];
        if (!GraveBillingCheck.SameCorrections(computed, confirmed))
        {
            throw new ValidationException(
                "CorrectionsChanged",
                "The rounding corrections differ from the ones confirmed."
            );
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var grave = graveId is { } id ? await LoadForReplaceAsync(id, ct) : NewGrave();
        grave.GraveNumber = fields.GraveNumber.Trim();
        grave.FieldNumber = fields.FieldNumber.Trim();
        grave.SiteName = fields.SiteName.Trim();
        grave.CemeteryId = fields.CemeteryId!.Value;
        grave.Position = fields.Position;
        grave.StartDate = fields.StartDate;
        grave.EndDate = fields.EndDate;

        // the deletes must reach the database before the new links and corrections
        // are inserted, or the unique indexes reject a re-added row
        await _db.SaveChangesAsync(ct);

        // Inserted in the order the editor sent them, and read back by id
        // (GraveBilling.LinksByGraveAsync, GraveInvoiceGenerator): that order
        // decides which line a correction lands on, so the corrections stored
        // below are only billed as shown while ids follow the insert order.
        foreach (var input in items)
        {
            grave.Items.Add(
                new GraveItem
                {
                    GraveId = grave.Id,
                    ItemId = input.ItemId,
                    Count = input.Count,
                    UnitPrice = input.UnitPrice,
                    PriceIncludesVat = input.PriceIncludesVat,
                }
            );
        }

        foreach (var recipientId in recipientIds)
        {
            grave.Recipients.Add(
                new GraveRecipient { GraveId = grave.Id, RecipientId = recipientId }
            );
        }

        foreach (var invoice in evaluation?.Invoices ?? [])
        {
            foreach (var correction in invoice.Corrections)
            {
                grave.Corrections.Add(
                    new GraveBillingCorrection
                    {
                        GraveId = grave.Id,
                        Kind = invoice.Example.Kind,
                        Year = invoice.Example.Period.Year,
                        Half = invoice.Example.Period.Half,
                        VatRate = correction.VatRate,
                        ItemId = correction.ItemId,
                        EnteredPrice = correction.EnteredPrice,
                        CorrectedPrice = correction.CorrectedPrice,
                    }
                );
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return grave;
    }

    private Grave NewGrave()
    {
        var grave = new Grave { CreatedAt = DateOnly.FromDateTime(_clock.GetLocalNow()) };
        _db.Graves.Add(grave);
        return grave;
    }

    private async Task<Grave> LoadForReplaceAsync(long graveId, CancellationToken ct)
    {
        var grave =
            await _db
                .Graves.Include(g => g.Items)
                .Include(g => g.Recipients)
                .Include(g => g.Corrections)
                .SingleOrDefaultAsync(g => g.Id == graveId, ct)
            ?? throw new ValidationException("GraveNotFound", "The grave does not exist.");

        // the dialog always sends the complete set of links, so replace wholesale
        _db.GraveItems.RemoveRange(grave.Items);
        _db.GraveRecipients.RemoveRange(grave.Recipients);
        _db.GraveBillingCorrections.RemoveRange(grave.Corrections);
        grave.Items.Clear();
        grave.Recipients.Clear();
        grave.Corrections.Clear();
        return grave;
    }

    private async Task ValidateFieldsAsync(long? graveId, GraveFields fields, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fields.GraveNumber))
        {
            throw new ValidationException("GraveNumberRequired", "The grave number is required.");
        }

        if (string.IsNullOrWhiteSpace(fields.FieldNumber))
        {
            throw new ValidationException(
                "FieldNumberRequired",
                "The grave field number is required."
            );
        }

        if (string.IsNullOrWhiteSpace(fields.SiteName))
        {
            throw new ValidationException("SiteNameRequired", "The grave site name is required.");
        }

        if (fields.CemeteryId is not { } cemeteryId)
        {
            throw new ValidationException("CemeteryRequired", "The cemetery is required.");
        }

        if (!await _db.Cemeteries.AnyAsync(c => c.Id == cemeteryId, ct))
        {
            throw new ValidationException("CemeteryNotFound", "The cemetery does not exist.");
        }

        if (fields.Position is null)
        {
            throw new ValidationException("PositionRequired", "The grave position is required.");
        }

        // The positions beyond the editor's six come from the old program's
        // data, through the import. A grave keeps the one it has, so it can be
        // saved unchanged, but none of them is chosen anew.
        if (!GravePositions.Offered.Contains(fields.Position.Value))
        {
            var stored = graveId is { } id
                ? await _db
                    .Graves.Where(g => g.Id == id)
                    .Select(g => g.Position)
                    .FirstOrDefaultAsync(ct)
                : null;
            if (stored != fields.Position)
            {
                throw new ValidationException(
                    "PositionNotOffered",
                    "This grave position can only be kept, not chosen."
                );
            }
        }

        // the import refuses the same (DateRangeInvalid)
        if (fields.EndDate < fields.StartDate)
        {
            throw new ValidationException(
                "DateRangeInvalid",
                "The end month must not be before the start month."
            );
        }

        // A number may come back once its grave has ended, but two graves that
        // run at the same time cannot share it. Compared in memory, like
        // SearchGraves: SQLite's lower() folds ASCII only.
        var number = fields.GraveNumber.Trim();
        var others = await _db
            .Graves.Where(g => graveId == null || g.Id != graveId)
            .Select(g => new
            {
                g.GraveNumber,
                g.StartDate,
                g.EndDate,
            })
            .ToListAsync(ct);
        var clash = others.FirstOrDefault(g =>
            string.Equals(g.GraveNumber.Trim(), number, StringComparison.OrdinalIgnoreCase)
            && GraveTerm.Overlap(g.StartDate, g.EndDate, fields.StartDate, fields.EndDate)
        );
        if (clash is not null)
        {
            throw new GraveNumberInUseException(clash.GraveNumber, clash.StartDate, clash.EndDate);
        }
    }

    private async Task<GraveBillingEvaluation?> ValidateAsync(
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
        GraveFields fields,
        CancellationToken ct
    )
    {
        if (items.Select(i => i.ItemId).Distinct().Count() != items.Count)
        {
            throw new ValidationException(
                "GraveItemLinkExists",
                "This item is already linked to the grave."
            );
        }

        if (recipientIds.Distinct().Count() != recipientIds.Count)
        {
            throw new ValidationException(
                "GraveRecipientLinkExists",
                "This recipient is already linked to the grave."
            );
        }

        if (recipientIds.Count > GraveBilling.MaxRecipients)
        {
            throw new ValidationException(
                "TooManyRecipients",
                $"A grave can have at most {GraveBilling.MaxRecipients} recipients."
            );
        }

        var itemIds = items.Select(i => i.ItemId).ToArray();
        var invoiceItems = await _db
            .InvoiceItems.Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        foreach (var input in items)
        {
            if (!invoiceItems.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException(
                    "ItemNotFound",
                    $"The item {input.ItemId} does not exist."
                );
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            // an item with neither a link price nor a default price would make the
            // whole grave get skipped during generation, so reject it while the
            // dialog is still open
            if ((input.UnitPrice ?? item.DefaultPrice) is null)
            {
                throw new ValidationException(
                    "PriceRequired",
                    $"A price is required for item '{item.Number}'."
                );
            }

            // prices are cents: a third decimal would pass the split check
            // rounded and be billed unrounded
            if (input.UnitPrice is { } unitPrice && !GraveBilling.IsWholeCents(unitPrice))
            {
                throw new ValidationException(
                    "InvalidPrice",
                    $"The price for item '{item.Number}' has more than two decimals."
                );
            }
        }

        // with one all-year item every half-year of the term bills something, so a
        // grave is always due while its term runs
        if (!items.Any(i => invoiceItems[i.ItemId].Quarter is null))
        {
            throw new ValidationException(
                "AllYearItemRequired",
                "A grave needs at least one item billed all year."
            );
        }

        var known = await _db.Recipients.Where(r => recipientIds.Contains(r.Id)).CountAsync(ct);
        if (known != recipientIds.Count)
        {
            throw new ValidationException("RecipientNotFound", "The recipient does not exist.");
        }

        // The bill is split equally, so it has to divide. Where a block does not,
        // a line per VAT rate is raised by a few cents (see GraveBillingCheck);
        // the corrections are returned for the save to compare with what the
        // user confirmed and to store. A grave no correction can fix is refused
        // here, while the dialog is still open and a price can be changed.
        //
        // Only checked once there are recipients -- until then there is nothing
        // to divide by, and a grave is often saved with its items before its
        // recipients are known.
        if (recipientIds.Count > 0 && items.Count > 0)
        {
            var links = items
                .Select(input =>
                    GraveBilling.LinkFor(
                        invoiceItems[input.ItemId],
                        input.Count,
                        input.UnitPrice,
                        input.PriceIncludesVat
                    )
                )
                .ToList();

            // every invoice the term produces has to divide on its own: the
            // first and last half-year bill other amounts than the full ones
            var evaluation = GraveBillingCheck.Evaluate(
                links,
                fields.StartDate,
                fields.EndDate,
                recipientIds.Count,
                HalfYearBilling.HalfYearOf(_clock.GetLocalNow())
            );
            if (evaluation.FirstFailing is { } failing)
            {
                throw new GraveShareValidationException(
                    failing.Problems,
                    recipientIds.Count,
                    HalfYearBilling.Label(failing.Example)
                );
            }

            return evaluation;
        }

        return null;
    }
}

/// <summary>A grave whose prices cannot be split evenly across its recipients.
/// Carries the problems so the dialog can name the rate, the amounts and what
/// would work instead, rather than only saying no.</summary>
public sealed class GraveShareValidationException : ValidationException
{
    public GraveShareValidationException(
        IReadOnlyList<GraveShareProblem> problems,
        int recipients,
        string? invoice = null
    )
        : base(
            "PricesDoNotDivide",
            invoice is null
                ? "The prices cannot be split evenly across the recipients."
                : $"The invoice '{invoice}' cannot be split evenly across the recipients."
        )
    {
        Problems = problems;
        Recipients = recipients;
        Invoice = invoice;
    }

    /// <summary>The label of the invoice that does not divide ("Anfang H1
    /// 2026"); null where only one invoice is in question, as in a revision.</summary>
    public string? Invoice { get; }

    public IReadOnlyList<GraveShareProblem> Problems { get; }
    public int Recipients { get; }
}

/// <summary>The grave number belongs to another grave whose term overlaps.
/// Carries that grave's number and term so the message can name them.</summary>
public sealed class GraveNumberInUseException : ValidationException
{
    public GraveNumberInUseException(string number, DateOnly start, DateOnly? end)
        : base(
            "GraveNumberInUse",
            end is { } until
                ? $"The grave number '{number}' is in use from {GraveTerm.FormatMonth(start)} to {GraveTerm.FormatMonth(until)}."
                : $"The grave number '{number}' is in use from {GraveTerm.FormatMonth(start)}."
        )
    {
        // two arguments for an open-ended grave, three otherwise: the UI picks
        // its text by the count
        Args = end is { } last
            ? [number, GraveTerm.FormatMonth(start), GraveTerm.FormatMonth(last)]
            : [number, GraveTerm.FormatMonth(start)];
    }

    public IReadOnlyList<string> Args { get; }
}
