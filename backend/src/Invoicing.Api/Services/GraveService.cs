using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class GraveItemInput
{
    public long ItemId { get; set; }
    public int Count { get; set; }
    public decimal? UnitPrice { get; set; }
}

public sealed class GraveService
{
    private readonly AppDbContext _db;

    public GraveService(AppDbContext db) => _db = db;

    /// <summary>Creates or updates a grave together with its full set of item and
    /// recipient links in a single transaction, so the dialog can collect
    /// everything before anything is persisted.</summary>
    public async Task<Grave> SaveAsync(
        long? graveId,
        string graveNumber,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
        CancellationToken ct
    )
    {
        await ValidateAsync(graveNumber, items, recipientIds, ct);

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
        grave.GraveNumber = graveNumber;
        grave.StartDate = startDate;
        grave.EndDate = endDate;

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

    private async Task ValidateAsync(
        string graveNumber,
        IReadOnlyList<GraveItemInput> items,
        IReadOnlyList<long> recipientIds,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(graveNumber))
        {
            throw new ValidationException("GraveNumberRequired", "The grave number is required.");
        }

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
        }

        var known = await _db.Recipients.Where(r => recipientIds.Contains(r.Id)).CountAsync(ct);
        if (known != recipientIds.Count)
        {
            throw new ValidationException("RecipientNotFound", "The recipient does not exist.");
        }
    }
}
