namespace Burkard.Invoicing.Domain;

/// <summary>What a grave's recipients are charged, and whether that comes out
/// even.
///
/// A grave's bill is split equally: every recipient gets the grave's own lines
/// and pays the same share of each VAT-rate subtotal. That only works when the
/// numbers divide, so the prices have to be chosen for it — nothing here adjusts
/// them. <see cref="Problems"/> reports what does not come out, and saving a
/// grave is refused until it does.
///
/// Raising prices automatically was tried first and abandoned. It kept the
/// arithmetic out of sight but put figures on the page that nobody had typed,
/// and left the grave's own worth and what the recipients paid a cent or two
/// apart with no honest way to show both.</summary>
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
            var subtotal = group.Sum(NetCents);

            if (subtotal % recipients != 0)
            {
                problems.Add(
                    new GraveShareProblem(
                        rate,
                        GraveShareProblemKind.NotDivisible,
                        subtotal,
                        CollectedFor(subtotal, recipients, rate),
                        Owed(group, rate),
                        NearbyValidSubtotals(group, subtotal, recipients, rate)
                    )
                );
                continue;
            }

            // A price entered as a gross is a promise about what the customer
            // pays, so what the recipients collect has to come to exactly that.
            // A net-priced line makes no such promise -- its gross is derived,
            // and what the recipients collect is simply what the grave is worth.
            if (group.Any(l => l.EnteredGross is not null))
            {
                var collected = CollectedFor(subtotal, recipients, rate);
                var owed = Owed(group, rate);
                if (collected != owed)
                {
                    problems.Add(
                        new GraveShareProblem(
                            rate,
                            GraveShareProblemKind.DoesNotAddUp,
                            subtotal,
                            collected,
                            owed,
                            NearbyValidSubtotals(group, subtotal, recipients, rate)
                        )
                    );
                }
            }
        }

        return problems;
    }

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
    /// divides exactly, so each share is a whole number of cents.</summary>
    public static long CollectedCents(IReadOnlyList<GraveLine> lines, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
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

    /// <summary>The nearest net subtotals, above and below, that would divide
    /// and add up — so the message can name a figure to aim at rather than only
    /// saying no. Empty where nothing nearby works, which the caller reports as
    /// "adjust a price" without a number.</summary>
    private static IReadOnlyList<long> NearbyValidSubtotals(
        IReadOnlyList<GraveLine> group,
        long subtotal,
        int recipients,
        decimal rate
    )
    {
        var checksGross = group.Any(l => l.EnteredGross is not null);
        var found = new List<long>();

        for (var distance = 1; distance <= 200 && found.Count < 2; distance++)
        {
            foreach (var candidate in new[] { subtotal - distance, subtotal + distance })
            {
                if (candidate <= 0 || candidate % recipients != 0 || found.Contains(candidate))
                {
                    continue;
                }

                // The gross test compares against what the lines are owed, which
                // moves with the prices being adjusted -- so a candidate subtotal
                // is only meaningful if the same adjustment lands on the owed
                // figure too. Shifting both by the same amount is what adjusting
                // a gross-entered price actually does.
                if (checksGross)
                {
                    if (
                        CollectedFor(candidate, recipients, rate)
                        != Owed(group, rate) + (candidate - subtotal)
                    )
                    {
                        continue;
                    }
                }

                found.Add(candidate);
            }
        }

        found.Sort();
        return found;
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

/// <summary>One reason a grave cannot be split as its prices stand. All amounts
/// are in cents.</summary>
public sealed record GraveShareProblem(
    decimal VatRate,
    GraveShareProblemKind Kind,
    long NetSubtotal,
    long Collected,
    long Owed,
    IReadOnlyList<long> NearbyValidSubtotals
);
