using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class DocumentLanguageTests
{
    private static InvoiceDocumentData SampleData(string language) => new()
    {
        InvoiceNumber = "2026-0004",
        GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        RecipientName = "Familie Mahr",
        RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
        EndingText = "",
        StampWord = "Kopie",
        Language = language,
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
    public void German_is_the_default_language()
    {
        var text = ExtractText(DocumentService.GeneratePdf(SampleData("de"), isCopy: false));

        Assert.Contains("Rechnung 2026-0004", text);
        Assert.Contains("Bezeichnung", text);
        Assert.Contains("Gesamtbetrag", text);
        Assert.Contains("240,00", text);   // German decimal comma
        Assert.Contains("15.01.2026", text);
    }

    [Fact]
    public void An_english_invoice_uses_english_labels_and_number_format()
    {
        var text = ExtractText(DocumentService.GeneratePdf(SampleData("en"), isCopy: false));

        Assert.Contains("Invoice 2026-0004", text);
        Assert.Contains("Description", text);
        Assert.Contains("Amount due", text);
        Assert.Contains("240.00", text);   // English decimal point
        Assert.DoesNotContain("Gesamtbetrag", text);
    }

    [Fact]
    public void The_share_note_follows_the_invoice_language()
    {
        Assert.Contains("50 % des Gesamtbetrags", DocumentTexts.For("de").ShareText(50m));
        Assert.Contains("50 % of the total amount", DocumentTexts.For("en").ShareText(50m));
    }

    [Fact]
    public void An_unknown_language_falls_back_to_the_default()
    {
        var text = ExtractText(DocumentService.GeneratePdf(SampleData("fr"), isCopy: false));

        Assert.Contains("Rechnung 2026-0004", text);
    }
}
