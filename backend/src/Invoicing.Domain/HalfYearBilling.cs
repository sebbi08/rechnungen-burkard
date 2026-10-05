using System.Globalization;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>One of a grave's item links as the half-year rule sees it: the
/// item's facts, the grave's count, and the price billed for a full half-year
/// in the unit it was entered in — the gross when <paramref name="PriceIncludesVat"/>.</summary>
public sealed record BillableLink(
    long ItemId,
    string ItemNumber,
    string Description,
    decimal VatRate,
    BillingQuarter? Quarter,
    int Count,
    decimal EnteredPrice,
    bool PriceIncludesVat
);

/// <summary>The months of one half-year an invoice covers, both ends inclusive,
/// as month numbers 1–12. <see cref="Year"/> is null for the generic full
/// half-years the editor previews; a prorated period always has one.</summary>
public sealed record BillingPeriod(int? Year, int Half, int FirstMonth, int LastMonth)
{
    public int Months => LastMonth - FirstMonth + 1;

    public bool IsFull => Months == 6;

    public static BillingPeriod Full(int half) =>
        half == 1 ? new(null, 1, 1, 6) : new(null, 2, 7, 12);
}

public enum BillingExampleKind
{
    Start,
    StartAndEnd,
    FullFirstHalf,
    FullSecondHalf,
    End,
}

/// <summary>One of the invoices a grave's term produces.</summary>
public sealed record BillingExample(BillingExampleKind Kind, BillingPeriod Period);

/// <summary>A billed line, the link it came from, and its price in the unit it
/// was entered in (prorated where the period is).</summary>
public sealed record BilledLine(int LinkIndex, decimal Price, GraveLine Line);

/// <summary>What a grave bills for one half-year.
///
/// A grave's term is counted in months. Only the months of the half-year that
/// fall inside it are billed: an all-year item at price × n ÷ 6, a quarter item
/// in full when one of its months is among them, and nothing at all when none
/// is. Saving a grave, the import, the editor's preview and generation all ask
/// here, so they cannot disagree about what a half-year costs.</summary>
public static class HalfYearBilling
{
    /// <summary>The months of the half-year inside the term, or null when there
    /// are none. Days are ignored: a term runs from its start month through its
    /// end month.</summary>
    public static BillingPeriod? PeriodIn(DateOnly start, DateOnly? end, int year, int half)
    {
        var halfFirst = year * 12 + (half == 1 ? 0 : 6);
        var halfLast = halfFirst + 5;
        var first = Math.Max(Index(start), halfFirst);
        var last = Math.Min(end is { } until ? Index(until) : int.MaxValue, halfLast);
        return first > last ? null : new BillingPeriod(year, half, first % 12 + 1, last % 12 + 1);
    }

    /// <summary>The lines billed for a period, each with the index of the link
    /// it came from. Links billed nothing in the period are left out.</summary>
    public static IReadOnlyList<BilledLine> Bill(
        IReadOnlyList<BillableLink> links,
        BillingPeriod period
    )
    {
        var billed = new List<BilledLine>(links.Count);
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (link.Quarter is { } quarter)
            {
                // a quarter's work is done or not: in full once it is touched
                if (Touches(quarter, period))
                {
                    billed.Add(Billed(index, link, link.EnteredPrice, link.Description));
                }

                continue;
            }

            if (period.IsFull)
            {
                billed.Add(Billed(index, link, link.EnteredPrice, link.Description));
                continue;
            }

            var prorated = decimal.Round(
                link.EnteredPrice * period.Months / 6m,
                2,
                MidpointRounding.AwayFromZero
            );
            billed.Add(Billed(index, link, prorated, link.Description + ProrationNote(period)));
        }

