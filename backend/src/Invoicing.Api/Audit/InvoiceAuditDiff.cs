using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>What an invoice chain's Verlauf records about an invoice: all of it
/// when it is created, and against its predecessor when it is revised. Lines
/// are paired by item (by item number on a line without one), repeats in line
/// order, so a changed count reads "count 1 → 3" rather than "removed, added".
/// Totals are the gross the invoice bills — the recipient's share on a grave
/// invoice — which the caller works out (<c>DocumentDataBuilder.BilledGross</c>).</summary>
public static class InvoiceAuditDiff
{
    public static IReadOnlyList<AuditChange> Created(Invoice invoice, decimal gross)
    {
        var changes = new List<AuditChange>();
        AddField(changes, "recipient", null, RecipientLabel(invoice), "text");
        AddField(changes, "introText", null, invoice.IntroText, "text");
        foreach (var line in Ordered(invoice))
        {
            AddLine(changes, line);
        }

        AddField(changes, "total", null, AuditValues.Money(gross), "money");
        return changes;
    }

    public static IReadOnlyList<AuditChange> Revised(
        Invoice previous,
        decimal previousGross,
        Invoice revision,
        decimal revisionGross
    )
    {
        var changes = new List<AuditChange>();
        AddField(changes, "invoiceNumber", previous.InvoiceNumber, revision.InvoiceNumber, "text");
        AddField(changes, "recipient", RecipientLabel(previous), RecipientLabel(revision), "text");
        AddField(changes, "introText", previous.IntroText, revision.IntroText, "text");

        var unpaired = Ordered(previous).ToList();
        foreach (var line in Ordered(revision))
        {
            var was = unpaired.FirstOrDefault(l => LineKey(l) == LineKey(line));
            if (was is null)
            {
                AddLine(changes, line);
                continue;
            }

            unpaired.Remove(was);
            var label = LineLabel(line);
            AddField(
                changes,
                "lineCount",
                AuditValues.Int(was.Count),
                AuditValues.Int(line.Count),
                "int",
                label
            );
            AddField(
                changes,
                "linePrice",
                AuditValues.Money(EnteredPrice(was)),
                AuditValues.Money(EnteredPrice(line)),
                "money",
                label
            );
            AddField(
                changes,
                "lineVatIncluded",
                AuditValues.Bool(was.PriceIncludesVat),
                AuditValues.Bool(line.PriceIncludesVat),
                "bool",
                label
            );
            AddField(changes, "lineComment", was.Comment, line.Comment, "text", label);
        }

        foreach (var line in unpaired)
        {
            changes.Add(new AuditChange("lineRemoved", LineLabel(line), null, null, "text"));
        }

        AddField(
            changes,
            "total",
            AuditValues.Money(previousGross),
            AuditValues.Money(revisionGross),
            "money"
        );
        return changes;
    }

    /// <summary>A grave entry's link to one of its invoices.</summary>
    public static AuditChange InvoiceLink(Invoice invoice) =>
        new(
            "invoice",
            null,
            null,
            AuditValues.Invoice(invoice.RootId, invoice.InvoiceNumber),
            "invoice"
        );

    /// <summary>"E-0001 Anna Beispiel" from the invoice's own snapshot, or the
    /// name alone when it has no number — as the grave's Verlauf names
    /// recipients.</summary>
    public static string RecipientLabel(Invoice invoice) =>
        string.IsNullOrWhiteSpace(invoice.RecipientNumber)
            ? invoice.RecipientName
            : $"{invoice.RecipientNumber} {invoice.RecipientName}";

    private static IEnumerable<InvoiceLine> Ordered(Invoice invoice) =>
        invoice.Lines.OrderBy(l => l.LineNumber);

    private static void AddLine(List<AuditChange> changes, InvoiceLine line)
    {
        var label = LineLabel(line);
        changes.Add(new AuditChange("lineAdded", label, null, null, "text"));
        AddField(changes, "lineCount", null, AuditValues.Int(line.Count), "int", label);
        AddField(changes, "linePrice", null, AuditValues.Money(EnteredPrice(line)), "money", label);
        if (line.PriceIncludesVat)
        {
            AddField(changes, "lineVatIncluded", null, AuditValues.Bool(true), "bool", label);
        }

        AddField(changes, "lineComment", null, line.Comment, "text", label);
    }

    private static void AddField(
        List<AuditChange> changes,
        string field,
        string? old,
        string? @new,
        string type,
        string? label = null
    )
    {
        old = string.IsNullOrEmpty(old) ? null : old;
        @new = string.IsNullOrEmpty(@new) ? null : @new;
        if (old != @new)
        {
            changes.Add(new AuditChange(field, label, old, @new, type));
        }
    }

    private static string LineKey(InvoiceLine line) =>
        line.ItemId is { } itemId ? $"id:{itemId}" : $"number:{line.ItemNumber}";

    private static string LineLabel(InvoiceLine line) =>
        string.IsNullOrWhiteSpace(line.ItemNumber)
            ? line.Description
            : $"{line.ItemNumber} {line.Description}";

    /// <summary>The price as it was entered: the gross on a VAT-included line
    /// that carries one, otherwise the net.</summary>
    private static decimal EnteredPrice(InvoiceLine line) =>
        line.PriceIncludesVat && line.GrossUnitPrice is { } gross ? gross : line.UnitPrice;
}
