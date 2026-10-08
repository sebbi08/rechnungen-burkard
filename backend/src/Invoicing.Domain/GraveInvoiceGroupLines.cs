using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Domain;

/// <summary>The grave's lines behind a group of grave invoices -- one grave, one
/// half-year, every recipient's latest revision -- as the edit dialog opens on
/// them and as a revision takes them back. One rule in one place, so what the
/// dialog shows and what a revision accepts as "left as shown" cannot
/// disagree.</summary>
public static class GraveInvoiceGroupLines
{
    /// <summary>False when the siblings do not carry the same number of lines.
    /// RecipientCount and GraveUnitPrice are only uniform across the group
    /// because generation (or a grave-aware revision) writes every recipient's
    /// invoice in one batch. A sibling revised on its own through the ordinary
    /// CreateRevision path never sets them and can also gain or lose lines,
    /// which would make <see cref="GraveInvoiceSplitter.Combine"/> throw -- so
    /// a line-count mismatch is reported instead of risking a wrong or crashing
    /// reconstruction.</summary>
    public static bool CarrySameLines(IReadOnlyList<Invoice> siblings) =>
        siblings.Select(s => s.Lines.Count).Distinct().Count() <= 1;

    /// <summary>The grave's lines as invoiced; call only once
    /// <see cref="CarrySameLines"/> holds. Three branches, newest first:
    ///
    /// Since the share feature, a grave invoice carries the grave's own lines
    /// at 100 % and is marked by <see cref="Invoice.RecipientCount"/>, so the
    /// first sibling's lines already are the grave's lines — read directly, no
    /// reconstruction needed. Trusted only when every sibling carries the
    /// column, not just the first, for the same reason GraveUnitPrice is: a
    /// sibling revised on its own through the ordinary CreateRevision path never
    /// sets it, and reading the first sibling's lines as the grave's when
    /// another sibling still holds a share would bill the share as the whole.
    ///
    /// Invoices generated before that, but after InvoiceLine.GraveUnitPrice
    /// existed, stored one recipient's share and remembered the grave's
    /// pre-split price on that column — again only when every sibling has it.
    ///
    /// Anything older still, or a group missing GraveUnitPrice on some sibling,
    /// falls back to <see cref="GraveInvoiceSplitter.Combine"/>, which
    /// reconstructs the grave's lines from the recipients' shares — a legacy,
    /// best-effort path.</summary>
    public static IReadOnlyList<GraveLine> AsInvoiced(IReadOnlyList<Invoice> siblings)
    {
        var orderedLines = siblings
            .Select(s => (IReadOnlyList<InvoiceLine>)s.Lines.OrderBy(l => l.LineNumber).ToList())
            .ToList();
        var reference = orderedLines[0];
        if (siblings.All(s => s.RecipientCount is not null))
        {
            return reference
                .Select(l => new GraveLine(
                    l.ItemId ?? 0,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.UnitPrice,
                    l.VatRate,
                    l.PriceIncludesVat
                ))
                .ToList();
        }

        if (orderedLines.All(lines => lines.All(l => l.GraveUnitPrice is not null)))
        {
            return reference
                .Select(l => new GraveLine(
                    l.ItemId ?? 0,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.GraveUnitPrice!.Value,
                    l.VatRate,
                    l.PriceIncludesVat
                ))
                .ToList();
        }

        return GraveInvoiceSplitter.Combine(orderedLines);
    }
}
