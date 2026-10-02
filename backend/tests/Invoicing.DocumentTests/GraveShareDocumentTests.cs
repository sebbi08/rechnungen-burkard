using System;
using System.Linq;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>DocumentDataBuilder built directly against an <see cref="Invoice"/>,
/// with no HTTP call and no database — the two branches on
/// <see cref="Invoice.RecipientCount"/> are pure functions of the invoice and its
/// lines. Two of the tests render the PDF as well, because what a rate label
/// is attached to is a property of the page, not of the data.</summary>
public class GraveShareDocumentTests
{
    private static InvoiceLine Line(
        int count,
        decimal unitPrice,
        decimal vatRate,
        int lineNumber
    ) =>
        new()
        {
            ItemNumber = $"M-{lineNumber}",
            Description = $"Item {lineNumber}",
            Count = count,
            UnitPrice = unitPrice,
            VatRate = vatRate,
            LineNumber = lineNumber,
        };

    /// <summary>The same four lines as GraveShareGenerationTests' seed, already
    /// raised by GraveShare.MakeDivisible for 3 recipients (the count-1 Grabstein
    /// line got the one cent that makes the 19 % subtotal divide): Grabpflege
    /// 1 x 100.00 @ 7 %, Blumenschmuck 2 x 24.95 @ 19 %, Grabstein 1 x 47.81
    /// @ 19 %, Giessdienst 5 x 3.70 @ 7 %.</summary>
    private static Invoice GraveInvoice() =>
        new()
        {
            InvoiceNumber = "2026-0001",
            GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            RecipientName = "Empfänger 1",
            RecipientAddress = "Weg 1",
            RecipientCount = 3,
            Lines = new List<InvoiceLine>
            {
                Line(1, 100.00m, 7m, 1),
                Line(2, 24.95m, 19m, 2),
                Line(1, 47.81m, 19m, 3),
                Line(5, 3.70m, 7m, 4),
            },
        };

