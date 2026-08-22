using System;
using Burkard.Invoicing.Documents;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class DeterminismTests
{
    [Fact]
    public void Same_input_produces_byte_identical_pdf()
    {
        var data = new InvoiceDocumentData
        {
            InvoiceNumber = "2026-0003",
            GeneratedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            RecipientName = "Test",
            RecipientAddress = "Testweg 1",
            EndingText = "Danke",
            StampWord = "Kopie",
            Lines = new[] { new InvoiceDocumentData.LineData(1, "A", "Leistung", 1, 5.00m, 7.0m, 5.00m, 0.35m) },
            VatSummaries = new[] { new InvoiceDocumentData.VatSummary(7.0m, 5.00m, 0.35m, 5.35m) },
            NetTotal = 5.00m,
            TotalVat = 0.35m,
            GrossTotal = 5.35m,
        };

        var first = DocumentService.GeneratePdf(data, isCopy: true);
        var second = DocumentService.GeneratePdf(data, isCopy: true);

        Assert.Equal(first, second);
    }
}
