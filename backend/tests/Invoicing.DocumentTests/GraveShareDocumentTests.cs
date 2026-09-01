using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>DocumentDataBuilder built directly against an <see cref="Invoice"/>,
/// with no HTTP call and no database — the two branches on
/// <see cref="Invoice.RecipientCount"/> are pure functions of the invoice and its
/// lines.</summary>
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

    [Fact]
    public void A_grave_invoice_prints_the_full_lines_and_the_recipients_share()
    {
        // The same four lines as GraveShareGenerationTests' seed, already raised by
        // GraveShare.MakeDivisible for 3 recipients (the count-1 Grabstein line got
        // the one cent that makes the 19 % subtotal divide): Grabpflege 1 x 100.00
        // @ 7 %, Blumenschmuck 2 x 24.95 @ 19 %, Grabstein 1 x 47.81 @ 19 %,
        // Giessdienst 5 x 3.70 @ 7 %.
        var invoice = new Invoice
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
}