    private static string ExtractText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (
            var i = haystack.IndexOf(needle, StringComparison.Ordinal);
            i >= 0;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void A_grave_invoice_prints_the_full_lines_and_the_recipients_share()
    {
        var invoice = GraveInvoice();

        var data = new DocumentDataBuilder().Build(invoice);

        // The 100 % figures, printed above the share block, are unchanged by a
        // share being present: 7 % subtotal is 100.00 + 5*3.70 = 118.50, 19 %
        // subtotal is 2*24.95 + 47.81 = 97.71, net total 118.50 + 97.71 = 216.21.
        Assert.Equal(216.21m, data.NetTotal);

        Assert.NotNull(data.Share);
        var share = data.Share!;

        // Each rate's net is exactly its 100 % net divided by 3 recipients — that
        // is what MakeDivisible guarantees, so no rounding happens here: 118.50 / 3
        // = 39.50 and 97.71 / 3 = 32.57.
        Assert.Equal(2, share.VatSummaries.Count);
        var sevenPercent = share.VatSummaries.Single(s => s.Rate == 7m);
        var nineteenPercent = share.VatSummaries.Single(s => s.Rate == 19m);
        Assert.Equal(39.50m, sevenPercent.NetTotal);
        Assert.Equal(32.57m, nineteenPercent.NetTotal);

        // The VAT on each rate is worked out from that share, not from the 100 %
        // figure: round(39.50 * 7 / 100, 2) = round(2.765, 2) = 2.77, and
        // round(32.57 * 19 / 100, 2) = round(6.1883, 2) = 6.19.
        Assert.Equal(2.77m, sevenPercent.VatAmount);
        Assert.Equal(6.19m, nineteenPercent.VatAmount);

        // Share.NetTotal = 39.50 + 32.57 = 72.07, and its gross adds the two VAT
        // amounts on top: 72.07 + 2.77 + 6.19 = 81.03.
        Assert.Equal(72.07m, share.NetTotal);
        Assert.Equal(81.03m, share.GrossTotal);
    }

    [Fact]
    public void A_grave_invoice_generated_before_the_share_renders_exactly_as_before()
    {
        // RecipientCount null means the persisted line is already one recipient's
        // share — a grave invoice generated before this change. 1 x 50.42 @ 19 %:
        // net 50.42, VAT round(50.42 * 19 / 100, 2) = round(9.5798, 2) = 9.58,
        // gross 50.42 + 9.58 = 60.00, exactly as DocumentDataBuilder computed it
        // before the share existed.
        var invoice = new Invoice
        {
            InvoiceNumber = "2026-0002",
            GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            RecipientName = "Empfänger 1",
            RecipientAddress = "Weg 1",
            RecipientCount = null,
            Lines = new List<InvoiceLine> { Line(1, 50.42m, 19m, 1) },
        };

        var data = new DocumentDataBuilder().Build(invoice);

        Assert.Equal(50.42m, data.NetTotal);
        Assert.Equal(60.00m, data.GrossTotal);
        Assert.Null(data.Share);
    }

    [Fact]
    public void A_grave_invoice_bills_the_share_per_rate()
    {
        var data = new DocumentDataBuilder().Build(GraveInvoice());
        var text = ExtractText(DocumentService.GeneratePdf(data, isCopy: false));

        // The line above the VAT block says what the block is a share of: the
        // grave's whole net, and this recipient's part of it.
        Assert.Contains("Nettosumme gesamt 216,21 € – davon Ihr Anteil 33,33 %", text);

        // The block has one column per rate, and the figures in it are the
        // invoiced ones, worked out from the share: 7 % of 39,50 is 2,77 and
        // 19 % of 32,57 is 6,19. The 100 % rows are not printed at all —
        // printing them too would put 8,30 and 2,77 under one 7 % head on a tax
        // document with nothing saying which of them was owed.
        Assert.Equal(1, Occurrences(text, "7,00 %"));
        Assert.Equal(1, Occurrences(text, "19,00 %"));
        Assert.DoesNotContain("8,30", text);
        Assert.DoesNotContain("18,56", text);

        // Each rate's net stands in the block with its VAT. That is the point of
        // printing the share's rows rather than a nicety: the lines above are
        // the whole grave, so without them the recipient's own taxable amount
        // per rate would appear nowhere on the page, and a VAT invoice has to
        // show it.
        Assert.Contains("39,50", text);
        Assert.Contains("2,77", text);
        Assert.Contains("32,57", text);
        Assert.Contains("6,19", text);
        Assert.Contains("81,03", text);
    }

    [Fact]
    public void A_non_grave_invoice_still_prints_its_own_rate_block()
    {
        // The same lines with RecipientCount null — an ordinary invoice. The
        // block bills the whole amount: net and VAT per rate, then the net, VAT
        // and gross totals, with no share line above it.
        var invoice = GraveInvoice();
        invoice.RecipientCount = null;
        var data = new DocumentDataBuilder().Build(invoice);
        var text = ExtractText(DocumentService.GeneratePdf(data, isCopy: false));

        Assert.Contains("118,50", text);
        Assert.Contains("8,30", text);
        Assert.Contains("97,71", text);
        Assert.Contains("18,56", text);
        Assert.Contains("216,21", text);
        Assert.Contains("26,86", text);
        Assert.Contains("243,07", text);
        Assert.DoesNotContain("Nettosumme gesamt", text);
    }

    [Fact]
    public void The_builder_hands_over_the_snapshot_and_the_vat_included_flag()
    {
        var invoice = GraveInvoice();
        invoice.RecipientNumber = "067200";
        invoice.GraveNumber = "1234";
        invoice.GraveSiteName = "Schineller Bäcke";
        invoice.GraveFieldNumber = "27";
        invoice.GravePosition = GravePosition.DoubleOutside;
        invoice.Lines[0].PriceIncludesVat = true;

        var data = new DocumentDataBuilder().Build(invoice);

        Assert.Equal("067200", data.RecipientNumber);
        Assert.Equal("1234", data.GraveNumber);
        Assert.Equal("Schineller Bäcke", data.GraveSiteName);
        Assert.Equal("27", data.GraveFieldNumber);
        Assert.Equal("DoubleOutside", data.GravePosition);
        Assert.True(data.Lines[0].PriceIncludesVat);
        Assert.False(data.Lines[1].PriceIncludesVat);
    }
}
