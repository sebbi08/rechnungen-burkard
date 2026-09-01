namespace Burkard.Invoicing.Domain;

/// <summary>Raises a grave's unit prices by whole cents until every VAT-rate
/// subtotal divides evenly across the recipients, so each of them can be billed
/// the identical share.
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
                    recipients
                )
            )
            {
                var line = result[indices[i]];
                result[indices[i]] = line with { UnitPrice = line.UnitPrice + 0.01m };
            }
        }

        return result;
    }

    /// <summary>Which lines to bump by one cent, fewest cents added first. Each
    /// bump of line i moves the subtotal by counts[i], so this is a shortest
    /// path from the current residue to zero over the residues modulo
    /// <paramref name="recipients"/>. Two combinations can cost the same — e.g.
    /// counts [1, 3] with 4 recipients: one bump of the count-3 line and three
    /// bumps of the count-1 line both cost 3. The tie is broken by comparing
    /// each candidate's sorted list of bumped line numbers and keeping the
    /// lexicographically smallest, a rule decided entirely by this method so a
    /// revision reproduces the same split regardless of any library's own,
    /// unspecified iteration order.</summary>
    private static IReadOnlyList<int> CheapestBumps(
        IReadOnlyList<int> counts,
        long subtotal,
        int recipients
    )
    {
        var start = (int)(((subtotal % recipients) + recipients) % recipients);
        if (start == 0)
        {
            return Array.Empty<int>();
        }

        var best = new long[recipients];
        var path = new List<int>?[recipients];
        Array.Fill(best, long.MaxValue);
        best[start] = 0;
        path[start] = new List<int>();

        // Fixed-point relaxation over the `recipients` residues, rather than a
        // priority-queue search: the graph has only `recipients` nodes, so a
        // full pass is O(recipients * counts.Count), which is small. Relaxing
        // residues and line indices in a fixed ascending order — and breaking
        // cost ties by comparing sorted bump lists rather than by whichever
        // path happens to be found first — makes the result depend only on
        // this code, never on the dequeue order of some collection type. This
        // terminates because every change strictly lowers either the cost, or,
        // at equal cost, the chosen path in lexicographic order, and both are
        // drawn from a finite set of possibilities.
        bool changed;
        do
        {
            changed = false;
            for (var residue = 0; residue < recipients; residue++)
            {
                if (best[residue] == long.MaxValue)
                {
                    continue;
                }

                for (var i = 0; i < counts.Count; i++)
                {
                    var next = (residue + counts[i]) % recipients;
                    var nextCost = best[residue] + counts[i];
                    if (nextCost > best[next])
                    {
                        continue;
                    }

                    var candidate = new List<int>(path[residue]!) { i };
                    candidate.Sort();
                    if (nextCost < best[next] || IsLexicographicallySmaller(candidate, path[next]!))
                    {
                        best[next] = nextCost;
                        path[next] = candidate;
                        changed = true;
                    }
                }
            }
        } while (changed);

        return path[0]
            ?? throw new InvalidOperationException(
                "No combination of cents makes the subtotal divisible, which the "
                    + "gcd argument says cannot happen."
            );
    }

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
