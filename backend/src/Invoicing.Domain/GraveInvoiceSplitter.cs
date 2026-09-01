using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>One line of a grave invoice before it is split across the grave's
/// recipients: the grave's count at the grave's price, as if the grave were
/// invoiced to a single recipient.</summary>
public sealed record GraveLine(
    long ItemId,
    string ItemNumber,
    string Description,
    int Count,
    decimal UnitPrice,
    decimal VatRate,
    bool PriceIncludesVat
);

/// <summary>Splits a grave's lines evenly across its recipients, and recovers
/// the grave's lines from the result.</summary>
public static class GraveInvoiceSplitter
{
    /// <summary>The lines of each recipient's invoice. Every line is split on
    /// its own, so each invoice keeps the grave's items in their configured
    /// proportion; splitting the grave total instead would rewrite every unit
    /// price.
    ///
    /// The <em>net</em> is what divides: the fee being shared is a net one, and
    /// each recipient's invoice then works out its own VAT from its own net, so
    /// every document adds up on its own. Dividing the gross instead would force
    /// one recipient's VAT to be whatever made the sum come out — a document that
    /// does not add up. The recipients' gross totals may therefore differ from a
    /// single invoice for the same grave by up to a cent each.</summary>
    public static IReadOnlyList<IReadOnlyList<InvoiceLine>> Split(
        IReadOnlyList<GraveLine> lines,
        int recipients
    )
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var shares = lines
            .Select(l => ShareCalculator.SplitCents(NetCents(l.Count, l.UnitPrice), recipients))
            .ToList();

        var result = new List<IReadOnlyList<InvoiceLine>>(recipients);
        for (var r = 0; r < recipients; r++)
        {
            var forRecipient = new List<InvoiceLine>(lines.Count);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                forRecipient.Add(
                    new InvoiceLine
                    {
                        ItemId = line.ItemId,
                        ItemNumber = line.ItemNumber,
                        Description = line.Description,
                        Count = line.Count,
                        // 3 decimals keep round(count × unitPrice × 100) exactly equal to this
                        // recipient's share, so the line bills what it was given. That holds
                        // below a count of ten: the invoice prints at most three decimals, so
                        // the smallest net step a line can express is count × 0.1 cents, and
                        // storing more would make the printed price stop reproducing the
                        // printed line total.
                        UnitPrice = decimal.Round(
                            shares[i][r] / 100m / line.Count,
                            3,
                            MidpointRounding.AwayFromZero
                        ),
                        VatRate = line.VatRate,
                        // the recipient's line remembers what the grave's line was
                        // entered as, so reopening the edit dialog shows the gross
                        // again rather than this recipient's net share
                        PriceIncludesVat = line.PriceIncludesVat,
                        LineNumber = i + 1,
                        GraveUnitPrice = line.UnitPrice,
                    }
                );
            }

            result.Add(forRecipient);
        }

        return result;
    }

    /// <summary>The grave's lines, recovered from the recipients' invoices by
    /// summing each line's net cents — the exact inverse of <see cref="Split"/>,
    /// because the net is what was divided. It only serves grave invoices
    /// generated before <c>InvoiceLine.GraveUnitPrice</c> was stored; for the
    /// older ones among those, split on the gross, it stays approximate.</summary>
    public static IReadOnlyList<GraveLine> Combine(
        IReadOnlyList<IReadOnlyList<InvoiceLine>> perRecipient
    )
    {
        if (perRecipient.Count == 0)
        {
            throw new ArgumentException(
                "At least one recipient is required.",
                nameof(perRecipient)
            );
        }

        var ordered = perRecipient
            .Select(lines => lines.OrderBy(l => l.LineNumber).ToList())
            .ToList();
        if (ordered.Any(lines => lines.Count != ordered[0].Count))
        {
            throw new ArgumentException(
                "Every recipient must carry the same lines.",
                nameof(perRecipient)
            );
        }

        var result = new List<GraveLine>(ordered[0].Count);
        for (var i = 0; i < ordered[0].Count; i++)
        {
            var template = ordered[0][i];
            var netCents = ordered.Sum(lines => NetCents(lines[i].Count, lines[i].UnitPrice));
            result.Add(
                new GraveLine(
                    template.ItemId ?? 0,
                    template.ItemNumber,
                    template.Description,
                    template.Count,
                    decimal.Round(
                        netCents / 100m / template.Count,
                        3,
                        MidpointRounding.AwayFromZero
                    ),
                    template.VatRate,
                    template.PriceIncludesVat
                )
            );
        }

        return result;
    }

    /// <summary>Net total in cents for one line, rounded the way
    /// DocumentDataBuilder rounds it — so this is what the line actually bills.</summary>
    private static long NetCents(int count, decimal unitPrice) =>
        (long)Math.Round(count * unitPrice * 100m, 0, MidpointRounding.AwayFromZero);
}