        return billed;
    }

    public static IReadOnlyList<GraveLine> Lines(
        IReadOnlyList<BillableLink> links,
        BillingPeriod period
    ) => Bill(links, period).Select(b => b.Line).ToList();

    /// <summary>Whether at least one month of the quarter is in the period.</summary>
    public static bool Touches(BillingQuarter quarter, BillingPeriod period)
    {
        var first = ((int)quarter - 1) * 3 + 1;
        return first <= period.LastMonth && period.FirstMonth <= first + 2;
    }

    /// <summary>The invoices the term produces, in order: the start (or the
    /// start and end in one), a full H1 and a full H2 where one lies entirely
    /// inside the term, and the end. Without a start, only the two full ones.</summary>
    public static IReadOnlyList<BillingExample> Examples(DateOnly? start, DateOnly? end)
    {
        var fullFirst = new BillingExample(BillingExampleKind.FullFirstHalf, BillingPeriod.Full(1));
        var fullSecond = new BillingExample(
            BillingExampleKind.FullSecondHalf,
            BillingPeriod.Full(2)
        );
        if (start is not { } from)
        {
            return new[] { fullFirst, fullSecond };
        }

        if (end is { } until && Index(until) < Index(from))
        {
            throw new ArgumentException(
                "The end month must not be before the start month.",
                nameof(end)
            );
        }

        var (startYear, startHalf) = HalfOf(from);
        (int Year, int Half)? endHalf = end is { } last ? HalfOf(last) : null;
        var sameHalf = endHalf == (startYear, startHalf);

        var examples = new List<BillingExample>
        {
            new(
                sameHalf ? BillingExampleKind.StartAndEnd : BillingExampleKind.Start,
                PeriodIn(from, end, startYear, startHalf)!
            ),
        };

        var (hasFirst, hasSecond) = FullHalves(from, end);
        if (hasFirst)
        {
            examples.Add(fullFirst);
        }

        if (hasSecond)
        {
            examples.Add(fullSecond);
        }

        if (endHalf is { } final && !sameHalf)
        {
            examples.Add(new(BillingExampleKind.End, PeriodIn(from, end, final.Year, final.Half)!));
        }

        return examples;
    }

    /// <summary>How messages name an invoice: "Anfang H1 2026", "Volles H2".</summary>
    public static string Label(BillingExample example)
    {
        var period = example.Period;
        return example.Kind switch
        {
            BillingExampleKind.Start => Invariant($"Anfang H{period.Half} {period.Year}"),
            BillingExampleKind.StartAndEnd => Invariant(
                $"Anfang und Ende H{period.Half} {period.Year}"
            ),
            BillingExampleKind.End => Invariant($"Ende H{period.Half} {period.Year}"),
            BillingExampleKind.FullFirstHalf => "Volles H1",
            _ => "Volles H2",
        };
    }

    /// <summary>", anteilig 05–06/2026 (2 von 6 Monaten)" — appended to a
    /// prorated line's description and stored with it.</summary>
    public static string ProrationNote(BillingPeriod period)
    {
        var year =
            period.Year
            ?? throw new ArgumentException("A prorated period needs its year.", nameof(period));
        var months =
            period.Months == 1
                ? Invariant($"{period.FirstMonth:00}/{year}")
                : Invariant($"{period.FirstMonth:00}–{period.LastMonth:00}/{year}");
        return Invariant($", anteilig {months} ({period.Months} von 6 Monaten)");
    }

    private static (bool First, bool Second) FullHalves(DateOnly start, DateOnly? end)
    {
        if (end is null)
        {
            return (true, true);
        }

        var first = false;
        var second = false;
        var (year, half) = HalfOf(start);
        var (lastYear, lastHalf) = HalfOf(end.Value);
        while (year * 2 + half <= lastYear * 2 + lastHalf && !(first && second))
        {
            if (PeriodIn(start, end, year, half) is { IsFull: true })
            {
                if (half == 1)
                {
                    first = true;
                }
                else
                {
                    second = true;
                }
            }

            (year, half) = half == 1 ? (year, 2) : (year + 1, 1);
        }

        return (first, second);
    }

    private static BilledLine Billed(
        int index,
        BillableLink link,
        decimal price,
        string description
    ) =>
        new(
            index,
            price,
            new GraveLine(
                link.ItemId,
                link.ItemNumber,
                description,
                link.Count,
                VatInclusivePrice.NetOf(price, link.VatRate, link.PriceIncludesVat),
                link.VatRate,
                link.PriceIncludesVat,
                link.PriceIncludesVat ? price : null
            )
        );

    private static (int Year, int Half) HalfOf(DateOnly month) =>
        (month.Year, month.Month <= 6 ? 1 : 2);

    private static int Index(DateOnly month) => month.Year * 12 + month.Month - 1;

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
