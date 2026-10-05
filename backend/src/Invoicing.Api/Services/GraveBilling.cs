using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Turns a grave's item links into what the half-year rule bills.
///
/// One place, because three callers have to agree exactly: the save that
/// refuses a grave whose prices do not divide, the dialog's check that says so
/// before the save is attempted, and the generation that bills them. Any
/// disagreement between those would show up as a grave that saves and then
/// cannot be invoiced.</summary>
public static class GraveBilling
{
    /// <summary>How many recipients a grave may split its bill across.</summary>
    public const int MaxRecipients = 4;

    /// <summary>A link as the half-year rule sees it: the link's own price, or
    /// the item's default standing in for it, rounded to the cent like
    /// <see cref="LineFor"/>.</summary>
    public static BillableLink LinkFor(
        InvoiceItem item,
        int count,
        decimal? unitPrice,
        bool priceIncludesVat
    ) =>
        new(
            item.Id,
            item.Number,
            item.Description,
            item.VatRate,
            item.Quarter,
            count,
            decimal.Round(unitPrice ?? item.DefaultPrice ?? 0m, 2, MidpointRounding.AwayFromZero),
            priceIncludesVat
        );

    /// <summary>Every grave's item links as the half-year rule sees them, in
    /// one query, keyed by grave id. A grave without items has no entry.</summary>
    public static async Task<Dictionary<long, IReadOnlyList<BillableLink>>> LinksByGraveAsync(
        AppDbContext db,
        CancellationToken ct = default
    )
    {
        // ordered by link id: the correction rule picks its target line by link
        // order, and the save and the generator both see the links in id order,
        // so the precheck must too or it could name another line than was stored
        var rows = await db
            .GraveItems.OrderBy(l => l.Id)
            .Select(l => new
            {
                l.GraveId,
                l.Item,
                l.Count,
                l.UnitPrice,
                l.PriceIncludesVat,
            })
            .ToListAsync(ct);
        return rows.GroupBy(l => l.GraveId)
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<BillableLink>)
                        g.Select(l => LinkFor(l.Item, l.Count, l.UnitPrice, l.PriceIncludesVat))
                            .ToList()
            );
    }

    /// <summary>Whether a grave is due an invoice for a half-year: its term
    /// reaches the half-year and its links bill at least one line in it. The
    /// overview and the generation job both ask here, so a grave is never
    /// listed as open that a run would then have nothing to bill for.</summary>
    public static bool IsDue(
        DateOnly start,
        DateOnly? end,
        IReadOnlyList<BillableLink> links,
        int year,
        int half
    ) =>
        HalfYearBilling.PeriodIn(start, end, year, half) is { } period
        && HalfYearBilling.Lines(links, period).Count > 0;

    /// <summary>Whether a price is a whole number of cents. Prices are entered,
    /// stored and billed in cents; a third decimal would be judged rounded here
    /// and billed unrounded by generation, so every write refuses one.</summary>
    public static bool IsWholeCents(decimal price) =>
        price == decimal.Round(price, 2, MidpointRounding.AwayFromZero);

    /// <summary>The billed line for one item link. The link's own price wins;
    /// without one the item's default is billed.</summary>
    public static GraveLine LineFor(
        InvoiceItem item,
        int count,
        decimal? unitPrice,
        bool priceIncludesVat
    )
    {
        var entered = decimal.Round(
            unitPrice ?? item.DefaultPrice ?? 0m,
            2,
            MidpointRounding.AwayFromZero
        );

        return new GraveLine(
            item.Id,
            item.Number,
            item.Description,
            count,
            VatInclusivePrice.NetOf(entered, item.VatRate, priceIncludesVat),
            item.VatRate,
            priceIncludesVat,
            // a price entered as a gross is a promise about what the customer
            // pays, and the split has to come to exactly that
            priceIncludesVat ? entered : null
        );
    }
}
