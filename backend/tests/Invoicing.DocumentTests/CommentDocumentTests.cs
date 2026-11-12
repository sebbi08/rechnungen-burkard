using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>A free text above the lines and a comment under a line, both on
/// ordinary invoices and quotes. Each starts where the item's description
/// starts, not under its number, and keeps its line breaks.</summary>
public class CommentDocumentTests
{
    private static InvoiceDocumentData Data() =>
        new()
        {
            InvoiceNumber = "2026-0001",
            GeneratedAt = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            RecipientName = "Familie Mahr",
            RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
            EndingText = "Zahlbar sofort.",
            StampWord = "Kopie",
            IntroText = "Vorspann erste Zeile\nVorspann zweite Zeile",
            Lines = new[]
            {
                new InvoiceDocumentData.LineData(
                    1,
                    "1001",
                    "Rasenmähen",
                    2,
                    50.00m,
                    19m,
                    100.00m,
                    19.00m,
                    Comment: "Anmerkung erste Zeile\nAnmerkung zweite Zeile"
                ),
                new InvoiceDocumentData.LineData(
                    2,
                    "1002",
                    "Heckenschnitt",
                    1,
                    80.00m,
                    19m,
                    80.00m,
                    15.20m
                ),
            },
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(19m, 180.00m, 34.20m, 214.20m),
            },
            NetTotal = 180.00m,
            TotalVat = 34.20m,
            GrossTotal = 214.20m,
        };

    private static Page FirstPage(InvoiceDocumentData data, out PdfDocument document)
    {
        document = PdfDocument.Open(DocumentService.GeneratePdf(data, isCopy: false));
        return document.GetPage(1);
    }

    private static Word Word(Page page, string text) => page.GetWords().Single(w => w.Text == text);

    [Fact]
    public void The_free_text_starts_with_the_description_above_the_first_line()
    {
        var page = FirstPage(Data(), out var document);
        using (document)
        {
            var description = Word(page, "Rasenmähen");
            var first = page.GetWords().First(w => w.Text == "Vorspann");
            var heading = Word(page, "Leistungsbezeichnung");

            Assert.Contains("Vorspann erste Zeile", page.Text);
            Assert.Contains("Vorspann zweite Zeile", page.Text);
            Assert.Equal(description.BoundingBox.Left, first.BoundingBox.Left, 0.5);
            // PDF coordinates grow upwards: below the column heads, above the line
            Assert.True(first.BoundingBox.Top < heading.BoundingBox.Bottom);
            Assert.True(first.BoundingBox.Bottom > description.BoundingBox.Top);
        }
    }

    [Fact]
    public void The_free_text_keeps_its_line_breaks()
    {
        var page = FirstPage(Data(), out var document);
        using (document)
        {
            var words = page.GetWords().Where(w => w.Text == "Vorspann").ToList();

            Assert.Equal(2, words.Count);
            Assert.Equal(words[0].BoundingBox.Left, words[1].BoundingBox.Left, 0.5);
            Assert.True(words[1].BoundingBox.Top < words[0].BoundingBox.Bottom);
        }
    }

    [Fact]
    public void A_comment_starts_with_the_description_under_its_line()
    {
        var page = FirstPage(Data(), out var document);
        using (document)
        {
            var description = Word(page, "Rasenmähen");
            var next = Word(page, "Heckenschnitt");
            var comments = page.GetWords().Where(w => w.Text == "Anmerkung").ToList();

            Assert.Contains("Anmerkung erste Zeile", page.Text);
            Assert.Contains("Anmerkung zweite Zeile", page.Text);
            Assert.Equal(2, comments.Count);
            foreach (var comment in comments)
            {
                Assert.Equal(description.BoundingBox.Left, comment.BoundingBox.Left, 0.5);
                Assert.True(comment.BoundingBox.Top < description.BoundingBox.Bottom);
                Assert.True(comment.BoundingBox.Bottom > next.BoundingBox.Top);
            }
            Assert.True(comments[1].BoundingBox.Top < comments[0].BoundingBox.Bottom);
        }
    }

    [Fact]
    public void A_comment_is_set_smaller_than_the_description()
    {
        var page = FirstPage(Data(), out var document);
        using (document)
        {
            var description = Word(page, "Rasenmähen").Letters[0].PointSize;
            var comment = page.GetWords().First(w => w.Text == "Anmerkung").Letters[0].PointSize;

            Assert.True(comment < description);
        }
    }

    [Fact]
    public void A_comment_stays_in_the_description_column()
    {
        var data = Data();
        var long_ = string.Join(" ", Enumerable.Repeat("Wort", 60));
        data.Lines = new[] { data.Lines[0] with { Comment = long_ }, data.Lines[1] };
        var page = FirstPage(data, out var document);
        using (document)
        {
            var count = page.GetWords().First(w => w.Text == "Menge");
            var rightmost = page.GetWords()
                .Where(w => w.Text == "Wort")
                .Max(w => w.BoundingBox.Right);

            Assert.True(rightmost < count.BoundingBox.Left);
        }
    }

    [Fact]
    public void Without_texts_nothing_extra_is_printed()
    {
        var data = Data();
        data.IntroText = "";
        data.Lines = data.Lines.Select(l => l with { Comment = "" }).ToArray();
        var page = FirstPage(data, out var document);
        using (document)
        {
            Assert.DoesNotContain("Vorspann", page.Text);
            Assert.DoesNotContain("Anmerkung", page.Text);
        }
    }

    [Fact]
    public void A_quote_prints_both_texts_too()
    {
        var data = Data();
        data.IsQuote = true;
        data.StampWord = "";
        var page = FirstPage(data, out var document);
        using (document)
        {
            Assert.Contains("Vorspann erste Zeile", page.Text);
            Assert.Contains("Anmerkung erste Zeile", page.Text);
        }
    }
}
