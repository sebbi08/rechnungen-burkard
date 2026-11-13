using System;
using System.Collections.Generic;
using System.Linq;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>The care list and the planting list: plain sheets, a stack of
/// pages per cemetery, rendered in the order the data comes in.</summary>
public class WorkListDocumentTests
{
    private static readonly DateOnly MadeOn = new(2026, 11, 13);

    private static WorkListLine Line(
        string itemNumber = "1001",
        string description = "Jahrespflege",
        int count = 1,
        BillingQuarter? quarter = null,
        decimal? amount = 120.00m
    ) => new(count, itemNumber, description, quarter, amount);

    private static WorkListGrave Grave(
        string number = "A-12",
        string site = "Müller",
        string? field = "27",
        GravePosition? position = GravePosition.DoubleInside,
        DateOnly? start = null,
        DateOnly? end = null,
        bool startInPeriod = false,
        bool endInPeriod = false,
        string? contract = "4711",
        params WorkListLine[] lines
    ) =>
        new(
            number,
            site,
            field,
            position,
            start ?? new DateOnly(2015, 4, 1),
            end,
            startInPeriod,
            endInPeriod,
            contract,
            lines
        );

    private static WorkListGrave Grave12() =>
        Grave(end: new DateOnly(2030, 12, 1), lines: [Line()]);

    private static WorkListData Care(WorkListKind kind, params WorkListCemetery[] cemeteries) =>
        new(kind, 2027, null, MadeOn, cemeteries);

    private static WorkListData Planting(params WorkListCemetery[] cemeteries) =>
        new(WorkListKind.Planting, 2027, BillingQuarter.Spring, MadeOn, cemeteries);

    private static WorkListCemetery Cemetery(string name, params WorkListGrave[] graves) =>
        new(name, graves);

    private static PdfDocument Open(WorkListData data) =>
        PdfDocument.Open(WorkListDocuments.Generate(data));

