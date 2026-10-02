using System.Globalization;

namespace Burkard.Invoicing.Documents;

/// <summary>Every string a generated document shows. Documents are always
/// German, with German number and date formats, and follow the layout of the
/// old system's invoices.</summary>
public static class DocumentTexts
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("de-DE");

    public const string Title = "**** RECHNUNG ****";
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

    public static string GraveSite(string name) => $"GRABSTÄTTE: {name}";

    /// <summary>"FELD-GRAB-NR: 27 dGr.a", with whatever of the two exists.</summary>
    public static string GraveField(string? fieldNumber, string? positionShortForm) =>
        "FELD-GRAB-NR: "
        + string.Join(
            " ",
            new[] { fieldNumber, positionShortForm }.Where(s => !string.IsNullOrWhiteSpace(s))
        );

    /// <summary>The old system's abbreviation of a grave position, from the
    /// position's member name; null for none or an unknown name.</summary>
    public static string? PositionShortForm(string? position) =>
        position switch
        {
            "DoubleInside" => "dGr.i",
            "DoubleOutside" => "dGr.a",
            "SingleInside" => "eGr.i",
            "SingleOutside" => "eGr.a",
            "UrnInside" => "uGr.i",
            "UrnOutside" => "uGr.a",
            _ => null,
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
}
