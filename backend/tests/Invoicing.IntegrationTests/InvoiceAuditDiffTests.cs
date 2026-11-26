using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What an invoice chain's Verlauf says a revision changed: lines
/// paired by item, repeats in line order, prices as they were entered.</summary>
public class InvoiceAuditDiffTests
{
    private const string Grabpflege = "1001 Grabpflege";
    private const string Blumen = "2002 Blumenschmuck";

    private static InvoiceLine GrabpflegeLine(
        int count = 1,
        decimal price = 100m,
        string comment = ""
    ) =>
        new()
        {
            ItemId = 11,
            ItemNumber = "1001",
            Description = "Grabpflege",
            Count = count,
            UnitPrice = price,
            VatRate = 19m,
            Comment = comment,
        };

    private static InvoiceLine BlumenLine() =>
        new()
        {
            ItemId = 12,
            ItemNumber = "2002",
            Description = "Blumenschmuck",
            Count = 2,
            UnitPrice = 12.5m,
            VatRate = 19m,
        };

    /// <summary>Same number, recipient and intro on both sides, so a test sees
    /// only the change it makes; line numbers follow the list.</summary>
    private static Invoice Invoice(
        string introText = "Sehr geehrte Damen und Herren,",
        params InvoiceLine[] lines
    )
    {
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i].LineNumber = i + 1;
        }

        return new Invoice
        {
            RootId = 1,
            InvoiceNumber = "2026-0001",
            RecipientNumber = "E-0001",
            RecipientName = "Anna Beispiel",
            IntroText = introText,
            Lines = [.. lines],
        };
    }

    private static Invoice Lines(params InvoiceLine[] lines) =>
        Invoice("Sehr geehrte Damen und Herren,", lines);

    private static IReadOnlyList<AuditChange> Revised(Invoice previous, Invoice revision) =>
        InvoiceAuditDiff.Revised(previous, 100m, revision, 100m);

    [Fact]
    public void An_added_line_is_listed_with_its_values()
    {
        var changes = Revised(Lines(GrabpflegeLine()), Lines(GrabpflegeLine(), BlumenLine()));

        Assert.Equal(
            [
                new AuditChange("lineAdded", Blumen, null, null, "text"),
                new AuditChange("lineCount", Blumen, null, "2", "int"),
                new AuditChange("linePrice", Blumen, null, "12.50", "money"),
            ],
            changes
        );
    }

    [Fact]
    public void A_removed_line_is_listed_by_its_label()
    {
        var changes = Revised(Lines(GrabpflegeLine(), BlumenLine()), Lines(GrabpflegeLine()));

        Assert.Equal([new AuditChange("lineRemoved", Blumen, null, null, "text")], changes);
    }

    [Fact]
    public void A_changed_intro_text_is_a_change()
    {
        var changes = Revised(
            Invoice("Guten Tag,", GrabpflegeLine()),
            Invoice("Sehr geehrte Frau Beispiel,", GrabpflegeLine())
        );

        Assert.Equal(
            [
                new AuditChange(
                    "introText",
                    null,
                    "Guten Tag,",
                    "Sehr geehrte Frau Beispiel,",
                    "text"
                ),
            ],
            changes
        );
    }

    [Fact]
    public void A_changed_line_comment_is_a_change_of_that_line()
    {
        var changes = Revised(
            Lines(GrabpflegeLine(comment: "Frühjahr")),
            Lines(GrabpflegeLine(comment: "Herbst"))
        );

        Assert.Equal(
            [new AuditChange("lineComment", Grabpflege, "Frühjahr", "Herbst", "text")],
            changes
        );
    }

    [Fact]
    public void Including_VAT_reads_as_a_switch_and_the_entered_gross_as_the_price()
    {
        var net = GrabpflegeLine(price: 100m);
        var gross = GrabpflegeLine(price: 100m);
        gross.PriceIncludesVat = true;
        gross.GrossUnitPrice = 119m;

        Assert.Equal(
            [
                new AuditChange("linePrice", Grabpflege, "100.00", "119.00", "money"),
                new AuditChange("lineVatIncluded", Grabpflege, "false", "true", "bool"),
            ],
            Revised(Lines(net), Lines(gross))
        );
        Assert.Equal(
            [
                new AuditChange("linePrice", Grabpflege, "119.00", "100.00", "money"),
                new AuditChange("lineVatIncluded", Grabpflege, "true", "false", "bool"),
            ],
            Revised(Lines(gross), Lines(net))
        );
    }

    [Fact]
    public void A_VAT_included_line_compares_the_entered_gross_not_the_net()
    {
        // the net is derived from the gross and may move by a rounding step
        // while the entered price stays put, or stay put while it moves
        var before = GrabpflegeLine(price: 100m);
        before.PriceIncludesVat = true;
        before.GrossUnitPrice = 119m;
        var after = GrabpflegeLine(price: 100.84m);
        after.PriceIncludesVat = true;
        after.GrossUnitPrice = 120m;

        Assert.Equal(
            [new AuditChange("linePrice", Grabpflege, "119.00", "120.00", "money")],
            Revised(Lines(before), Lines(after))
        );
    }

    [Fact]
    public void Of_three_equal_lines_of_one_item_removing_one_is_one_removal()
    {
        var changes = Revised(
            Lines(GrabpflegeLine(), GrabpflegeLine(), GrabpflegeLine()),
            Lines(GrabpflegeLine(), GrabpflegeLine())
        );

        Assert.Equal([new AuditChange("lineRemoved", Grabpflege, null, null, "text")], changes);
    }

    [Fact]
    public void Repeated_lines_of_one_item_pair_in_line_order()
    {
        var changes = Revised(
            Lines(GrabpflegeLine(count: 1), BlumenLine(), GrabpflegeLine(count: 5)),
            Lines(GrabpflegeLine(count: 2), BlumenLine(), GrabpflegeLine(count: 5))
        );

        Assert.Equal([new AuditChange("lineCount", Grabpflege, "1", "2", "int")], changes);
    }
}
