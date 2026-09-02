namespace Burkard.Invoicing.Domain;

/// <summary>Raises a grave's unit prices by whole cents until every VAT-rate
/// subtotal divides evenly across the recipients and the shares together still
/// collect the grave's own bill, so each recipient can be billed the identical
/// share without the split costing the business a cent.
///
/// Divisibility alone is not enough, because the VAT is rounded once per
/// recipient rather than once for the grave. A subtotal of 84,24 at 19 % over
/// two recipients divides exactly — 42,12 each — but each recipient's VAT
/// rounds 8,0028 down to 8,00, where the whole grave would have rounded 16,0056
/// up to 16,01. Two roundings down replace one rounding up and the split
/// collects 100,24 against a bill of 100,25. So a subtotal has to satisfy both
/// tests, and the cheapest raise satisfying both is the one taken.
///
/// Adding one cent to a line's unit price moves its subtotal by the line's
/// count. With g = gcd(counts, recipients), g divides every count and therefore
/// the subtotal too, and the reachable additions are exactly the multiples of
/// g — so the residue needed is always reachable. Adding a cent to each line in
/// turn is not enough: with three recipients and lines of count 1 and 2, a full
/// lap adds three cents and returns to the same residue forever. Choosing which
/// line to bump is what makes it terminate, and a shortest path over the
/// residues finds the fewest cents.</summary>
public static class GraveShare
{
    public static IReadOnlyList<GraveLine> MakeDivisible(
        IReadOnlyList<GraveLine> lines,
        int recipients
    )
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        // A non-positive count breaks the search below rather than producing a
        // wrong answer. Count 0 gives the residue graph a zero-cost self-loop,
        // and the lexicographic tie-break keeps accepting an ever-longer path
        // through it, so the relaxation never settles. A negative count indexes
        // best[next] negatively and throws. Callers reject non-positive counts
        // already, so this only keeps that from being the sole thing between an
        // edit here and a hang.
        if (lines.Any(l => l.Count <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lines),
                "Every line's count must be positive."
            );
        }

        var result = lines.ToList();
        // materialised: the loop rewrites result as it goes
        foreach (var rate in result.Select(l => l.VatRate).Distinct().ToList())
        {
            var indices = Enumerable
                .Range(0, result.Count)
                .Where(i => result[i].VatRate == rate)
                .ToList();
            var subtotal = indices.Sum(i => NetCents(result[i]));
            foreach (
                var i in CheapestBumps(
                    indices.Select(i => result[i].Count).ToList(),
                    subtotal,
                    recipients,
                    rate,
                    BillFor(indices.Select(i => result[i]).ToList(), rate)
                )
            )
            {
                var line = result[indices[i]];
                result[indices[i]] = line with { UnitPrice = line.UnitPrice + 0.01m };
            }
        }

        return result;
    }

    /// <summary>Which lines to bump by one cent, fewest cents added first.
    ///
    /// A bump of line i raises the subtotal by counts[i], so the cents a bump
    /// set adds to the bill are exactly the subtotal it adds — cheapest and
    /// smallest-delta are the same thing, and the search can walk the reachable
    /// deltas in ascending order and stop at the first one that works. A delta
    /// works when the raised subtotal both divides across the recipients and
    /// leaves their shares collecting the grave's own bill.
    ///
    /// Walking deltas rather than residues is what lets the second test be
    /// applied at all: it depends on the subtotal's value, not merely on its
    /// residue, so a residue-only search cannot express it. Two bump sets
    /// reaching the same delta cost the same; the tie is broken by comparing
    /// their sorted lists of bumped line numbers and keeping the
    /// lexicographically smallest, a rule decided entirely by this method so a
    /// revision reproduces the same split regardless of any library's own,
    /// unspecified iteration order. That choice propagates safely: with every
    /// count positive, no bump set at a given delta can be a proper prefix of
    /// another, so extending the smallest set at each delta yields the smallest
    /// set at the next.</summary>
    private static IReadOnlyList<int> CheapestBumps(
        IReadOnlyList<int> counts,
        long subtotal,
        int recipients,
        decimal rate,
        long bill
    )
    {
        if (Works(subtotal, recipients, rate, bill))
        {
            return Array.Empty<int>();
        }

        // Generous, and only a bound: the residue alone needs at most
        // (recipients - 1) bumps of the largest count, and the covering test is
        // satisfied within a few cents of that. Exceeding it means an
        // assumption here is wrong, which should stop the bill rather than
        // quietly mis-price it.
        var maxCount = counts.Max();
        var limit = (recipients + 64) * maxCount;

        var best = new List<int>?[limit + 1];
        best[0] = new List<int>();

        for (var delta = 0; delta <= limit; delta++)
        {
            if (best[delta] is null)
            {
                continue;
            }

            if (delta > 0 && Works(subtotal + delta, recipients, rate, bill))
            {
                return best[delta]!;
            }

            for (var i = 0; i < counts.Count; i++)
            {
                var next = delta + counts[i];
                if (next > limit)
                {
                    continue;
                }

                var candidate = new List<int>(best[delta]!) { i };
                candidate.Sort();
                if (best[next] is null || IsLexicographicallySmaller(candidate, best[next]!))
                {
                    best[next] = candidate;
                }
            }
        }

        throw new InvalidOperationException(
            $"No combination of cents within {limit} makes a subtotal of {subtotal} "
                + $"divide across {recipients} recipients and still cover the bill."
        );
    }

    /// <summary>What one VAT rate's lines have to collect, in cents, fixed
    /// before a single cent is added. A line entered as a gross has to collect
    /// exactly that gross: measuring it instead by the gross of its rounded net
    /// would charge again for the cent <see cref="VatInclusivePrice.NetOf"/>
    /// may already have added to reach it. Everything else is net-priced, so
    /// what it owes is its net plus that net's VAT.
    ///
    /// A target recomputed from the raised subtotal would climb with every cent
    /// added and never be caught, which is why this is worked out once.</summary>
    private static long BillFor(IReadOnlyList<GraveLine> lines, decimal rate)
    {
        var netPriced = lines.Where(l => l.EnteredGross is null).Sum(NetCents);
        var grossPriced = lines
            .Where(l => l.EnteredGross is not null)
            .Sum(l =>
                (long)decimal.Round(l.EnteredGross!.Value * 100m, 0, MidpointRounding.AwayFromZero)
                * l.Count
            );

        return netPriced + VatCents(netPriced, rate) + grossPriced;
    }

    /// <summary>Whether a subtotal can be billed as it stands: it divides
    /// evenly across the recipients, and the shares together — each carrying
    /// its own rounded VAT — collect at least <paramref name="bill"/>, the
    /// gross the grave was asked for before any cent was added.</summary>
    private static bool Works(long subtotal, int recipients, decimal rate, long bill)
    {
        if (subtotal % recipients != 0)
        {
            return false;
        }

        var share = subtotal / recipients;
        return recipients * (share + VatCents(share, rate)) >= bill;
    }

    /// <summary>The VAT on a net amount, in whole cents — the same rounding the
    /// document applies, so what is checked here is what is billed.</summary>
    private static long VatCents(long netCents, decimal rate) =>
        (long)decimal.Round(netCents * rate / 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>True if <paramref name="candidate"/> sorts before <paramref
    /// name="current"/> in the usual dictionary order — compared element by
    /// element, with a shorter list that is a prefix of the longer one
    /// counting as smaller.</summary>
    private static bool IsLexicographicallySmaller(
        IReadOnlyList<int> candidate,
        IReadOnlyList<int> current
    )
    {
        var length = Math.Min(candidate.Count, current.Count);
        for (var i = 0; i < length; i++)
        {
            if (candidate[i] != current[i])
            {
                return candidate[i] < current[i];
            }
        }

        return candidate.Count < current.Count;
    }

    private static long NetCents(GraveLine line) =>
        (long)decimal.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
}
