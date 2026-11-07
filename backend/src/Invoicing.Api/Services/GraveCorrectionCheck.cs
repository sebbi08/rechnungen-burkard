using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Whether the corrections stored with the graves still match the
/// rule. A run bills stored corrections, so one that the rule would now compute
/// differently must be looked at and saved again before anything is invoiced.</summary>
public sealed class GraveCorrectionCheck
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public GraveCorrectionCheck(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The numbers of the graves due in the half-year, not yet invoiced
    /// for it, whose stored corrections differ from the computed ones.</summary>
    public async Task<IReadOnlyList<string>> OutdatedGraveNumbersAsync(
        int year,
        int half,
        CancellationToken ct
    )
    {
        var invoiced = await _db
            .GraveInvoiceRuns.Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToListAsync(ct);
        var graves = await _db.Graves.AsNoTracking().ToListAsync(ct);
        var links = await GraveBilling.LinksByGraveAsync(_db, ct);
        var recipients = await _db
            .GraveRecipients.GroupBy(r => r.GraveId)
            .Select(g => new { GraveId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GraveId, x => x.Count, ct);
        var stored = (await _db.GraveBillingCorrections.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.GraveId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var current = HalfYearBilling.HalfYearOf(_clock.GetLocalNow());
        var outdated = new List<string>();
        foreach (var grave in graves)
        {
            var graveLinks = links.GetValueOrDefault(grave.Id) ?? [];
            if (
                grave.IsCooperative
                || invoiced.Contains(grave.Id)
                || !recipients.TryGetValue(grave.Id, out var count)
                || !GraveBilling.IsDue(grave.StartDate, grave.EndDate, graveLinks, year, half)
            )
            {
                continue;
            }

            var computed = GraveBillingCheck
                .Evaluate(graveLinks, grave.StartDate, grave.EndDate, count, current)
                .CorrectionKeys;
            var saved = GraveBillingCheck.StoredKeys(
                stored.GetValueOrDefault(grave.Id) ?? [],
                current
            );
            if (!GraveBillingCheck.SameCorrections(computed, saved))
            {
                outdated.Add(grave.GraveNumber);
            }
        }

        return outdated.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}
