namespace Burkard.Invoicing.Domain;

/// <summary>What a grave's recipients are charged, and whether that comes out
/// even.
///
/// A grave's bill is split equally: every recipient gets the grave's own lines
/// and pays the same share of each VAT-rate subtotal. That only works when the
/// numbers divide. <see cref="Problems"/> judges whether the lines do, and
/// <see cref="CollectedCents"/> what the recipients end up paying. VAT is
/// rounded once per recipient, not on the whole grave, because that is what
/// each of them is actually billed.
///
/// When they do not divide, nothing is refused outright: <c>GraveBillingCheck</c>
/// raises one line per invoice block and VAT rate (the rounding correction), by
/// at most 1,00 €. Only a block that no line can fix within that is refused.</summary>
public static class GraveShare
{
    /// <summary>Everything about these lines that stops them being split evenly
    /// across the recipients. Empty when the grave can be billed as it stands.</summary>
    public static IReadOnlyList<GraveShareProblem> Problems(
        IReadOnlyList<GraveLine> lines,
        int recipients
    )
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        if (lines.Any(l => l.Count <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lines),
                "Every line's count must be positive."
            );
        }

        var problems = new List<GraveShareProblem>();
        foreach (var rate in lines.Select(l => l.VatRate).Distinct().OrderBy(r => r))
        {
            var group = lines.Where(l => l.VatRate == rate).ToList();
            var kind = KindOf(group, recipients, rate);
            if (kind is null)
            {
                continue;
            }

            // The figure the message names, and the suggestions, are both in the
            // unit the prices were entered in: a group priced gross is talked
            // about in gross, because that is the number the user typed and the
            // one they will change.
            var amount = PricedGross(group) ? Owed(group, rate) : group.Sum(NetCents);
            problems.Add(
                new GraveShareProblem(
                    rate,
                    kind.Value,
                    amount,
                    Suggestions(group, recipients, rate)
                )
            );
        }

        return problems;
    }

    /// <summary>Whether these lines split evenly — the verdict of
    /// <see cref="Problems"/> without the search for suggestions, which the
    /// per-line suggestion search would otherwise run thousands of times.</summary>
    public static bool IsEven(IReadOnlyList<GraveLine> lines, int recipients) =>
        lines
            .Select(l => l.VatRate)
            .Distinct()
            .All(rate =>
                KindOf(lines.Where(l => l.VatRate == rate).ToList(), recipients, rate) is null
            );

    /// <summary>What stops one rate's lines being split evenly, or null when
    /// nothing does.</summary>
    private static GraveShareProblemKind? KindOf(
        IReadOnlyList<GraveLine> group,
        int recipients,
        decimal rate
    )
    {
        var subtotal = group.Sum(NetCents);
        if (subtotal % recipients != 0)
        {
            return GraveShareProblemKind.NotDivisible;
        }

        // A price entered as a gross is a promise about what the customer pays,
        // so what the recipients collect has to come to exactly that. A
        // net-priced line makes no such promise -- its gross is derived, and
        // what the recipients collect is simply what the grave is worth.
        if (PricedGross(group) && CollectedFor(subtotal, recipients, rate) != Owed(group, rate))
        {
            return GraveShareProblemKind.DoesNotAddUp;
        }

        return null;
    }

    private static bool PricedGross(IReadOnlyList<GraveLine> group) =>
        group.Any(l => l.EnteredGross is not null);

    /// <summary>What the recipients collect between them, in cents — each of
    /// them an equal share of every VAT-rate subtotal plus that share's own
    /// rounded VAT.
    ///
    /// This is the grave's worth, and the only gross figure worth showing. VAT
    /// is rounded once per recipient, so adding VAT to the whole grave instead
    /// gives a different number: 84,26 net at 19 % is 100,27 as one bill but
    /// 100,26 as two of 50,13. Only one of those is ever paid.
    ///
    /// Expects lines that <see cref="Problems"/> passes; every rate subtotal
    /// divides exactly, so each share is a whole number of cents. Lines that do
    /// not are refused rather than answered with the odd cents dropped.</summary>
    public static long CollectedCents(IReadOnlyList<GraveLine> lines, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        if (lines.GroupBy(l => l.VatRate).Any(rate => rate.Sum(NetCents) % recipients != 0))
        {
            throw new InvalidOperationException(
                "The lines do not divide across the recipients; check them with Problems first."
            );
        }

        return lines
            .Select(l => l.VatRate)
            .Distinct()
            .Sum(rate =>
                CollectedFor(lines.Where(l => l.VatRate == rate).Sum(NetCents), recipients, rate)
            );
    }

    private static long CollectedFor(long subtotal, int recipients, decimal rate)
    {
        var share = subtotal / recipients;
        return recipients * (share + VatCents(share, rate));
    }

    /// <summary>What one rate's lines are worth, in cents: the gross that was
    /// typed for every line entered as one, and net plus VAT for the rest.</summary>
    private static long Owed(IReadOnlyList<GraveLine> group, decimal rate)
    {
        var netPriced = group.Where(l => l.EnteredGross is null).Sum(NetCents);
        var grossPriced = group
            .Where(l => l.EnteredGross is not null)
            .Sum(l =>
                (long)decimal.Round(l.EnteredGross!.Value * 100m, 0, MidpointRounding.AwayFromZero)
                * l.Count
            );

        return netPriced + VatCents(netPriced, rate) + grossPriced;
    }

    /// <summary>The nearest amounts, one below and one above, that would come
    /// out even — so the message can name figures to aim at rather than only
    /// saying no. In the same unit as the problem's own amount.
    ///
    /// A net-priced group is searched by moving its subtotal, which is what
    /// changing any of its prices does. A group priced gross is searched by
    /// moving the gross that was typed, one cent at a time, and re-testing the
    /// whole group: a cent on a gross moves the net subtotal by about
    /// 1/(1 + rate) of a cent, so searching the subtotal instead looks in the
    /// wrong place and finds nothing.
    ///
    /// Empty where nothing within reach works, and the message then says to
    /// adjust a price without naming one.</summary>
    private static IReadOnlyList<long> Suggestions(
        IReadOnlyList<GraveLine> group,
        int recipients,
        decimal rate
    )
    {
        var below = 0L;
        var above = 0L;

        for (var distance = 1; distance <= 100 && (below == 0 || above == 0); distance++)
        {
            if (below == 0)
            {
                below = CandidateAt(group, recipients, rate, -distance);
            }

            if (above == 0)
            {
                above = CandidateAt(group, recipients, rate, distance);
            }
        }

        return new[] { below, above }.Where(value => value != 0).ToList();
    }

    /// <summary>The amount this group would come to with `offset` cents added,
    /// if that comes out even — otherwise 0.</summary>
    private static long CandidateAt(
        IReadOnlyList<GraveLine> group,
        int recipients,
        decimal rate,
        long offset
    )
    {
        if (PricedGross(group))
        {
            // move the first gross-entered line's own price; its count decides
            // how far that moves the group, so the suggestion is always a figure
            // the prices can actually reach
            var index = group.ToList().FindIndex(l => l.EnteredGross is not null);
            var line = group[index];
            var price = line.EnteredGross!.Value + offset / 100m;
            if (price <= 0)
            {
                return 0;
            }

            var moved = group.ToList();
            moved[index] = line with
            {
                EnteredGross = price,
                UnitPrice = VatInclusivePrice.NetOf(price, rate, true),
            };
            return KindOf(moved, recipients, rate) is null ? Owed(moved, rate) : 0;
        }

        var subtotal = group.Sum(NetCents) + offset;
        return subtotal > 0 && subtotal % recipients == 0 ? subtotal : 0;
    }

    private static long VatCents(long netCents, decimal rate) =>
        (long)decimal.Round(netCents * rate / 100m, 0, MidpointRounding.AwayFromZero);

    private static long NetCents(GraveLine line) =>
        (long)decimal.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
}

public enum GraveShareProblemKind
{
    /// <summary>The rate's net subtotal does not divide evenly across the
    /// recipients, so they cannot be billed the same amount.</summary>
    NotDivisible,

    /// <summary>It divides, but what the recipients collect between them is not
    /// what the lines are worth — a price entered as a gross cannot be billed
    /// exactly as it was typed.</summary>
    DoesNotAddUp,
}

/// <summary>One reason a grave cannot be split as its prices stand.
///
/// <paramref name="Amount"/> and <paramref name="Suggestions"/> are in cents,
/// and in the unit the rate's prices were entered in: net where they are net
/// prices, gross where any of them was typed as a gross. That is the number the
/// user sees in the field and the one they will change.</summary>
public sealed record GraveShareProblem(
    decimal VatRate,
    GraveShareProblemKind Kind,
    long Amount,
    IReadOnlyList<long> Suggestions
);
