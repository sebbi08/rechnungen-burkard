using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>One ticked row of a price increase: the default price, or one own
/// price the graves bill the item at.</summary>
public sealed class PriceIncreaseRowInput
{
    public bool IsDefault { get; set; }
    public decimal OldPrice { get; set; }
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

/// <summary>A price increase a grave's rounding cannot absorb: the first grave
/// that fails and the invoice that does not divide.</summary>
public sealed class PriceIncreaseDoesNotDivideException : ValidationException
{
    public PriceIncreaseDoesNotDivideException(string graveNumber, string invoiceLabel)
        : base("PricesDoNotDivide", "A raised price does not divide among the recipients.")
    {
        Args = [graveNumber, invoiceLabel];
    }
}

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

    /// <summary>The request's rows and the item, once the request is known to be
    /// well formed and the rows still describe the item's prices.</summary>
    private async Task<(List<PriceIncreaseRowInput> Rows, InvoiceItem Item)> PrepareAsync(
        ItemPriceIncreaseRequest? request,
        bool track,
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

        var keys = rows.Select(r => r.IsDefault ? (true, 0m) : (false, r.OldPrice));
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

        var items = track ? _db.InvoiceItems : _db.InvoiceItems.AsNoTracking();
        var item =
            await items.FirstOrDefaultAsync(i => i.Id == request.ItemId, ct)
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

        return (rows, item);
    }

    /// <summary>A grave the increase raises: the row each raised link belongs to
    /// (by link id), and its evaluation at the new prices, or null when no
    /// evaluation applies.</summary>
    private sealed record GravePlan(
        Grave Grave,
        Dictionary<long, int> RaisedRows,
        GraveBillingEvaluation? Evaluation
    );

    /// <summary>Resolves every link of the item to its row, substitutes the new
    /// prices and evaluates each affected grave as a whole, as a save of it with
    /// those prices would. Preview and apply both stand on this.</summary>
    private async Task<List<GravePlan>> PlanAsync(
        List<PriceIncreaseRowInput> rows,
        InvoiceItem item,
        bool track,
        CancellationToken ct
    )
    {
        var query = _db
            .Graves.Where(g => g.DeletedAt == null && g.Items.Any(l => l.ItemId == item.Id))
            .Include(g => g.Items)
                .ThenInclude(l => l.Item)
            .Include(g => g.Recipients)
            .OrderBy(g => g.Id)
            .AsQueryable();
        var graves = await (track ? query : query.AsNoTracking()).ToListAsync(ct);

        var plans = new List<GravePlan>();
        var current = HalfYearBilling.HalfYearOf(_clock.GetLocalNow());

        foreach (var grave in graves)
        {
            var raisedRows = new Dictionary<long, int>();
            var billable = new List<BillableLink>();
            foreach (var link in grave.Items.OrderBy(l => l.Id))
            {
                var rowIndex = link.ItemId == item.Id ? RowOf(rows, item, link.UnitPrice) : -1;
                var unitPrice = link.UnitPrice;
                if (rowIndex >= 0)
                {
                    raisedRows[link.Id] = rowIndex;
                    unitPrice = rows[rowIndex].NewPrice;
                }

                billable.Add(GraveBilling.LinkFor(link.Item, link.Count, unitPrice));
            }

            if (raisedRows.Count == 0)
            {
                continue;
            }

            // a cooperative grave is never split, and one without recipients
            // has nothing to divide by: neither is evaluated or corrected
            var evaluation =
                grave.IsCooperative || grave.Recipients.Count == 0
                    ? null
                    : GraveBillingCheck.Evaluate(
                        billable,
                        grave.StartDate,
                        grave.EndDate,
                        grave.Recipients.Count,
                        current
                    );
            plans.Add(new GravePlan(grave, raisedRows, evaluation));
        }

        return plans;
    }

