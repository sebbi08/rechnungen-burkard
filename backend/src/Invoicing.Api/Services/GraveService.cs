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

public sealed record GraveFields(
    string GraveNumber,
    string FieldNumber,
    string SiteName,
    GravePosition? Position,
    DateOnly StartDate,
    DateOnly? EndDate
);

public sealed class GraveService
{
    private readonly AppDbContext _db;

    public GraveService(AppDbContext db) => _db = db;

    /// <summary>Creates or updates a grave together with its full set of item and
    /// recipient links in a single transaction, so the dialog can collect
    /// everything before anything is persisted.</summary>
    public async Task<Grave> SaveAsync(
        long? graveId,
        GraveFields fields,
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
        CancellationToken ct
    )
    {
        await ValidateFieldsAsync(graveId, fields, ct);
        await ValidateAsync(items, recipientIds, ct);

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

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var grave = graveId is { } id ? await LoadForReplaceAsync(id, ct) : NewGrave();
        grave.GraveNumber = fields.GraveNumber.Trim();
        grave.FieldNumber = fields.FieldNumber.Trim();
        grave.SiteName = fields.SiteName.Trim();
        grave.Position = fields.Position;
        grave.StartDate = fields.StartDate;
        grave.EndDate = fields.EndDate;

        // the deletes must reach the database before the new links are inserted,
        // or the unique (GraveId, ItemId) index rejects a re-added link
        await _db.SaveChangesAsync(ct);

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

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return grave;
    }

    private Grave NewGrave()
    {
        var grave = new Grave { CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow) };
        _db.Graves.Add(grave);
        return grave;
    }

    private async Task<Grave> LoadForReplaceAsync(long graveId, CancellationToken ct)
    {
        var grave =
            await _db
                .Graves.Include(g => g.Items)
                .Include(g => g.Recipients)
                .SingleOrDefaultAsync(g => g.Id == graveId, ct)
            ?? throw new ValidationException("GraveNotFound", "The grave does not exist.");

        // the dialog always sends the complete set of links, so replace wholesale
        _db.GraveItems.RemoveRange(grave.Items);
        _db.GraveRecipients.RemoveRange(grave.Recipients);
        grave.Items.Clear();
        grave.Recipients.Clear();
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

        if (fields.Position is null)
        {
            throw new ValidationException("PositionRequired", "The grave position is required.");
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

    private async Task ValidateAsync(
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
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

        var known = await _db.Recipients.Where(r => recipientIds.Contains(r.Id)).CountAsync(ct);
        if (known != recipientIds.Count)
        {
            throw new ValidationException("RecipientNotFound", "The recipient does not exist.");
        }

        // The bill is split equally, so it has to divide. Nothing is adjusted to
        // make it: a grave that does not come out even is refused here, while
        // the dialog is still open and a price can be changed, rather than
        // producing invoices that disagree with each other or with the grave.
        //
        // Only checked once there are recipients -- until then there is nothing
        // to divide by, and a grave is often saved with its items before its
        // recipients are known.
        if (recipientIds.Count > 0 && items.Count > 0)
        {
            var lines = items
                .Select(input =>
                    GraveBilling.LineFor(
                        invoiceItems[input.ItemId],
                        input.Count,
                        input.UnitPrice,
                        input.PriceIncludesVat
                    )
                )
                .ToList();

            var problems = GraveShare.Problems(lines, recipientIds.Count);
            if (problems.Count > 0)
            {
                throw new GraveShareValidationException(problems, recipientIds.Count);
            }
        }
    }
}

/// <summary>A grave whose prices cannot be split evenly across its recipients.
/// Carries the problems so the dialog can name the rate, the amounts and what
/// would work instead, rather than only saying no.</summary>
public sealed class GraveShareValidationException : ValidationException
{
    public GraveShareValidationException(IReadOnlyList<GraveShareProblem> problems, int recipients)
        : base("PricesDoNotDivide", "The prices cannot be split evenly across the recipients.")
    {
        Problems = problems;
        Recipients = recipients;
    }

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
