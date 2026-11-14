using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>The layout of the owner's scanned invoices: details beside the
/// address, the grave site above the lines, the VAT rate in the last column,
/// and the VAT block at the foot of the last page.</summary>
public class InvoiceLayoutTests
{
    private const float PointsPerMillimetre = 72f / 25.4f;
    private const float PageHeight = 297 * PointsPerMillimetre;

    // a DIN lang window spans 20 mm to 110 mm from the sheet's left edge
    private const float WindowRight = 110 * PointsPerMillimetre;

    /// <summary>The mockup's grave: two lines at 19 %, one VAT-included line at
    /// 7 % (5 x 10,70 gross = 5 x 10,00 net), two recipients.</summary>
    private static InvoiceDocumentData GraveData() =>
        new()
        {
            InvoiceNumber = "2026-0412",
            GeneratedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
            RecipientName = "Werner Schineller",
            RecipientAddress = "Bartholomäus-Weltz-Straße 4\n67346 Speyer",
            RecipientNumber = "067200",
            GraveNumber = "1234",
            GraveSiteName = "Schineller Bäcke",
            GraveFieldNumber = "27",
            GravePosition = "DoubleOutside",
            EndingText = "Zahlbar sofort nach Erhalt der Rechnung ohne Abzug",
            StampWord = "Kopie",
            Lines = new[]
            {
                new InvoiceDocumentData.LineData(
                    1,
                    "12001",
                    "Herbstbepflanzung",
                    1,
                    24.00m,
                    19m,
                    24.00m,
                    4.56m
                ),
                new InvoiceDocumentData.LineData(
                    2,
                    "12500",
                    "Herbstschale",
                    1,
                    40.00m,
                    19m,
                    40.00m,
                    7.60m
                ),
                new InvoiceDocumentData.LineData(
                    3,
                    "55566",
                    "Pflanzen",
                    5,
                    10.00m,
                    7m,
                    50.00m,
                    3.50m,
                    PriceIncludesVat: true
                ),
            },
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(7m, 50.00m, 3.50m, 53.50m),
                new InvoiceDocumentData.VatSummary(19m, 64.00m, 12.16m, 76.16m),
            },
            NetTotal = 114.00m,
            TotalVat = 15.66m,
            GrossTotal = 129.66m,
            Share = new InvoiceDocumentData.ShareData(
                50m,
                57.00m,
                new[]
                {
                    new InvoiceDocumentData.VatSummary(7m, 25.00m, 1.75m, 26.75m),
                    new InvoiceDocumentData.VatSummary(19m, 32.00m, 6.08m, 38.08m),
                },
                7.83m,
                64.83m
            ),
        };

    private static string Text(InvoiceDocumentData data)
    {
        using var document = PdfDocument.Open(DocumentService.GeneratePdf(data, isCopy: false));
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static Word FirstWord(Page page, string text) =>
        page.GetWords().First(w => w.Text == text);

    [Fact]
    public void The_details_block_names_number_customer_grave_date_and_page()
    {
        var text = Text(GraveData());

        Assert.Contains("**** RECHNUNG ****", text);
        Assert.Contains("NUMMER:", text);
        Assert.Contains("2026-0412", text);
        Assert.Contains("KUNDEN-NR:", text);
        Assert.Contains("067200", text);
        Assert.Contains("LAUFWEG-NR:", text);
        Assert.Contains("1234", text);
        Assert.Contains("DATUM:", text);
        Assert.Contains("02.10.2026", text);
        Assert.Contains("SEITE:", text);
        Assert.Contains("1 von 1", text);
        // the old centred page footer is gone
        Assert.DoesNotContain("Seite 1 von 1", text);
    }

    [Fact]
    public void The_details_block_sits_right_of_the_envelope_window()
    {
        using var document = PdfDocument.Open(
            DocumentService.GeneratePdf(GraveData(), isCopy: false)
        );
        var page = document.GetPage(1);

        Assert.True(FirstWord(page, "NUMMER:").BoundingBox.Left > WindowRight);
        Assert.True(FirstWord(page, "DATUM:").BoundingBox.Left > WindowRight);
    }

    [Fact]
    public void An_ordinary_invoice_has_no_grave_number_and_no_site_lines()
    {
        var data = GraveData();
        data.GraveNumber = null;
        data.GraveSiteName = null;
        data.GraveFieldNumber = null;
        data.GravePosition = null;
        data.Share = null;
        var text = Text(data);

        Assert.Contains("KUNDEN-NR:", text);
        Assert.DoesNotContain("LAUFWEG-NR", text);
        Assert.DoesNotContain("GRABSTÄTTE", text);
        Assert.DoesNotContain("FELD-GRAB-NR", text);
    }

    [Fact]
    public void An_invoice_without_a_recipient_number_leaves_the_line_out()
    {
        var data = GraveData();
        data.RecipientNumber = null;

        Assert.DoesNotContain("KUNDEN-NR", Text(data));
    }

    [Fact]
    public void The_grave_site_is_printed_above_the_lines()
    {
        var text = Text(GraveData());

        Assert.Contains("GRABSTÄTTE: Schineller Bäcke", text);
        Assert.Contains("FELD-GRAB-NR: 27 dGr.a", text);
        Assert.True(
            text.IndexOf("FELD-GRAB-NR", StringComparison.Ordinal)
                < text.IndexOf("Herbstbepflanzung", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("DoubleInside", "dGr.i")]
    [InlineData("SingleInside", "eGr.i")]
    [InlineData("SingleOutside", "eGr.a")]
    [InlineData("UrnInside", "uGr.i")]
    [InlineData("UrnOutside", "uGr.a")]
    [InlineData("Single", "eGr")]
    [InlineData("Double", "dGr")]
    [InlineData("Urn", "uGr")]
    [InlineData("TripleInside", "3fGr.i")]
    [InlineData("QuadrupleOutside", "4fGr.a")]
    [InlineData("Child", "kGr")]
    [InlineData("LawnInside", "Rasengrab innen")]
    [InlineData("LawnUrnOutside", "Rasen-Urne außen")]
    [InlineData("LawnUrn", "Rasen-Urne")]
    [InlineData("TreeOutside", "Baumgrab außen")]
    [InlineData("HonoraryInside", "Ehrengrab innen")]
    [InlineData("Slab", "Plattengrab")]
    [InlineData("UnknownInside", "Gr.i")]
    [InlineData("UnknownOutside", "Gr.a")]
    public void Each_position_prints_its_short_form(string position, string shortForm)
    {
        var data = GraveData();
        data.GravePosition = position;

        Assert.Contains($"FELD-GRAB-NR: 27 {shortForm}", Text(data));
    }

    /// <summary>Every position has a short form but the one that says nothing
    /// is known; that one prints the field number alone.</summary>
    [Fact]
    public void Every_position_but_unknown_has_a_short_form()
    {
        foreach (var position in Enum.GetNames<GravePosition>())
        {
            var shortForm = DocumentTexts.PositionShortForm(position);
            if (position == nameof(GravePosition.Unknown))
            {
                Assert.Null(shortForm);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(shortForm), position);
            }
        }

        Assert.Equal(
            "FELD-GRAB-NR: 27",
            DocumentTexts.GraveField(
                "27",
                DocumentTexts.PositionShortForm(nameof(GravePosition.Unknown))
            )
        );
    }

    [Fact]
    public void A_grave_migrated_without_site_fields_prints_no_empty_lines()
    {
        var data = GraveData();
        data.GraveSiteName = "";
        data.GraveFieldNumber = "";
        data.GravePosition = null;
        var text = Text(data);

        Assert.DoesNotContain("GRABSTÄTTE", text);
        Assert.DoesNotContain("FELD-GRAB-NR", text);
        Assert.Contains("LAUFWEG-NR:", text);
    }

    [Fact]
    public void The_table_has_item_numbers_and_the_rate_in_the_last_column()
    {
        var text = Text(GraveData());

        Assert.Contains("Artikel / Leistungsbezeichnung", text);
        Assert.Contains("Menge", text);
        Assert.Contains("E-Preis €", text);
        Assert.Contains("Gesamt €", text);
        Assert.Contains("12001", text);
        Assert.Contains("19 %", text);
        Assert.Contains("7 % Incl.", text);
    }

    [Fact]
    public void A_vat_included_line_prints_its_gross_prices()
    {
        var text = Text(GraveData());

        // entered as 10,70 gross; 5 of them are 53,50
        Assert.Contains("10,70", text);
        Assert.Contains("53,50", text);
        Assert.DoesNotContain("10,00", text);
    }

    [Fact]
    public void A_gross_led_line_prints_the_entered_gross()
    {
        // 75,00 at 19 % is 63,03 net and 11,97 VAT; the net alone, grossed up
        // again, would print 75,01
        var data = GraveData();
        data.Share = null;
        data.Lines = new[]
        {
            new InvoiceDocumentData.LineData(
                1,
                "9001",
                "Pflege",
                1,
                63.03m,
                19m,
                63.03m,
                11.97m,
                PriceIncludesVat: true,
                GrossUnitPrice: 75.00m
            ),
        };
        data.VatSummaries = new[]
        {
            new InvoiceDocumentData.VatSummary(19m, 63.03m, 11.97m, 75.00m),
        };
        data.NetTotal = 63.03m;
        data.TotalVat = 11.97m;
        data.GrossTotal = 75.00m;

        var text = Text(data);

        Assert.Contains("75,00", text);
        Assert.DoesNotContain("75,01", text);
    }

    [Fact]
    public void A_vat_included_line_total_agrees_with_the_vat_block()
    {
        // 3 x 10,00 gross at 19 % is 8,40 net each, 25,20 net, 4,79 VAT, 29,99
        // gross: the line bills what the block bills, not 3 x 10,00 = 30,00
        var data = GraveData();
        data.Share = null;
        data.Lines = new[]
        {
            new InvoiceDocumentData.LineData(
                1,
                "9001",
                "Pflege",
                3,
                8.40m,
                19m,
                25.20m,
                4.79m,
                PriceIncludesVat: true
            ),
        };
        data.VatSummaries = new[]
        {
            new InvoiceDocumentData.VatSummary(19m, 25.20m, 4.79m, 29.99m),
        };
        data.NetTotal = 25.20m;
        data.TotalVat = 4.79m;
        data.GrossTotal = 29.99m;

        var text = Text(data);

        Assert.Contains("29,99", text);
        Assert.DoesNotContain("30,00", text);
    }

    [Fact]
    public void A_rate_with_two_decimals_prints_both_in_the_line()
    {
        var data = GraveData();
        data.Share = null;
        data.Lines = new[]
        {
            new InvoiceDocumentData.LineData(1, "9002", "Pflege", 1, 10.00m, 7.65m, 10.00m, 0.77m),
        };
        data.VatSummaries = new[]
        {
            new InvoiceDocumentData.VatSummary(7.65m, 10.00m, 0.77m, 10.77m),
        };
        data.NetTotal = 10.00m;
        data.TotalVat = 0.77m;
        data.GrossTotal = 10.77m;

        var text = Text(data);

        // the VAT block's head prints 7,65 % either way; the line must not round it
        Assert.Contains("7,65 %", text);
        Assert.DoesNotContain("7,7 %", text);
    }

    [Fact]
    public void A_share_invoice_bills_the_share_in_the_vat_block()
    {
        var text = Text(GraveData());

        Assert.Contains("Nettosumme gesamt 114,00 € – davon Ihr Anteil 50 %", text);
        Assert.Contains("7,00 %", text);
        Assert.Contains("19,00 %", text);
        Assert.Contains("Netto €", text);
        Assert.Contains("MWSt €", text);
        Assert.Contains("Nettobetrag", text);
        Assert.Contains("Bruttobetrag", text);
        Assert.Contains("57,00", text);
        Assert.Contains("7,83", text);
        Assert.Contains("64,83", text);
        // the 100 % VAT and gross are not what this recipient pays
        Assert.DoesNotContain("15,66", text);
        Assert.DoesNotContain("129,66", text);
    }

    [Fact]
    public void An_ordinary_invoice_bills_its_whole_amount()
    {
        var data = GraveData();
        data.Share = null;
        var text = Text(data);

        Assert.DoesNotContain("Nettosumme gesamt", text);
        Assert.Contains("114,00", text);
        Assert.Contains("15,66", text);
        Assert.Contains("129,66", text);
    }

    [Fact]
    public void An_old_grave_invoice_still_renders_its_share_note()
    {
        // generated before the share and the snapshot: RecipientCount null, so
        // no Share; the lines are already the share and the note says so
        var data = GraveData();
        data.Share = null;
        data.RecipientNumber = null;
        data.GraveNumber = null;
        data.GraveSiteName = null;
        data.GraveFieldNumber = null;
        data.GravePosition = null;
        data.ShareText = "Diese Rechnung enthält 50 % des Gesamtbetrags.";
        var text = Text(data);

        Assert.Contains("Diese Rechnung enthält 50 % des Gesamtbetrags.", text);
        Assert.DoesNotContain("LAUFWEG-NR", text);
        Assert.Contains("Bruttobetrag", text);
    }

    [Fact]
    public void The_vat_block_sits_below_the_ending_text_at_the_foot_of_the_page()
    {
        using var document = PdfDocument.Open(
            DocumentService.GeneratePdf(GraveData(), isCopy: false)
        );
        var page = document.GetPage(1);

        var ending = FirstWord(page, "Zahlbar").BoundingBox.Bottom;
        var gross = FirstWord(page, "Bruttobetrag").BoundingBox.Top;
        Assert.True(gross < ending, "the VAT block must come after the ending text");
        // pushed down to the foot of the content area, not straight after the text
        Assert.True(
            gross < 80 * PointsPerMillimetre,
            "the VAT block is not at the foot of the page"
        );
    }
}
