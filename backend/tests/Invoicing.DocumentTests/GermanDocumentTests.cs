using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class GermanDocumentTests
{
    private static InvoiceDocumentData SampleData() =>
        new()
        {
            InvoiceNumber = "2026-0004",
            GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            RecipientName = "Familie Mahr",
            RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
            EndingText = "",
            StampWord = "Kopie",
            Lines = new[]
            {
                new InvoiceDocumentData.LineData(
                    1,
                    "1001",
                    "Grabpflege",
                    2,
                    120.00m,
                    7.0m,
                    240.00m,
                    16.80m
                ),
            },
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(7.0m, 240.00m, 16.80m, 256.80m),
            },
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
    public void Documents_use_german_labels_and_number_format()
    {
        var text = ExtractText(DocumentService.GeneratePdf(SampleData(), isCopy: false));

        Assert.Contains("Rechnung 2026-0004", text);
        Assert.Contains("Bezeichnung", text);
        Assert.Contains("Gesamtbetrag", text);
        Assert.Contains("240,00", text); // German decimal comma
        Assert.Contains("15.01.2026", text);
    }

    [Fact]
    public void The_share_note_is_german()
    {
        Assert.Equal(
            "Diese Rechnung enthält 50 % des Gesamtbetrags.",
            DocumentTexts.ShareText(50m)
        );
    }
}
