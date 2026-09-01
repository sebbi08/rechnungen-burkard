using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class LetterheadTests
{
    // A4 in PDF points, and the two bands the letterhead occupies itself: the logo
    // block down to ~50mm from the top, the bank details from ~31mm off the bottom.
    private const float PointsPerMillimetre = 72f / 25.4f;
    private const float PageHeight = 297 * PointsPerMillimetre;
    private const float HeaderBandBottom = PageHeight - 50 * PointsPerMillimetre;
    private const float FooterBandTop = 31 * PointsPerMillimetre;

    // enough lines and a long enough ending text that the invoice runs onto a second page
    private static InvoiceDocumentData MultiPageData()
    {
        var lines = Enumerable
            .Range(1, 40)
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

        return new InvoiceDocumentData
        {
            InvoiceNumber = "2026-0042",
            GeneratedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            RecipientName = "Familie Mahr",
            RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
            EndingText = string.Join(
                "\n",
                Enumerable.Repeat(
                    "Vielen Dank für Ihren Auftrag. Bitte überweisen Sie den Betrag "
                        + "innerhalb von 14 Tagen auf eines der unten genannten Konten.",
                    12
                )
            ),
            StampWord = "Kopie",
            Lines = lines,
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(19.0m, 400.00m, 76.00m, 476.00m),
            },
            NetTotal = 400.00m,
            TotalVat = 76.00m,
            GrossTotal = 476.00m,
        };
    }

    // Same as MultiPageData, but with a grave share: the extra summary rows the
    // share block adds (the 100 % net sum, "Ihr Anteil", one VAT line per rate)
    // are exactly the content CLAUDE.md warns can push the content band one
    // millimetre past what page.Background() reserves, which drops the
    // letterhead from every continuation page without complaint.
    private static InvoiceDocumentData MultiPageShareData()
    {
        var data = MultiPageData();
        data.Share = new InvoiceDocumentData.ShareData(
            33.33m,
            133.33m,
            new[] { new InvoiceDocumentData.VatSummary(19.0m, 133.33m, 25.33m, 158.66m) },
            25.33m,
            158.66m
        );
        return data;
    }

    [Fact]
    public void The_share_block_does_not_push_the_letterhead_off_continuation_pages()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageShareData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().ToList();

        Assert.True(pages.Count > 1, $"expected a multi-page invoice, got {pages.Count} page(s)");
        Assert.All(
            pages,
            page =>
            {
                var images = page.GetImages().ToList();
                Assert.Equal(2, images.Count);
                Assert.Contains(images, i => i.BoundingBox.Bottom > HeaderBandBottom);
                Assert.Contains(images, i => i.BoundingBox.Top < FooterBandTop);
            }
        );

        // the share block itself only has to appear once, wherever the summary
        // column lands
        Assert.Contains("33,33", string.Join("\n", pages.Select(p => p.Text)));
    }

    // Only the artwork that cannot be set as text is an image: the rose cluster with
    // the Burkard script it overlaps, and the rose beside the bank details.
    [Fact]
    public void Every_page_carries_the_logo_and_the_footer_rose()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().ToList();

        Assert.True(pages.Count > 1, $"expected a multi-page invoice, got {pages.Count} page(s)");
        Assert.All(
            pages,
            page =>
            {
                var images = page.GetImages().ToList();
                Assert.Equal(2, images.Count);
                Assert.Contains(images, i => i.BoundingBox.Bottom > HeaderBandBottom);
                Assert.Contains(images, i => i.BoundingBox.Top < FooterBandTop);
            }
        );
    }

    [Theory]
    [InlineData("Schnittblumen")] // service list, top left
    [InlineData("Treuhandstelle der Friedhofsgen.")]
    [InlineData("Blumen Burkard")] // sender line
    [InlineData("Inhaber: Guido Burkard")] // footer, first column
    [InlineData("burkard-speyer@t-online.de")]
    [InlineData("IBAN DE38 5455 0010 0380 0922 70")] // footer, Sparkasse
    [InlineData("IBAN DE75 5479 0000 0001 4167 90")] // footer, Volksbank
    [InlineData("Steuer-Nr.: 41/024/7006/5")]
    [InlineData("www.blumen-burkard.de")]
    public void Every_page_sets_the_company_details_as_text(string expected)
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        Assert.All(document.GetPages(), page => Assert.Contains(expected, page.Text));
    }

    [Fact]
    public void Content_stays_clear_of_the_letterhead_bands()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        foreach (var page in document.GetPages())
        {
            // the letterhead sets its own text inside those bands; the invoice must not.
            // The letterhead prints in the company blue and grey, the invoice in black.
            foreach (var letter in page.Letters.Where(IsBlack))
            {
                Assert.True(
                    letter.BoundingBox.Top <= HeaderBandBottom,
                    $"'{letter.Value}' on page {page.Number} overlaps the letterhead logo block"
                );
                Assert.True(
                    letter.BoundingBox.Bottom >= FooterBandTop,
                    $"'{letter.Value}' on page {page.Number} overlaps the letterhead bank details"
                );
            }
        }
    }

    // A DIN lang window envelope reveals the sheet from 45 mm to 90 mm below its top
    // edge. The letterhead's sender line and the recipient both have to land in there.
    private const float WindowTop = PageHeight - 45 * PointsPerMillimetre;
    private const float WindowBottom = PageHeight - 90 * PointsPerMillimetre;

    private static double TopOf(Page page, string word) =>
        page.GetWords().First(w => w.Text == word).BoundingBox.Top;

    [Fact]
    public void Recipient_follows_the_letterhead_sender_line()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);

        // "Blumen" is the letterhead sender line; the footer's web address is one word
        var gap = TopOf(page, "Blumen") - TopOf(page, "Familie");

        Assert.InRange(gap, 0, 12 * PointsPerMillimetre);
    }

    [Fact]
    public void Invoice_number_is_printed_below_the_recipient()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);

        Assert.True(
            TopOf(page, "Rechnung") < TopOf(page, "Musterstadt"),
            "the invoice number must come under the recipient address, not above it"
        );
    }

    [Fact]
    public void Both_addresses_fall_inside_the_din_lang_window()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);

        foreach (var word in new[] { "Blumen", "Familie", "Musterstraße", "Musterstadt" })
        {
            Assert.InRange(TopOf(page, word), WindowBottom, WindowTop);
        }
    }

    // Whatever the address is, the invoice number has to start below the window
    // rather than be sliced in half by its edge.
    [Theory]
    [InlineData("Musterstraße 1\n12345 Musterstadt")]
    [InlineData("z. Hd. Frau Dr. Anneliese Mustermann\nAm Alten Friedhof 17a\n67346 Speyer")]
    public void Invoice_number_clears_the_din_lang_window(string address)
    {
        var data = MultiPageData();
        data.RecipientAddress = address;

        var pdf = DocumentService.GeneratePdf(data, isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);

        Assert.True(
            TopOf(page, "Rechnung") < WindowBottom,
            "the invoice number runs into the envelope window"
        );
    }

    // The address block only earns its space on the sheet that goes in the envelope.
    // Continuation pages just have to say which invoice they belong to.
    [Fact]
    public void Continuation_pages_replace_the_address_block_with_one_line()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().ToList();

        Assert.True(pages.Count > 1, $"expected a multi-page invoice, got {pages.Count} page(s)");
        Assert.Contains("Familie Mahr", pages[0].Text);
        Assert.All(
            pages.Skip(1),
            page =>
            {
                Assert.DoesNotContain("Familie Mahr", page.Text);
                Assert.DoesNotContain("Musterstadt", page.Text);
                Assert.Contains("2026-0042", page.Text);
            }
        );
    }

    // The sheet folds at 105 mm and 210 mm, leaving panels of 105, 105 and 87 mm.
    private const float FirstFold = 105;
    private const float SecondFold = 210;
    private const float SheetHeight = 297;

    // Folding by hand and printer paper feed are both a millimetre or two out, so
    // nothing in the window may sit right on its edge.
    private const float WindowClearance = 4 * PointsPerMillimetre;
    private const float WindowLeft = 20 * PointsPerMillimetre;

    /// <summary>Where the printed fold marks sit, in millimetres from the sheet's top.</summary>
    private static List<double> FoldMarkPositions(Page page) =>
        page
            .Paths.Select(path => path.GetBoundingRectangle())
            .Where(box => box.HasValue)
            .Select(box => box!.Value)
            // the letterhead's own rule reaches well past the text margin
            .Where(box => box.Right < WindowLeft)
            .Select(box => (PageHeight - box.Top) / PointsPerMillimetre)
            .OrderBy(millimetre => millimetre)
            .ToList();

    [Fact]
    public void Window_content_keeps_clear_of_the_window_edges()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);

        var inWindow = page
            .Letters.Where(l =>
                l.BoundingBox.Bottom < WindowTop && l.BoundingBox.Top > WindowBottom
            )
            .ToList();

        Assert.NotEmpty(inWindow);
        Assert.All(
            inWindow,
            letter =>
            {
                Assert.True(
                    letter.BoundingBox.Left >= WindowLeft + WindowClearance,
                    $"'{letter.Value}' sits on the window's left edge"
                );
                Assert.True(
                    letter.BoundingBox.Top <= WindowTop - WindowClearance,
                    $"'{letter.Value}' sits on the window's top edge"
                );
            }
        );
    }

    [Fact]
    public void Fold_marks_are_printed_outside_the_text_at_both_creases()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);

        Assert.All(
            document.GetPages(),
            page =>
            {
                var marks = FoldMarkPositions(page);
                Assert.Equal(2, marks.Count);
                Assert.Equal(FirstFold, marks[0], 1d);
                Assert.Equal(SecondFold, marks[1], 1d);
            }
        );
    }

    // The panel above the first crease has to be the tallest of the three. A shorter
    // one leaves a taller panel standing proud of the sheet's top edge when it folds
    // behind, and then everything shows that far lower in the envelope's window —
    // which is what creases at 87/192 did: an 18 mm overhang, and an address that
    // printed correctly but arrived sunk to the bottom of the window.
    [Fact]
    public void The_panel_carrying_the_address_is_the_tallest_of_the_three()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var marks = FoldMarkPositions(document.GetPage(1));

        var panels = new[] { marks[0], marks[1] - marks[0], SheetHeight - marks[1] };

        Assert.Equal(panels.Max(), marks[0], 1d);
    }

    [Fact]
    public void Neither_crease_runs_through_the_envelope_window()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        using var document = PdfDocument.Open(pdf);

        Assert.All(
            FoldMarkPositions(document.GetPage(1)),
            millimetre =>
                Assert.False(
                    millimetre is > 45 and < 90,
                    $"a crease at {millimetre:0.#} mm runs through the address window"
                )
        );
    }

    private static bool IsBlack(Letter letter)
    {
        var (r, g, b) = letter.Color.ToRGBValues();
        return r < 0.2 && g < 0.2 && b < 0.2;
    }

    // The two images are single static instances shared by every document, so a
    // multi-page invoice embeds them once. Bulk generation runs while download
    // requests are being served, so that sharing has to survive parallel use.
    [Fact]
    public void Concurrent_generations_share_the_letterhead_safely()
    {
        var data = MultiPageData();

        var pdfs = Enumerable
            .Range(0, 16)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => DocumentService.GeneratePdf(data, isCopy: false))
            .ToList();

        Assert.All(pdfs, pdf => Assert.Equal(pdfs[0], pdf));
        Assert.All(
            pdfs,
            pdf =>
            {
                using var document = PdfDocument.Open(pdf);
                Assert.All(document.GetPages(), page => Assert.Equal(2, page.GetImages().Count()));
            }
        );
    }

    // Setting the letterhead as text and embedding only the artwork is the whole point
    // of not using the scan: a page of stationery costs kilobytes, not hundreds of them.
    [Fact]
    public void Letterhead_costs_a_fraction_of_the_full_page_scan()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: false);

        Assert.True(pdf.Length < 150_000, $"two-page invoice grew to {pdf.Length / 1024} kB");
    }
}
