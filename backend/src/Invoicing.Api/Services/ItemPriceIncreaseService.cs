using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>One ticked row of a price increase: the default price, or one own
/// price the graves bill the item at. <see cref="PriceIncludesVat"/> only tells
/// own rows apart; the default row ignores it.</summary>
public sealed class PriceIncreaseRowInput
{
    public bool IsDefault { get; set; }
    public decimal OldPrice { get; set; }
    public bool PriceIncludesVat { get; set; }
    public decimal NewPrice { get; set; }

    /// <summary>The graves the popover showed in the row (apply only).</summary>
    public List<long>? GraveIds { get; set; }
}

/// <summary>One correction the user confirmed (apply only).</summary>
public sealed class PriceIncreaseCorrectionInput
{
    public long GraveId { get; set; }
    public string Kind { get; set; } = "";
    public int? Year { get; set; }
    public int Half { get; set; }
    public decimal VatRate { get; set; }
    public long ItemId { get; set; }
    public decimal CorrectedPrice { get; set; }
}

public sealed class ItemPriceIncreaseRequest
{
    public long ItemId { get; set; }
    public List<PriceIncreaseRowInput>? Rows { get; set; }
    public List<PriceIncreaseCorrectionInput>? Corrections { get; set; }
}

/// <summary>A grave no correction can split at the new price: the first invoice
/// that fails and the VAT rate that does not divide.</summary>
public sealed record PriceIncreaseProblem(
    long GraveId,
    string GraveNumber,
    string Invoice,
    decimal VatRate
);

public sealed record PriceIncreaseCorrection(
    long GraveId,
    string GraveNumber,
    string Kind,
    int? Year,
    int Half,
    string Invoice,
    decimal VatRate,
    long ItemId,
    string ItemNumber,
    decimal EnteredPrice,
    decimal CorrectedPrice
);

/// <summary><c>RowProblems[i]</c> belongs to <c>Rows[i]</c> of the request.</summary>
public sealed record PriceIncreasePreview(
    IReadOnlyList<IReadOnlyList<PriceIncreaseProblem>> RowProblems,
    IReadOnlyList<PriceIncreaseCorrection> Corrections
);

public sealed class ItemNotFoundException : Exception
{
    public ItemNotFoundException()
        : base("The item does not exist.") { }
}

