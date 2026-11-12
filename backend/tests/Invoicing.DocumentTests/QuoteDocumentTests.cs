using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class QuoteDocumentTests
{
    private static InvoiceDocumentData QuoteData(int lineCount = 1)
    {
        var lines = Enumerable
            .Range(1, lineCount)
            .Select(i => new InvoiceDocumentData.LineData(
                i,
                $"{1000 + i}",
                $"Grabpflege Position {i}",
                1,
                10.00m,
                19.0m,
                10.00m,
                1.90m
            ))
            .ToArray();
        var net = lineCount * 10.00m;
        var vat = lineCount * 1.90m;

        return new InvoiceDocumentData
        {
            InvoiceNumber = "A2026-0001",
            IsQuote = true,
            GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            RecipientName = "Familie Mahr",
            RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
            EndingText = "Vielen Dank!\nBitte überweisen Sie den Betrag.",
            Lines = lines,
            VatSummaries = new[] { new InvoiceDocumentData.VatSummary(19.0m, net, vat, net + vat) },
            NetTotal = net,
            TotalVat = vat,
            GrossTotal = net + vat,
        };
    }

    [Fact]
    public void A_quote_is_headed_Angebot()
    {
        var pdf = DocumentService.GeneratePdf(QuoteData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var text = document.GetPage(1).Text;
        Assert.Contains("**** ANGEBOT ****", text);
        Assert.Contains("A2026-0001", text);
        Assert.DoesNotContain("RECHNUNG", text);
    }

    [Fact]
    public void A_quote_names_itself_Angebot_in_its_metadata()
    {
        var pdf = DocumentService.GeneratePdf(QuoteData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        Assert.Equal("Angebot A2026-0001", document.Information.Title);
    }

    [Fact]
    public void A_quote_keeps_the_ending_text_and_the_totals()
    {
        var pdf = DocumentService.GeneratePdf(QuoteData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var text = document.GetPage(1).Text;
        Assert.Contains("Vielen Dank!", text);
        Assert.Contains("Bitte überweisen Sie den Betrag.", text);
        Assert.Contains("Bruttobetrag", text);
        Assert.Contains("11,90", text);
    }

    [Fact]
    public void Page_two_of_a_long_quote_says_Angebot()
    {
        var pdf = DocumentService.GeneratePdf(QuoteData(40), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages > 1);
        var second = document.GetPage(2).Text;
        Assert.Contains("Angebot A2026-0001", second);
        Assert.DoesNotContain("Rechnung A2026-0001", second);
        Assert.Contains("Inhaber: Guido Burkard", second);
    }
}
