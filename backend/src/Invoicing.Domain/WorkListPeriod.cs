using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>A run of months, both ends inclusive; each is the first of its month.</summary>
public readonly record struct MonthRange(DateOnly First, DateOnly Last);

/// <summary>The periods a work list covers, and whether a grave's term runs in
/// one. A term is counted in months, like in <see cref="HalfYearBilling"/>:
/// touching a period by a single month is enough.</summary>
public static class WorkListPeriod
{
    public static MonthRange Year(int year) => new(new(year, 1, 1), new(year, 12, 1));

    public static MonthRange Quarter(int year, BillingQuarter quarter)
    {
        var first = ((int)quarter - 1) * 3 + 1;
        return new(new(year, first, 1), new(year, first + 2, 1));
    }

    public static bool Runs(DateOnly start, DateOnly? end, MonthRange range) =>
        start <= range.Last && (end is null || end >= range.First);

    public static bool Contains(MonthRange range, DateOnly month) =>
        range.First <= month && month <= range.Last;
}
