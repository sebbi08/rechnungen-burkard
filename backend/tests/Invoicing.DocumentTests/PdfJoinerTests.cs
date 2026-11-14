using System;
using System.IO;
using System.Linq;
using Burkard.Invoicing.Documents;
using QuestPDF.Fluent;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>Joining stored PDFs into one, for printing a half-year's grave
/// invoices in one go: every page as it is, in the order given.</summary>
public class PdfJoinerTests
{
    private static readonly string WorkRoot = Path.Combine(
        Path.GetTempPath(),
        "invoicing-pdf-join"
    );

    /// <summary>A PDF with one page per text, each page showing its text.</summary>
    private static byte[] Pdf(params string[] pageTexts)
    {
        QuestPDF.Settings.License ??= QuestPDF.Infrastructure.LicenseType.Community;
        return Document
            .Create(document =>
            {
                foreach (var text in pageTexts)
                {
                    document.Page(page => page.Content().Text(text));
                }
            })
            .GeneratePdf();
    }

    private static string[] PageTexts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => page.Text.Trim()).ToArray();
    }

    private static int WorkDirectories() =>
        Directory.Exists(WorkRoot) ? Directory.GetDirectories(WorkRoot).Length : 0;

    [Fact]
    public void Join_keeps_every_page_in_order()
    {
        var joined = PdfJoiner.Join([Pdf("A1"), Pdf("B1", "B2"), Pdf("C1")]);

        Assert.Equal(["A1", "B1", "B2", "C1"], PageTexts(joined));
    }

    [Fact]
    public void Join_of_one_pdf_is_that_pdf()
    {
        var joined = PdfJoiner.Join([Pdf("A1")]);

        Assert.Equal(["A1"], PageTexts(joined));
    }

    [Fact]
    public void Join_leaves_no_temp_files()
    {
        var before = WorkDirectories();

        PdfJoiner.Join([Pdf("A1"), Pdf("B1")]);
        Assert.Equal(before, WorkDirectories());

        Assert.ThrowsAny<Exception>(() => PdfJoiner.Join([Pdf("A1"), "no pdf"u8.ToArray()]));
        Assert.Equal(before, WorkDirectories());
    }

    [Fact]
    public void Join_of_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PdfJoiner.Join([]));
    }
}