    public async Task<PriceIncreasePreview> PreviewAsync(
        ItemPriceIncreaseRequest? request,
        CancellationToken ct
    )
    {
        var (rows, item) = await PrepareAsync(request, track: false, ct);
        var plans = await PlanAsync(rows, item, track: false, ct);

        var problems = rows.Select(_ => new List<PriceIncreaseProblem>()).ToList();
        var corrections = new List<PriceIncreaseCorrection>();
        foreach (var plan in plans)
        {
            if (plan.Evaluation is not { } evaluation)
            {
                continue;
            }

            var grave = plan.Grave;
            if (ProblemOf(grave.Id, grave.GraveNumber, evaluation) is { } problem)
            {
                foreach (var rowIndex in plan.RaisedRows.Values.Distinct())
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

    /// <summary>Raises the prices and replaces the affected graves' corrections in
    /// one transaction. Refuses unless the request still matches what is stored
    /// and the corrections are the ones the user confirmed.</summary>
    public async Task ApplyAsync(ItemPriceIncreaseRequest? request, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (rows, item) = await PrepareAsync(request, track: true, ct);
        var plans = await PlanAsync(rows, item, track: true, ct);

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].GraveIds is not { } expected)
            {
                throw new ValidationException("InvalidRequest", "A row must list its graves.");
            }

            var found = plans
                .Where(p => p.RaisedRows.Values.Contains(i))
                .Select(p => p.Grave.Id)
                .ToHashSet();
            if (!found.SetEquals(expected))
            {
                throw new ValidationException(
                    "PricesChanged",
                    "The prices changed since the increase was prepared."
                );
            }
        }

        foreach (var plan in plans.OrderBy(p => p.Grave.GraveNumber, StringComparer.Ordinal))
        {
            if (
                plan.Evaluation is { } evaluation
                && ProblemOf(plan.Grave.Id, plan.Grave.GraveNumber, evaluation) is { } problem
            )
            {
                throw new PriceIncreaseDoesNotDivideException(problem.GraveNumber, problem.Invoice);
            }
        }

        var confirmed = (request!.Corrections ?? [])
            .GroupBy(c => c.GraveId)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.Select(c => new CorrectionKey(
                            Enum.TryParse<BillingExampleKind>(c.Kind, out var kind)
                            && Enum.IsDefined(kind)
                                ? kind
                                : (BillingExampleKind)(-1),
                            c.Year,
                            c.Half,
                            c.VatRate,
                            c.ItemId,
                            c.CorrectedPrice
                        ))
                        .ToList()
            );
        var affected = plans.Select(p => p.Grave.Id).ToHashSet();
        if (
            confirmed.Keys.Any(id => !affected.Contains(id))
            || plans.Any(p =>
                !GraveBillingCheck.SameCorrections(
                    p.Evaluation?.CorrectionKeys ?? [],
                    confirmed.GetValueOrDefault(p.Grave.Id) ?? []
                )
            )
        )
        {
            throw new ValidationException(
                "CorrectionsChanged",
                "The rounding corrections differ from the ones confirmed."
            );
        }

        if (rows.FirstOrDefault(r => r.IsDefault) is { } defaultRow)
        {
            item.DefaultPrice = defaultRow.NewPrice;
        }

        _db.GraveBillingCorrections.RemoveRange(
            await _db
                .GraveBillingCorrections.Where(c => affected.Contains(c.GraveId))
                .ToListAsync(ct)
        );
        foreach (var plan in plans)
        {
            foreach (var link in plan.Grave.Items.Where(l => plan.RaisedRows.ContainsKey(l.Id)))
            {
                // a link without its own price keeps billing the default
                if (link.UnitPrice is not null)
                {
                    link.UnitPrice = rows[plan.RaisedRows[link.Id]].NewPrice;
                }
            }

            if (plan.Evaluation is { } evaluation)
            {
                _db.GraveBillingCorrections.AddRange(
                    GraveCorrections.Build(plan.Grave.Id, evaluation)
                );
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>The ticked row a link of the item belongs to, or -1. The default
    /// row holds the links billing the default, compared by number only; an own
    /// row holds the links at exactly its price.</summary>
    private static int RowOf(List<PriceIncreaseRowInput> rows, InvoiceItem item, decimal? unitPrice)
    {
        var billsDefault = unitPrice is null || unitPrice == item.DefaultPrice;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.IsDefault ? billsDefault : !billsDefault && unitPrice == row.OldPrice)
            {
                return i;
            }
        }

        return -1;
    }
}
