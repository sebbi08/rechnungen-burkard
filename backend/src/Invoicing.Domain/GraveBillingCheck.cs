namespace Burkard.Invoicing.Domain;

/// <summary>A raised price on one line of a block, so that the line's VAT rate
/// splits evenly. <paramref name="EnteredPrice"/> is what the block billed before
/// (prorated where the block is), in the unit the price was entered in.</summary>
public sealed record BillingCorrection(
    int LinkIndex,
    long ItemId,
    string ItemNumber,
    decimal VatRate,
    decimal EnteredPrice,
    decimal CorrectedPrice
);

/// <summary>What identifies a correction when stored corrections are compared
/// with freshly computed ones.</summary>
public sealed record CorrectionKey(
    BillingExampleKind Kind,
    int? Year,
    int Half,
    decimal VatRate,
    long ItemId,
    decimal CorrectedPrice
);

/// <summary>One invoice of a grave's term, what it bills and what stops it
/// splitting evenly.</summary>
public sealed record BillingExampleResult(
    BillingExample Example,
    IReadOnlyList<BilledLine> Lines,
    IReadOnlyList<GraveShareProblem> Problems,
    IReadOnlyList<BillingCorrection> Corrections
)
{
    public bool Ok => Problems.Count == 0;

    public IReadOnlyList<GraveLine> GraveLines => Lines.Select(l => l.Line).ToList();
}

/// <summary>Every invoice of a grave's term, with the corrections each needs.</summary>
public sealed record GraveBillingEvaluation(IReadOnlyList<BillingExampleResult> Invoices)
{
    public bool Ok => Invoices.All(i => i.Ok);

    public BillingExampleResult? FirstFailing => Invoices.FirstOrDefault(i => !i.Ok);

    public IReadOnlyList<CorrectionKey> CorrectionKeys =>
        Invoices
            .SelectMany(invoice =>
                invoice.Corrections.Select(c => new CorrectionKey(
                    invoice.Example.Kind,
                    invoice.Example.Period.Year,
                    invoice.Example.Period.Half,
                    c.VatRate,
                    c.ItemId,
                    c.CorrectedPrice
                ))
            )
            .ToList();
}

/// <summary>Whether a grave can be billed as configured: each invoice its term
/// produces has to split evenly on its own, since the first and last half-year
/// bill different amounts than the full ones.</summary>
public static class GraveBillingCheck
{
    /// <summary>How far, in cents, a correction may raise a line.</summary>
    public const int CorrectionReachCents = 100;

    /// <summary>Every invoice of the term, each block corrected per VAT rate
    /// where it does not split evenly. An invoice still has problems only when
    /// no line of a rate can fix it within <see cref="CorrectionReachCents"/>.</summary>
    public static GraveBillingEvaluation Evaluate(
        IReadOnlyList<BillableLink> links,
        DateOnly? start,
        DateOnly? end,
        int recipients
    )
    {
        var invoices = HalfYearBilling
            .Examples(start, end)
            .Select(example =>
            {
                var (lines, corrections) = Correct(
                    links,
                    HalfYearBilling.Bill(links, example.Period),
                    recipients
                );
                var problems =
                    lines.Count == 0
                        ? []
                        : GraveShare.Problems(lines.Select(b => b.Line).ToList(), recipients);
                return new BillingExampleResult(example, lines, problems, corrections);
            })
            .ToList();
        return new GraveBillingEvaluation(invoices);
    }

    private static (List<BilledLine> Lines, List<BillingCorrection> Corrections) Correct(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BilledLine> billed,
        int recipients
    )
    {
        var lines = billed.ToList();
        var corrections = new List<BillingCorrection>();
        foreach (var rate in lines.Select(b => b.Line.VatRate).Distinct().OrderBy(r => r))
        {
            bool Even() =>
                GraveShare.IsEven(
                    lines.Where(b => b.Line.VatRate == rate).Select(b => b.Line).ToList(),
                    recipients
                );

            if (Even())
            {
                continue;
            }

            // all-year lines first: they are in every block, so the cent sits on
            // the same line wherever it can
            var candidates = lines
                .Select((line, position) => (line, position))
                .Where(x => x.line.Line.VatRate == rate)
                .OrderBy(x => links[x.line.LinkIndex].Quarter is null ? 0 : 1)
                .ThenBy(x => x.position)
                .Select(x => x.position)
                .ToList();

            foreach (var position in candidates)
            {
                var original = lines[position];
                var link = links[original.LinkIndex];
                BilledLine? fixedLine = null;
                for (var cents = 1; cents <= CorrectionReachCents; cents++)
                {
                    lines[position] = HalfYearBilling.BillAt(
                        original.LinkIndex,
                        link,
                        original.Price + cents / 100m,
                        original.Line.Description
                    );
                    if (Even())
                    {
                        fixedLine = lines[position];
                        break;
                    }
                }

                if (fixedLine is not null)
                {
                    corrections.Add(
                        new BillingCorrection(
                            original.LinkIndex,
                            original.Line.ItemId,
                            original.Line.ItemNumber,
                            rate,
                            original.Price,
                            fixedLine.Price
                        )
                    );
                    break;
                }

                lines[position] = original;
            }
        }

        return (lines, corrections);
    }

    /// <summary>The lines of one block with stored corrections applied: the line
    /// of each correction's item and rate is billed at its corrected price.</summary>
    public static IReadOnlyList<BilledLine> ApplyCorrections(
        IReadOnlyList<BillableLink> links,
        IReadOnlyList<BilledLine> billed,
        IEnumerable<(long ItemId, decimal VatRate, decimal CorrectedPrice)> corrections
    )
    {
        var lines = billed.ToList();
        foreach (var (itemId, rate, price) in corrections)
        {
            var position = lines.FindIndex(b => b.Line.ItemId == itemId && b.Line.VatRate == rate);
            if (position < 0)
            {
                continue;
            }

            var line = lines[position];
            lines[position] = HalfYearBilling.BillAt(
                line.LinkIndex,
                links[line.LinkIndex],
                price,
                line.Line.Description
            );
        }

        return lines;
    }

    /// <summary>Whether two sets of corrections are the same, in any order.</summary>
    public static bool SameCorrections(IEnumerable<CorrectionKey> a, IEnumerable<CorrectionKey> b)
    {
        var left = a.ToList();
        var right = b.ToList();
        return left.Count == right.Count && new HashSet<CorrectionKey>(left).SetEquals(right);
    }
}