    private static string Text(WorkListData data)
    {
        using var document = Open(data);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; )
        {
            count++;
            i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal);
        }
        return count;
    }

    /// <summary>The letters a part of the page's text is drawn with.</summary>
    private static IReadOnlyList<Letter> LettersOf(Page page, string part)
    {
        Assert.Equal(page.Text, string.Concat(page.Letters.Select(l => l.Value)));
        var index = page.Text.IndexOf(part, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{part}' is not on the page");
        return page.Letters.Skip(index).Take(part.Length).ToList();
    }

    private static Word Word(Page page, string text) => page.GetWords().Single(w => w.Text == text);

    private const double PointsPerMillimetre = 72 / 25.4;

    [Fact]
    public void Each_cemetery_starts_on_its_own_page()
    {
        using var document = Open(
            Care(
                WorkListKind.CareGravesOnly,
                Cemetery("Waldfriedhof", Grave12()),
                Cemetery("Bergfriedhof", Grave(number: "B-1"))
            )
        );

        Assert.Equal(2, document.NumberOfPages);
        Assert.Contains("Waldfriedhof", document.GetPage(1).Text);
        Assert.DoesNotContain("Bergfriedhof", document.GetPage(1).Text);
        Assert.Contains("Bergfriedhof", document.GetPage(2).Text);
        Assert.DoesNotContain("Waldfriedhof", document.GetPage(2).Text);
    }

    [Fact]
    public void The_heading_names_list_cemetery_date_and_page()
    {
        var care = Text(Care(WorkListKind.CareGravesOnly, Cemetery("Waldfriedhof", Grave12())));
        var planting = Text(Planting(Cemetery("Waldfriedhof", Grave12())));

        Assert.Contains("Pflegeliste 2027", care);
        Assert.Contains("Waldfriedhof", care);
        Assert.Contains("Stand: 13.11.2026", care);
        Assert.Contains("Seite 1 von 1", care);
        Assert.Contains("Pflanzliste Frühling 2027", planting);
        Assert.Contains("Stand: 13.11.2026", planting);
        Assert.Contains("Seite 1 von 1", planting);
    }

    /// <summary>Each cemetery's pages are a stack a worker takes along, so
    /// "Seite 2 von 3" counts that stack, not the whole file.</summary>
    [Fact]
    public void Page_numbers_restart_for_each_cemetery()
    {
        var many = Enumerable
            .Range(1, 120)
            .Select(n => Grave(number: $"A-{n}", lines: [Line()]))
            .ToArray();
        using var document = Open(
            Care(
                WorkListKind.CareWithServices,
                Cemetery("Waldfriedhof", many),
                Cemetery("Bergfriedhof", Grave(number: "B-1", lines: [Line()]))
            )
        );

        var pagesOfA = document.NumberOfPages - 1;
        Assert.True(pagesOfA >= 2, $"the first cemetery has {pagesOfA} page(s)");
        Assert.Contains($"Seite 1 von {pagesOfA}", document.GetPage(1).Text);
        Assert.Contains($"Seite {pagesOfA} von {pagesOfA}", document.GetPage(pagesOfA).Text);
        Assert.Contains("Bergfriedhof", document.GetPage(pagesOfA + 1).Text);
        Assert.Contains("Seite 1 von 1", document.GetPage(pagesOfA + 1).Text);
    }

    [Theory]
    [InlineData(WorkListKind.CareGravesOnly)]
    [InlineData(WorkListKind.CareWithServices)]
    [InlineData(WorkListKind.Planting)]
    public void The_grave_details_are_printed(WorkListKind kind)
    {
        var text = Text(
            new WorkListData(
                kind,
                2027,
                kind == WorkListKind.Planting ? BillingQuarter.Spring : null,
                MadeOn,
                [Cemetery("Waldfriedhof", Grave12())]
            )
        );

        Assert.Contains("A-12", text);
        Assert.Contains("Müller", text);
        Assert.Contains("27 dGr.i", text);
        Assert.Contains("04/2015", text);
        Assert.Contains("12/2030", text);
        Assert.Contains("4711", text);
    }

    [Fact]
    public void The_position_without_a_position_is_the_field_number()
    {
        var text = Text(
            Care(
                WorkListKind.CareGravesOnly,
                Cemetery("Waldfriedhof", Grave(field: "31", position: null))
            )
        );

        Assert.Contains("31", text);
        Assert.DoesNotContain("dGr", text);
    }

    [Fact]
    public void A_grave_without_end_prints_a_dash()
    {
        var text = Text(
            Care(WorkListKind.CareGravesOnly, Cemetery("Waldfriedhof", Grave(end: null)))
        );

        Assert.Contains("–", text);
    }

    [Theory]
    [InlineData(WorkListKind.CareGravesOnly)]
    [InlineData(WorkListKind.CareWithServices)]
    [InlineData(WorkListKind.Planting)]
    public void Start_and_end_in_the_period_are_bold(WorkListKind kind)
    {
        using var document = Open(
            new WorkListData(
                kind,
                2027,
                kind == WorkListKind.Planting ? BillingQuarter.Spring : null,
                MadeOn,
                [
                    Cemetery(
                        "Waldfriedhof",
                        Grave(
                            number: "A-12",
                            end: new DateOnly(2027, 3, 1),
                            endInPeriod: true,
                            lines: [Line()]
                        ),
                        Grave(
                            number: "A-14",
                            start: new DateOnly(2027, 11, 1),
                            startInPeriod: true,
                            lines: [Line()]
                        )
                    ),
                ]
            )
        );
        var page = document.GetPage(1);

        Assert.All(LettersOf(page, "11/2027"), l => Assert.Contains("Bold", l.FontName));
        Assert.All(LettersOf(page, "03/2027"), l => Assert.Contains("Bold", l.FontName));
        Assert.All(LettersOf(page, "04/2015"), l => Assert.DoesNotContain("Bold", l.FontName));
    }

    /// <summary>The graves-only care list runs in two columns; the last page
    /// shares its graves out between both.</summary>
    [Fact]
    public void The_graves_only_variant_has_two_columns()
    {
        using var document = Open(
            Care(
                WorkListKind.CareGravesOnly,
                Cemetery("Waldfriedhof", Grave(number: "A-12"), Grave(number: "A-14"))
            )
        );
        var page = document.GetPage(1);

        var first = Word(page, "A-12").BoundingBox.Left;
        var last = Word(page, "A-14").BoundingBox.Left;
        var headings = page.GetWords()
            .Where(w => w.Text == "Grabstätte")
            .OrderBy(w => w.BoundingBox.Left)
            .ToList();

        Assert.Equal(1, document.NumberOfPages);
        // each column has its own heading
        Assert.Equal(2, headings.Count);
        Assert.True(
            headings[1].BoundingBox.Left - headings[0].BoundingBox.Left > 70 * PointsPerMillimetre
        );
        Assert.True(
            last - first > 70 * PointsPerMillimetre,
            $"the second grave starts {(last - first) / PointsPerMillimetre:0} mm right of the first"
        );
    }

    [Fact]
    public void A_long_site_name_is_printed_whole()
    {
        const string site = "Grabstätte der Familien Müller-Lüdenscheid und Schwarzenbach";
        Assert.Equal(60, site.Length);
        var text = Text(
            Care(WorkListKind.CareGravesOnly, Cemetery("Waldfriedhof", Grave(site: site)))
        );

        Assert.Contains(
            string.Concat(site.Where(c => !char.IsWhiteSpace(c))),
            string.Concat(text.Where(c => !char.IsWhiteSpace(c)))
        );
    }

    [Fact]
    public void The_services_variant_lists_lines_with_amounts()
    {
        using var document = Open(
            Care(WorkListKind.CareWithServices, Cemetery("Waldfriedhof", Grave12()))
        );
        var page = document.GetPage(1);

        Assert.Contains("1 × 1001", page.Text);
        Assert.Contains("Jahrespflege", page.Text);
        Assert.Contains("120,00 €", page.Text);
        Assert.Contains("Leistung", page.Text);
        Assert.Contains("Betrag je Halbjahr", page.Text);
        // the column heading stands above the first grave
        Assert.True(
            page.GetWords().First(w => w.Text == "Halbjahr").BoundingBox.Bottom
                > Word(page, "A-12").BoundingBox.Top
        );
    }

    [Fact]
    public void A_seasonal_line_names_its_quarter_on_the_care_list()
    {
        var seasonal = Grave(
            lines: [Line("2010", "Frühjahrsbepflanzung", quarter: BillingQuarter.Spring)]
        );

        var care = Text(Care(WorkListKind.CareWithServices, Cemetery("Waldfriedhof", seasonal)));
        var planting = Text(Planting(Cemetery("Waldfriedhof", seasonal)));

        Assert.Contains("Frühjahrsbepflanzung (Frühling)", care);
        Assert.Contains("Frühjahrsbepflanzung", planting);
        Assert.DoesNotContain("(Frühling)", planting);
    }

    [Theory]
    [InlineData(WorkListKind.CareWithServices)]
    [InlineData(WorkListKind.Planting)]
    public void A_line_without_amount_prints_a_dash(WorkListKind kind)
    {
        var text = Text(
            new WorkListData(
                kind,
                2027,
                kind == WorkListKind.Planting ? BillingQuarter.Spring : null,
                MadeOn,
                [
                    Cemetery(
                        "Waldfriedhof",
                        Grave(end: new DateOnly(2030, 12, 1), lines: [Line(amount: null)])
                    ),
                ]
            )
        );

        Assert.Contains("–", text);
        Assert.DoesNotContain("0,00 €", text);
    }

    [Fact]
    public void The_planting_list_prints_grave_details_once_per_grave()
    {
        var text = Text(
            Planting(
                Cemetery(
                    "Waldfriedhof",
                    Grave(
                        end: new DateOnly(2030, 12, 1),
                        lines:
                        [
                            Line("2010", "Frühjahrsbepflanzung", amount: 48.15m),
                            Line("2020", "Schalen", count: 2, amount: 21.40m),
                        ]
                    )
                )
            )
        );

        Assert.Equal(1, Occurrences(text, "A-12"));
        Assert.Equal(1, Occurrences(text, "04/2015"));
        Assert.Equal(1, Occurrences(text, "4711"));
        Assert.Contains("1 × Frühjahrsbepflanzung", text);
        Assert.Contains("2 × Schalen", text);
        Assert.Contains("48,15 €", text);
        Assert.Contains("21,40 €", text);
    }

    [Fact]
    public void The_properties_name_the_list()
    {
        using var care = Open(
            Care(WorkListKind.CareGravesOnly, Cemetery("Waldfriedhof", Grave12()))
        );
        using var planting = Open(Planting(Cemetery("Waldfriedhof", Grave12())));

        Assert.Equal("Pflegeliste 2027", care.Information.Title);
        Assert.Equal("Blumen Burkard", care.Information.Author);
        Assert.Equal("Pflanzliste Frühling 2027", planting.Information.Title);
    }

    [Fact]
    public void The_same_data_gives_the_same_bytes()
    {
        var data = Care(WorkListKind.CareWithServices, Cemetery("Waldfriedhof", Grave12()));

        Assert.Equal(WorkListDocuments.Generate(data), WorkListDocuments.Generate(data));
    }

    [Fact]
    public void The_texts_for_quarters_positions_months_and_amounts()
    {
        Assert.Equal("Frühling", DocumentTexts.QuarterName(BillingQuarter.Spring));
        Assert.Equal("Winter", DocumentTexts.QuarterName(BillingQuarter.Winter));
        Assert.Equal("Fruehling", DocumentTexts.QuarterFileWord(BillingQuarter.Spring));
        Assert.Equal("Herbst", DocumentTexts.QuarterFileWord(BillingQuarter.Autumn));
        Assert.Equal("Pflegeliste 2027", DocumentTexts.CareListTitle(2027));
        Assert.Equal(
            "Pflanzliste Sommer 2027",
            DocumentTexts.PlantingListTitle(BillingQuarter.Summer, 2027)
        );
        Assert.Equal(
            "27 Rasengrab außen",
            DocumentTexts.GravePositionText("27", GravePosition.LawnOutside)
        );
        Assert.Equal("dGr", DocumentTexts.GravePositionText(null, GravePosition.Double));
        Assert.Equal("", DocumentTexts.GravePositionText(" ", null));
        Assert.Equal("04/2015", DocumentTexts.Month(new DateOnly(2015, 4, 1)));
        Assert.Equal("–", DocumentTexts.Month(null));
        Assert.Equal("1.234,50 €", DocumentTexts.Amount(1234.5m));
        Assert.Equal("–", DocumentTexts.Amount(null));
    }
}
