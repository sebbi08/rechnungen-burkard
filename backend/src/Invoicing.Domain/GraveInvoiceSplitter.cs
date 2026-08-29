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
    decimal VatRate);

/// <summary>Splits a grave's lines evenly across its recipients, and recovers
/// the grave's lines from the result.</summary>
public static class GraveInvoiceSplitter
{
    /// <summary>The lines of each recipient's invoice. Every line is split on
    /// its own, so each invoice keeps the grave's items in their configured
    /// proportion; splitting the grave total instead would rewrite every unit
    /// price.</summary>
    public static IReadOnlyList<IReadOnlyList<InvoiceLine>> Split(
        IReadOnlyList<GraveLine> lines, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var shares = lines.Select(l => ShareCalculator.SplitCents(GrossCents(l.Count, l.UnitPrice, l.VatRate), recipients)).ToList();

        var result = new List<IReadOnlyList<InvoiceLine>>(recipients);
        for (var r = 0; r < recipients; r++)
        {
            var forRecipient = new List<InvoiceLine>(lines.Count);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var netCents = NetCents(shares[i][r], line.VatRate);
                forRecipient.Add(new InvoiceLine
                {
                    ItemId = line.ItemId,
                    ItemNumber = line.ItemNumber,
                    Description = line.Description,
                    Count = line.Count,
                    // 3 decimals keep round(count × unitPrice × 100) exactly equal to the
                    // derived net for realistic counts, so the persisted line gross stays exact.
                    UnitPrice = decimal.Round(netCents / 100m / line.Count, 3, MidpointRounding.AwayFromZero),
                    VatRate = line.VatRate,
                    LineNumber = i + 1,
                    GraveUnitPrice = line.UnitPrice,
                });
            }

            result.Add(forRecipient);
        }

        return result;
    }

    /// <summary>The grave's lines, recovered from the recipients' invoices by
    /// summing each line's gross cents. Best-effort: for amounts too small to
    /// round cleanly it can land a cent or two off, which is why it only serves
    /// grave invoices generated before <c>InvoiceLine.GraveUnitPrice</c>.</summary>
    public static IReadOnlyList<GraveLine> Combine(
        IReadOnlyList<IReadOnlyList<InvoiceLine>> perRecipient)
    {
        if (perRecipient.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(perRecipient));
        }

        var ordered = perRecipient.Select(lines => lines.OrderBy(l => l.LineNumber).ToList()).ToList();
        if (ordered.Any(lines => lines.Count != ordered[0].Count))
        {
            throw new ArgumentException("Every recipient must carry the same lines.", nameof(perRecipient));
        }

        var result = new List<GraveLine>(ordered[0].Count);
        for (var i = 0; i < ordered[0].Count; i++)
        {
            var template = ordered[0][i];
            var grossCents = ordered.Sum(lines => GrossCents(lines[i].Count, lines[i].UnitPrice, lines[i].VatRate));
            var netCents = NetCents(grossCents, template.VatRate);
            result.Add(new GraveLine(
                template.ItemId ?? 0,
                template.ItemNumber,
                template.Description,
                template.Count,
                decimal.Round(netCents / 100m / template.Count, 3, MidpointRounding.AwayFromZero),
                template.VatRate));
        }

        return result;
    }

    /// <summary>Gross total in cents for one line: round(net cents × (100 + rate) / 100).</summary>
    private static long GrossCents(int count, decimal unitPrice, decimal vatRate)
    {
        var netCents = (long)Math.Round(count * unitPrice * 100m, 0, MidpointRounding.AwayFromZero);
        return (long)Math.Round(netCents * (100m + vatRate) / 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static long NetCents(long grossCents, decimal vatRate) =>
        (long)Math.Round(grossCents * 100m / (100m + vatRate), 0, MidpointRounding.AwayFromZero);
}
