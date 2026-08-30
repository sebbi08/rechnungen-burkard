using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class CopyStampTests
{
    // reuse a multi-page sample: many lines force > 1 page
    private static InvoiceDocumentData MultiPageData() =>
        new()
        {
            InvoiceNumber = "2026-0002",
            GeneratedAt = new System.DateTime(2026, 2, 1, 0, 0, 0, System.DateTimeKind.Utc),
            RecipientName = "Anna Beispiel",
            RecipientAddress = "Hauptstraße 1\n99999 Musterdorf",
            EndingText = "",
            StampWord = "Kopie",
            Lines = Enumerable
                .Range(1, 80)
                .Select(i => new InvoiceDocumentData.LineData(
                    i,
                    $"I-{i}",
                    $"Leistung {i}",
                    1,
                    10.00m,
                    19.0m,
                    10.00m,
                    1.90m
                ))
                .ToArray(),
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(19.0m, 800.00m, 152.00m, 952.00m),
            },
            NetTotal = 800.00m,
            TotalVat = 152.00m,
            GrossTotal = 952.00m,
        };

    [Fact]
    public void Copy_stamp_word_appears_on_every_page()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: true);
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);

        var pages = document.GetPages().ToList();
        Assert.True(pages.Count > 1, "sample should produce multiple pages");
        foreach (var page in pages)
        {
            Assert.Contains("Kopie", page.Text);
        }
    }

    [Fact]
    public void Very_long_stamp_word_still_generates_and_appears_on_every_page()
    {
        var data = MultiPageData();
        data.StampWord = new string('K', 80);

        var pdf = DocumentService.GeneratePdf(data, isCopy: true);
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);

        foreach (var page in document.GetPages())
        {
            Assert.Contains('K', page.Text);
        }
    }
}
