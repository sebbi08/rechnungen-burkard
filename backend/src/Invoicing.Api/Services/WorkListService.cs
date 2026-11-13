using System.Globalization;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Which services the care list prints under each grave.</summary>
public enum CareListServices
{
    None,

    /// <summary>The all-year services.</summary>
    Care,

    /// <summary>The all-year services and those of the quarters the grave runs in.</summary>
    All,
}

/// <summary>No grave is on the requested list, so there is nothing to print.</summary>
public sealed class NoGravesOnListException : ValidationException
{
    public NoGravesOnListException(params string[] args)
        : base("NoGravesOnList", "No grave is on this list.")
    {
        Args = args;
    }
}

/// <summary>Gathers the graves of the care list and the planting list: which
/// run in the period, their services and amounts, in the order they print.</summary>
public sealed class WorkListService(AppDbContext db, IClock clock)
{
    private static readonly StringComparer CemeteryOrder = StringComparer.Create(
        CultureInfo.GetCultureInfo("de-DE"),
        ignoreCase: true
    );

    /// <summary>The care list of a year, or null when no grave runs in it.</summary>
    public async Task<WorkListData?> CareListAsync(int year, CareListServices services)
    {
        var range = WorkListPeriod.Year(year);
        var graves = await RunningAsync(range);
        var kind =
            services == CareListServices.None
                ? WorkListKind.CareGravesOnly
                : WorkListKind.CareWithServices;
        return Build(
            kind,
            year,
            null,
            range,
            graves,
            g =>
                services switch
                {
                    CareListServices.None => [],
                    CareListServices.Care => g.Items.Where(l => l.Item.Quarter is null),
                    _ => g.Items.Where(l =>
                        l.Item.Quarter is not { } q
                        || WorkListPeriod.Runs(
                            g.StartDate,
                            g.EndDate,
                            WorkListPeriod.Quarter(year, q)
                        )
                    ),
                }
        );
    }

    /// <summary>The planting list of a quarter, or null when no grave has a
    /// service of that quarter running in it.</summary>
    public async Task<WorkListData?> PlantingListAsync(int year, BillingQuarter quarter)
    {
        var range = WorkListPeriod.Quarter(year, quarter);
        var graves = (await RunningAsync(range))
            .Where(g => g.Items.Any(l => l.Item.Quarter == quarter))
            .ToList();
        return Build(
            WorkListKind.Planting,
            year,
            quarter,
            range,
            graves,
            g => g.Items.Where(l => l.Item.Quarter == quarter)
        );
    }

    private async Task<List<Grave>> RunningAsync(MonthRange range)
    {
        var graves = await db
            .Graves.AsNoTracking()
            .Include(g => g.Cemetery)
            .Include(g => g.Items)
                .ThenInclude(l => l.Item)
            .Where(g => g.DeletedAt == null)
            .ToListAsync();
        return graves.Where(g => WorkListPeriod.Runs(g.StartDate, g.EndDate, range)).ToList();
    }

    private WorkListData? Build(
        WorkListKind kind,
        int year,
        BillingQuarter? quarter,
        MonthRange range,
        List<Grave> graves,
        Func<Grave, IEnumerable<GraveItem>> linesOf
    )
    {
        var cemeteries = graves
            .GroupBy(g => g.Cemetery.Name)
            .OrderBy(c => c.Key, CemeteryOrder)
            .Select(c => new WorkListCemetery(
                c.Key,
                c.OrderBy(g => g.GraveNumber, GraveNumberOrder.Instance)
                    .ThenBy(g => g.StartDate)
                    .Select(g => ToGrave(g, range, linesOf(g)))
                    .ToList()
            ))
            .ToList();
        return cemeteries.Count == 0
            ? null
            : new WorkListData(
                kind,
                year,
                quarter,
                DateOnly.FromDateTime(clock.GetLocalNow()),
                cemeteries
            );
    }

    private static WorkListGrave ToGrave(Grave g, MonthRange range, IEnumerable<GraveItem> links) =>
        new(
            g.GraveNumber,
            g.SiteName,
            g.FieldNumber,
            g.Position,
            g.StartDate,
            g.EndDate,
            WorkListPeriod.Contains(range, g.StartDate),
            g.EndDate is { } end && WorkListPeriod.Contains(range, end),
            g.ContractNumber,
            links.OrderBy(l => l.Id).Select(ToLine).ToList()
        );

    private static WorkListLine ToLine(GraveItem link)
    {
        var item = link.Item;
        var price = link.UnitPrice ?? item.DefaultPrice;
        decimal? amount = price is { } p
            ? VatInclusivePrice.GrossOf(
                decimal.Round(p, 2, MidpointRounding.AwayFromZero),
                item.VatRate,
                link.PriceIncludesVat
            ) * link.Count
            : null;
        return new WorkListLine(link.Count, item.Number, item.Description, item.Quarter, amount);
    }
}
