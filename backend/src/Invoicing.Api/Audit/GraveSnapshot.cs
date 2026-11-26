using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>A grave as its Verlauf compares it: its fields, and its items,
/// recipients and rounding corrections with the labels a person reads, taken
/// when the snapshot is.</summary>
public sealed record GraveSnapshot(
    string GraveNumber,
    string FieldNumber,
    string SiteName,
    string CemeteryName,
    GravePosition? Position,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCooperative,
    string? ContractNumber,
    IReadOnlyList<GraveItemSnapshot> Items,
    IReadOnlyList<GraveRecipientSnapshot> Recipients,
    IReadOnlyList<GraveCorrectionSnapshot> Corrections
);

/// <summary>An item link: <see cref="Label"/> is "1001 Grabpflege", and
/// <see cref="EffectivePrice"/> the own price or else the item's default.</summary>
public sealed record GraveItemSnapshot(
    long ItemId,
    string Label,
    int Count,
    decimal? OwnPrice,
    decimal? EffectivePrice
);

/// <summary>A recipient link: <see cref="Label"/> is "E-0107 Carla Muster", or
/// the name alone when there is no number.</summary>
public sealed record GraveRecipientSnapshot(long RecipientId, string Label);

/// <summary>A rounding correction; <see cref="Kind"/> is the
/// <see cref="BillingExampleKind"/>'s name.</summary>
public sealed record GraveCorrectionSnapshot(
    string Kind,
    int? Year,
    int Half,
    decimal VatRate,
    long ItemId,
    string ItemLabel,
    decimal CorrectedPrice
);

public static class GraveSnapshots
{
    /// <summary>The snapshots of the graves that exist among
    /// <paramref name="graveIds"/>, read untracked, one query per table.</summary>
    public static async Task<Dictionary<long, GraveSnapshot>> LoadAsync(
        AppDbContext db,
        IReadOnlyCollection<long> graveIds,
        CancellationToken ct
    )
    {
        if (graveIds.Count == 0)
        {
            return new Dictionary<long, GraveSnapshot>();
        }

        var ids = graveIds.Distinct().ToList();
        var graves = await db
            .Graves.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .Select(g => new
            {
                g.Id,
                g.GraveNumber,
                g.FieldNumber,
                g.SiteName,
                CemeteryName = g.Cemetery.Name,
                g.Position,
                g.StartDate,
                g.EndDate,
                g.IsCooperative,
                g.ContractNumber,
            })
            .ToListAsync(ct);
        var items = await db
            .GraveItems.AsNoTracking()
            .Where(i => ids.Contains(i.GraveId))
            .Select(i => new
            {
                i.GraveId,
                i.ItemId,
                i.Item.Number,
                i.Item.Description,
                i.Count,
                i.UnitPrice,
                i.Item.DefaultPrice,
            })
            .ToListAsync(ct);
        var recipients = await db
            .GraveRecipients.AsNoTracking()
            .Where(r => ids.Contains(r.GraveId))
            .Select(r => new
            {
                r.GraveId,
                r.RecipientId,
                r.Recipient.Number,
                r.Recipient.Name,
            })
            .ToListAsync(ct);
        var corrections = await db
            .GraveBillingCorrections.AsNoTracking()
            .Where(c => ids.Contains(c.GraveId))
            .Select(c => new
            {
                c.GraveId,
                c.Kind,
                c.Year,
                c.Half,
                c.VatRate,
                c.ItemId,
                c.Item.Number,
                c.Item.Description,
                c.CorrectedPrice,
            })
            .ToListAsync(ct);

        // ordered in memory: decimals are text in SQLite, and the order only has
        // to be the same every time
        var itemsByGrave = items
            .Select(i =>
                (
                    i.GraveId,
                    Snapshot: new GraveItemSnapshot(
                        i.ItemId,
                        ItemLabel(i.Number, i.Description),
                        i.Count,
                        i.UnitPrice,
                        i.UnitPrice ?? i.DefaultPrice
                    )
                )
            )
            .OrderBy(i => i.Snapshot.Label, StringComparer.Ordinal)
            .ThenBy(i => i.Snapshot.ItemId)
            .ToLookup(i => i.GraveId, i => i.Snapshot);
        var recipientsByGrave = recipients
            .Select(r =>
                (
                    r.GraveId,
                    Snapshot: new GraveRecipientSnapshot(
                        r.RecipientId,
                        string.IsNullOrWhiteSpace(r.Number) ? r.Name : $"{r.Number} {r.Name}"
                    )
                )
            )
            .OrderBy(r => r.Snapshot.Label, StringComparer.Ordinal)
            .ThenBy(r => r.Snapshot.RecipientId)
            .ToLookup(r => r.GraveId, r => r.Snapshot);
        var correctionsByGrave = corrections
            .Select(c =>
                (
                    c.GraveId,
                    Snapshot: new GraveCorrectionSnapshot(
                        c.Kind.ToString(),
                        c.Year,
                        c.Half,
                        c.VatRate,
                        c.ItemId,
                        ItemLabel(c.Number, c.Description),
                        c.CorrectedPrice
                    )
                )
            )
            .OrderBy(c => c.Snapshot.Year ?? 0)
            .ThenBy(c => c.Snapshot.Half)
            .ThenBy(c => c.Snapshot.Kind, StringComparer.Ordinal)
            .ThenBy(c => c.Snapshot.VatRate)
            .ThenBy(c => c.Snapshot.ItemId)
            .ToLookup(c => c.GraveId, c => c.Snapshot);

        return graves.ToDictionary(
            g => g.Id,
            g => new GraveSnapshot(
                g.GraveNumber,
                g.FieldNumber,
                g.SiteName,
                g.CemeteryName,
                g.Position,
                g.StartDate,
                g.EndDate,
                g.IsCooperative,
                g.ContractNumber,
                itemsByGrave[g.Id].ToList(),
                recipientsByGrave[g.Id].ToList(),
                correctionsByGrave[g.Id].ToList()
            )
        );
    }

    private static string ItemLabel(string number, string description) =>
        string.IsNullOrWhiteSpace(number) ? description : $"{number} {description}";
}
