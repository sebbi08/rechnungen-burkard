using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>One line of a grave's bill: the grave's count at the grave's price,
/// billed to every recipient in full. It is what generation and revision hand to
/// <see cref="GraveShare"/>, and what the edit dialog opens on;
/// the recipient's share of it is worked out when the document is built, not by
/// dividing the line.</summary>
public sealed record GraveLine(
    long ItemId,
    string ItemNumber,
    string Description,
    int Count,
    decimal UnitPrice,
    decimal VatRate
);

/// <summary>Recovers a grave's lines from its recipients' invoices. Nothing
/// splits any more — the name is historical, kept so the file's history stays
/// readable. <c>Split</c> is gone along with <c>ShareCalculator</c>, and what
/// remains is a legacy reconstruction path: it only serves grave invoices
/// generated before this feature, when each recipient's invoice stored their own
/// share of the grave's lines rather than the grave's lines in full.
///
/// Nothing written since then reaches it. New invoices carry the grave's lines
/// directly and are marked by <c>Invoice.RecipientCount</c>, so
/// <c>GraveInvoiceGroupController</c> reads them rather than reconstructing
/// them.</summary>
public static class GraveInvoiceSplitter
{
    /// <summary>The grave's lines, recovered from the recipients' invoices by
    /// summing each line's net cents — the exact inverse of the even split those
    /// invoices were once created with, because the net is what was divided. It
    /// only serves grave invoices generated before <c>InvoiceLine.GraveUnitPrice</c>
    /// was stored; for the older ones among those, split on the gross, it stays
    /// approximate.</summary>
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
                    template.VatRate
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