/// <summary>Raising an item's default price and the graves' own prices for it.
/// Judged on the whole grave: its other items bill at their unchanged prices,
/// so a raised grave is checked as it will be billed.</summary>
public sealed class ItemPriceIncreaseService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public ItemPriceIncreaseService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The problem a grave's evaluation names, or null when it divides.</summary>
    public static PriceIncreaseProblem? ProblemOf(
        long graveId,
        string graveNumber,
        GraveBillingEvaluation evaluation
    )
    {
        if (evaluation.FirstFailing is not { } failing)
        {
            return null;
        }

        return new PriceIncreaseProblem(
            graveId,
            graveNumber,
            HalfYearBilling.Label(failing.Example),
            failing.Problems[0].VatRate
        );
    }

    public async Task<PriceIncreasePreview> PreviewAsync(
        ItemPriceIncreaseRequest? request,
        CancellationToken ct
    )
    {
        if (request?.Rows is not { Count: > 0 } rows)
        {
            throw new ValidationException(
                "InvalidRequest",
                "The request must carry at least one row."
            );
        }

        var keys = rows.Select(r =>
            r.IsDefault ? (true, 0m, false) : (false, r.OldPrice, r.PriceIncludesVat)
        );
        if (keys.Distinct().Count() != rows.Count)
        {
            throw new ValidationException("InvalidRequest", "A price is listed twice.");
        }

        if (rows.Any(r => r.NewPrice < 0.01m || !GraveBilling.IsWholeCents(r.NewPrice)))
        {
            throw new ValidationException(
                "InvalidPrice",
                "A new price must be at least 0.01 with at most two decimals."
            );
        }

        var item =
            await _db
                .InvoiceItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == request.ItemId, ct)
            ?? throw new ItemNotFoundException();

        foreach (var row in rows)
        {
            var stale = row.IsDefault
                ? item.DefaultPrice is null || item.DefaultPrice != row.OldPrice
                : item.DefaultPrice == row.OldPrice;
            if (stale)
            {
                throw new ValidationException(
                    "PricesChanged",
                    "The prices changed since the increase was prepared."
                );
            }
        }

        var graves = await _db
            .Graves.AsNoTracking()
            .Where(g => g.DeletedAt == null && g.Items.Any(l => l.ItemId == item.Id))
            .Include(g => g.Items)
                .ThenInclude(l => l.Item)
            .Include(g => g.Recipients)
            .OrderBy(g => g.Id)
            .ToListAsync(ct);

        var problems = rows.Select(_ => new List<PriceIncreaseProblem>()).ToList();
        var corrections = new List<PriceIncreaseCorrection>();
        var current = HalfYearBilling.HalfYearOf(_clock.GetLocalNow());

        foreach (var grave in graves)
        {
            var links = grave.Items.OrderBy(l => l.Id).ToList();
            var raisedRows = new Dictionary<long, int>();
            var billable = new List<BillableLink>();
            foreach (var link in links)
            {
                var rowIndex =
                    link.ItemId == item.Id
                        ? RowOf(rows, item, link.UnitPrice, link.PriceIncludesVat)
                        : -1;
                var unitPrice = link.UnitPrice;
                if (rowIndex >= 0)
                {
                    raisedRows[link.Id] = rowIndex;
                    unitPrice = rows[rowIndex].NewPrice;
                }

                billable.Add(
                    GraveBilling.LinkFor(link.Item, link.Count, unitPrice, link.PriceIncludesVat)
                );
            }

            if (raisedRows.Count == 0)
            {
                continue;
            }

            // a cooperative grave is never split, and one without recipients
            // has nothing to divide by: neither is evaluated or corrected
            if (grave.IsCooperative || grave.Recipients.Count == 0)
            {
                continue;
            }

            var evaluation = GraveBillingCheck.Evaluate(
                billable,
                grave.StartDate,
                grave.EndDate,
                grave.Recipients.Count,
                current
            );

            if (ProblemOf(grave.Id, grave.GraveNumber, evaluation) is { } problem)
            {
                foreach (var rowIndex in raisedRows.Values.Distinct())
                {
                    problems[rowIndex].Add(problem);
                }
            }

            foreach (var invoice in evaluation.Invoices)
            {
                foreach (var c in invoice.Corrections)
                {
                    corrections.Add(
                        new PriceIncreaseCorrection(
                            grave.Id,
                            grave.GraveNumber,
                            invoice.Example.Kind.ToString(),
                            invoice.Example.Period.Year,
                            invoice.Example.Period.Half,
                            HalfYearBilling.Label(invoice.Example),
                            c.VatRate,
                            c.ItemId,
                            c.ItemNumber,
                            c.EnteredPrice,
                            c.CorrectedPrice
                        )
                    );
                }
            }
        }

        return new PriceIncreasePreview(
            problems.Select(p => (IReadOnlyList<PriceIncreaseProblem>)p).ToList(),
            corrections
        );
    }

    /// <summary>The ticked row a link of the item belongs to, or -1. The default
    /// row holds the links billing the default, compared by number only; an own
    /// row holds the links at exactly its price and gross flag.</summary>
    private static int RowOf(
        List<PriceIncreaseRowInput> rows,
        Domain.Entities.InvoiceItem item,
        decimal? unitPrice,
        bool gross
    )
    {
        var billsDefault = unitPrice is null || unitPrice == item.DefaultPrice;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (
                row.IsDefault
                    ? billsDefault
                    : !billsDefault && unitPrice == row.OldPrice && gross == row.PriceIncludesVat
            )
            {
                return i;
            }
        }

        return -1;
    }
}
