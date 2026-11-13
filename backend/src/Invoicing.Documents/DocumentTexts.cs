using System.Globalization;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Documents;

/// <summary>Every string a generated document shows. Documents are always
/// German, with German number and date formats, and follow the layout of the
/// old system's invoices.</summary>
public static class DocumentTexts
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("de-DE");

    public const string Title = "**** RECHNUNG ****";
    public const string MetadataTitle = "Rechnung";
    public const string QuoteHeading = "**** ANGEBOT ****";
    public const string QuoteMetadataTitle = "Angebot";
    public const string MetadataAuthor = "Blumen Burkard";
    public const string LabelNumber = "NUMMER:";
    public const string LabelCustomerNumber = "KUNDEN-NR:";
    public const string LabelGraveNumber = "LAUFWEG-NR:";
    public const string LabelDate = "DATUM:";
    public const string LabelPage = "SEITE:";
    public const string PageOf = " von ";
    public const string PageWord = "Seite";
    public const string CustomerNumberShort = "Kunden-Nr.";

    public const string ColumnDescription = "Artikel / Leistungsbezeichnung";
    public const string ColumnCount = "Menge";
    public const string ColumnUnitPrice = "E-Preis €";
    public const string ColumnTotal = "Gesamt €";
    public const string ColumnVat = "ST";
    public const string VatIncluded = "Incl.";

    public const string NetRow = "Netto €";
    public const string VatRow = "MWSt €";
    public const string Currency = "€";
    public const string NetAmount = "Nettobetrag";
    public const string VatAmount = "MWSt";
    public const string GrossAmount = "Bruttobetrag";

    public static string ShareText(decimal percent) =>
        string.Format(
            Culture,
            "Diese Rechnung enthält {0} % des Gesamtbetrags.",
            percent.ToString("0.##", Culture)
        );

    public static string InvoiceTitle(string invoiceNumber) =>
        string.Format(Culture, "Rechnung {0}", invoiceNumber);

    public static string QuoteTitle(string number) =>
        string.Format(Culture, "{0} {1}", QuoteMetadataTitle, number);

    public static string GraveSite(string name) => $"GRABSTÄTTE: {name}";

    /// <summary>"FELD-GRAB-NR: 27 dGr.a", with whatever of the two exists.</summary>
    public static string GraveField(string? fieldNumber, string? positionShortForm) =>
        "FELD-GRAB-NR: "
        + string.Join(
            " ",
            new[] { fieldNumber, positionShortForm }.Where(s => !string.IsNullOrWhiteSpace(s))
        );

    /// <summary>The old system's abbreviation of a grave position, from the
    /// position's member name: the kind, then ".i" or ".a" for inside or
    /// outside ("dGr.a"), or nothing where the side is not known ("dGr"). Kinds
    /// the old system had no abbreviation for are spelled out ("Rasengrab
    /// außen"). Null for none, for <c>Unknown</c> (nothing known to print) and
    /// for a name that is not a position.</summary>
    public static string? PositionShortForm(string? position)
    {
        if (position is null)
        {
            return null;
        }

        var side =
            position.EndsWith(Inside, StringComparison.Ordinal) ? Inside
            : position.EndsWith(Outside, StringComparison.Ordinal) ? Outside
            : "";
        var kind = position[..^side.Length];
        if (AbbreviatedKinds.TryGetValue(kind, out var abbreviation))
        {
            return side switch
            {
                Inside => abbreviation + ".i",
                Outside => abbreviation + ".a",
                _ => kind == "Unknown" ? null : abbreviation,
            };
        }

        if (SpelledKinds.TryGetValue(kind, out var word))
        {
            return side switch
            {
                Inside => word + " innen",
                Outside => word + " außen",
                _ => word,
            };
        }

        return null;
    }

    private const string Inside = "Inside";
    private const string Outside = "Outside";

    private static readonly Dictionary<string, string> AbbreviatedKinds = new()
    {
        ["Single"] = "eGr",
        ["Double"] = "dGr",
        ["Urn"] = "uGr",
        ["Triple"] = "3fGr",
        ["Quadruple"] = "4fGr",
        ["Child"] = "kGr",
        ["Unknown"] = "Gr",
    };

    private static readonly Dictionary<string, string> SpelledKinds = new()
    {
        ["Lawn"] = "Rasengrab",
        ["LawnUrn"] = "Rasen-Urne",
        ["Tree"] = "Baumgrab",
        ["Honorary"] = "Ehrengrab",
        ["Slab"] = "Plattengrab",
    };

    /// <summary>"19 %", or "7,65 %" for a rate with decimals: the line's ST cell.</summary>
    public static string VatRate(decimal rate) => $"{rate.ToString("0.##", Culture)} %";

    /// <summary>"19,00 %": a column head of the VAT block.</summary>
    public static string VatRateHeading(decimal rate) => $"{rate.ToString("N2", Culture)} %";

    /// <summary>Above the VAT block of an invoice that bills one recipient's
    /// share: the grave's whole net, and the share of it the block bills.</summary>
    public static string ShareLine(decimal netTotal, decimal percent) =>
        string.Format(
            Culture,
            "Nettosumme gesamt {0} € – davon Ihr Anteil {1} %",
            netTotal.ToString("N2", Culture),
            percent.ToString("0.##", Culture)
        );

    /// <summary>A quarter's German name: "Frühling", "Sommer", "Herbst", "Winter".</summary>
    public static string QuarterName(BillingQuarter quarter) =>
        quarter switch
        {
            BillingQuarter.Spring => "Frühling",
            BillingQuarter.Summer => "Sommer",
            BillingQuarter.Autumn => "Herbst",
            BillingQuarter.Winter => "Winter",
            _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, null),
        };

    /// <summary>A quarter's name as a file name spells it, without umlauts:
    /// "Fruehling".</summary>
    public static string QuarterFileWord(BillingQuarter quarter) =>
        QuarterName(quarter).Replace("ü", "ue", StringComparison.Ordinal);

    public static string CareListTitle(int year) => string.Format(Culture, "Pflegeliste {0}", year);

    public static string PlantingListTitle(BillingQuarter quarter, int year) =>
        string.Format(Culture, "Pflanzliste {0} {1}", QuarterName(quarter), year);

    /// <summary>"27 dGr.a": the field number and the position's short form,
    /// whichever of the two exists, as the grave invoice composes them but
    /// without its "FELD-GRAB-NR:" label.</summary>
    public static string GravePositionText(string? fieldNumber, GravePosition? position) =>
        string.Join(
            " ",
            new[] { fieldNumber, PositionShortForm(position?.ToString()) }.Where(s =>
                !string.IsNullOrWhiteSpace(s)
            )
        );

    /// <summary>"04/2015", or "–" for none (a grave without an end).</summary>
    public static string Month(DateOnly? month) =>
        // the slash quoted: unquoted it stands for the culture's date separator, "." in German
        month?.ToString("MM'/'yyyy", Culture) ?? Dash;

    /// <summary>"48,15 €", or "–" for a service without a price.</summary>
    public static string Amount(decimal? amount) =>
        amount is { } value ? $"{value.ToString("N2", Culture)} {Currency}" : Dash;

    public const string Dash = "–";

    public const string WorkListColumnNumber = "Nr.";
    public const string WorkListColumnSite = "Grabstätte";
    public const string WorkListColumnPosition = "Pos.";
    public const string WorkListColumnStart = "Beginn";
    public const string WorkListColumnEnd = "Ende";
    public const string WorkListColumnContract = "Vertrag";
    public const string WorkListColumnService = "Leistung";
    public const string WorkListColumnAmount = "Betrag";
    public const string WorkListColumnHalfYearAmount = "Betrag je Halbjahr";

    /// <summary>"Stand: 13.11.2026": the day a list was made.</summary>
    public static string WorkListMadeOn(DateOnly day) =>
        string.Format(Culture, "Stand: {0:dd.MM.yyyy}", day);

    /// <summary>"2 × 1001 Jahrespflege (Frühling)", the quarter only where
    /// one is given.</summary>
    public static string WorkListService(
        int count,
        string? itemNumber,
        string description,
        BillingQuarter? quarter
    ) =>
        string.Join(
            " ",
            new[] { $"{count} ×", itemNumber, description }.Where(s =>
                !string.IsNullOrWhiteSpace(s)
            )
        ) + (quarter is { } q ? $" ({QuarterName(q)})" : "");
}
