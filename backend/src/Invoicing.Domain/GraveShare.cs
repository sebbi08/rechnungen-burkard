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
    /// <paramref name="recipients"/>. Equal-cost choices take the lower line
    /// number, so a revision reproduces the same split.</summary>
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

        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var residue, out var cost))
        {
            if (cost > best[residue])
            {
                continue;
            }

            for (var i = 0; i < counts.Count; i++)
            {
                var next = (residue + counts[i]) % recipients;
                var nextCost = cost + counts[i];
                if (nextCost >= best[next])
                {
                    continue;
                }

                best[next] = nextCost;
                path[next] = new List<int>(path[residue]!) { i };
                queue.Enqueue(next, nextCost);
            }
        }

        return path[0]
            ?? throw new InvalidOperationException(
                "No combination of cents makes the subtotal divisible, which the "
                    + "gcd argument says cannot happen."
            );
    }

    private static long NetCents(GraveLine line) =>
        (long)decimal.Round(line.Count * line.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
}
