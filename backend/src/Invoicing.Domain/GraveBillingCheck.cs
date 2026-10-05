namespace Burkard.Invoicing.Domain;

/// <summary>One invoice of a grave's term, what it bills and what stops it
/// splitting evenly.</summary>
public sealed record BillingExampleResult(
    BillingExample Example,
    IReadOnlyList<BilledLine> Lines,
    IReadOnlyList<GraveShareProblem> Problems
)
{
    public bool Ok => Problems.Count == 0;

    public IReadOnlyList<GraveLine> GraveLines => Lines.Select(l => l.Line).ToList();
}

/// <summary>Every invoice of a grave's term, and per link index the nearest
/// prices below and above at which all of them divide.</summary>
public sealed record GraveBillingEvaluation(
    IReadOnlyList<BillingExampleResult> Invoices,
    IReadOnlyDictionary<int, IReadOnlyList<decimal>> Suggestions
)
{
    public bool Ok => Invoices.All(i => i.Ok);

    public BillingExampleResult? FirstFailing => Invoices.FirstOrDefault(i => !i.Ok);
}

/// <summary>Whether a grave can be billed as configured: each invoice its term
/// produces has to split evenly on its own, since the first and last half-year
/// bill different amounts than the full ones.
///
/// A suggestion moves one line's entered price and keeps every other line, and
/// is only offered when it fixes every invoice at once — a price that rescues
/// the full half-years but breaks the first one is no help.</summary>
public static class GraveBillingCheck
{
    /// <summary>How far, in cents, a suggestion may lie from the entered price —
    /// the reach <see cref="GraveShare"/> has always searched.</summary>
    public const int SuggestionReachCents = 400;

    public static GraveBillingEvaluation Evaluate(
        IReadOnlyList<BillableLink> links,
        DateOnly? start,
        DateOnly? end,
        int recipients,
        bool withSuggestions = true
    )
    {
        var examples = HalfYearBilling.Examples(start, end);
        var invoices = examples
            .Select(example =>
            {
                var billed = HalfYearBilling.Bill(links, example.Period);
                var problems =
                    billed.Count == 0
                        ? []
                        : GraveShare.Problems(billed.Select(b => b.Line).ToList(), recipients);
                return new BillingExampleResult(example, billed, problems);
            })
            .ToList();

        var suggestions =
            withSuggestions && invoices.Any(i => !i.Ok)
                ? Suggest(links, examples, invoices, recipients)
                : new Dictionary<int, IReadOnlyList<decimal>>();
        return new GraveBillingEvaluation(invoices, suggestions);
    }

    private static Dictionary<int, IReadOnlyList<decimal>> Suggest(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BillingExample> examples,
        IReadOnlyList<BillingExampleResult> invoices,
        int recipients
    )
    {
        var candidates = invoices
            .Where(i => !i.Ok)
            .SelectMany(i => i.Lines.Select(l => l.LinkIndex))
            .Distinct()
            .OrderBy(index => index);

        var found = new Dictionary<int, IReadOnlyList<decimal>>();
        foreach (var index in candidates)
        {
            var price = links[index].EnteredPrice;
            decimal? below = null;
            decimal? above = null;
            for (
                var cents = 1;
                cents <= SuggestionReachCents && (below is null || above is null);
                cents++
            )
            {
                var step = cents / 100m;
                if (
                    below is null
                    && price - step > 0
                    && AllEven(With(links, index, price - step), examples, recipients)
                )
                {
                    below = price - step;
                }

                if (
                    above is null
                    && AllEven(With(links, index, price + step), examples, recipients)
                )
                {
                    above = price + step;
                }
            }

            var prices = new[] { below, above }
                .Where(p => p is not null)
                .Select(p => p!.Value)
                .ToList();
            if (prices.Count > 0)
            {
                found[index] = prices;
            }
        }

        return found;
    }

    private static bool AllEven(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BillingExample> examples,
        int recipients
    ) =>
        examples.All(example =>
        {
            var lines = HalfYearBilling.Lines(links, example.Period);
            return lines.Count == 0 || GraveShare.IsEven(lines, recipients);
        });

    private static List<BillableLink> With(
        IReadOnlyList<BillableLink> links,
        int index,
        decimal price
    )
    {
        var copy = links.ToList();
        copy[index] = copy[index] with { EnteredPrice = price };
        return copy;
    }
}
