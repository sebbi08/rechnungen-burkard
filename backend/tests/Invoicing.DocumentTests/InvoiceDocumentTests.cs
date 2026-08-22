using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class InvoiceDocumentTests
{
    private static InvoiceDocumentData SampleData() => new()
    {
        InvoiceNumber = "2026-0001",
        GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        RecipientName = "Familie Mahr",
        RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
        EndingText = "Vielen Dank!\nBitte überweisen Sie den Betrag.",
        StampWord = "Kopie",
        Lines = new[]
        {
            new InvoiceDocumentData.LineData(1, "1001", "Grabpflege", 2, 120.00m, 7.0m, 240.00m, 16.80m),
        },
        VatSummaries = new[] { new InvoiceDocumentData.VatSummary(7.0m, 240.00m, 16.80m, 256.80m) },
        NetTotal = 240.00m,
        TotalVat = 16.80m,
        GrossTotal = 256.80m,
    };

    private static string ExtractText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void Normal_invoice_contains_number_lines_totals_and_ending_text()
    {
        var pdf = DocumentService.GeneratePdf(SampleData(), isCopy: false);
        var text = ExtractText(pdf);

        Assert.Contains("2026-0001", text);
        Assert.Contains("Familie Mahr", text);
        Assert.Contains("Musterstraße 1", text);
        Assert.Contains("Grabpflege", text);
        Assert.Contains("240,00", text);      // line total, German number format
        Assert.Contains("256,80", text);      // gross total
        Assert.Contains("Vielen Dank!", text);
        Assert.Contains("Bitte überweisen Sie den Betrag.", text);
        Assert.DoesNotContain("Kopie", text); // normal has no stamp
    }

    [Fact]
    public void Share_text_is_printed_above_the_lines()
    {
        var data = SampleData();
        data.ShareText = "Diese Rechnung enthält 50 % des Gesamtbetrags.";
        var pdf = DocumentService.GeneratePdf(data, isCopy: false);
        var text = ExtractText(pdf);

        Assert.Contains("Diese Rechnung enthält 50 % des Gesamtbetrags.", text);
        Assert.True(text.IndexOf("50 % des Gesamtbetrags", StringComparison.Ordinal)
                    < text.IndexOf("Grabpflege", StringComparison.Ordinal));
    }
}
