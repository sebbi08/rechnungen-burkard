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
        // a root of its own: other test assemblies join in the shared one at
        // the same time, and their directories would be counted here
        var root = Path.Combine(Path.GetTempPath(), $"pdf-join-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            PdfJoiner.Join([Pdf("A1"), Pdf("B1")], root);
            Assert.Empty(Directory.GetFileSystemEntries(root));

            Assert.ThrowsAny<Exception>(() =>
                PdfJoiner.Join([Pdf("A1"), "no pdf"u8.ToArray()], root)
            );
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Join_of_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PdfJoiner.Join([]));
    }
}
