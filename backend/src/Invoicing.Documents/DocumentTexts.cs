using System.Globalization;

namespace Burkard.Invoicing.Documents;

/// <summary>Every string a generated document shows. Documents are always
/// German, with German number and date formats.</summary>
public static class DocumentTexts
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("de-DE");

    public const string ColumnDescription = "Bezeichnung";
    public const string ColumnCount = "Anzahl";
    public const string ColumnUnitPrice = "Einzelpreis";
    public const string ColumnVat = "MwSt.";
    public const string ColumnTotal = "Summe";
    public const string PageFooterPageOf = "Seite";
    public const string PageFooterOf = " von ";
    public const string VatRateSuffix = " %";

    public static string ShareText(decimal percent) =>
        string.Format(
            Culture,
            "Diese Rechnung enthält {0} % des Gesamtbetrags.",
            percent.ToString("0.##", Culture)
        );

    public static string InvoiceTitle(string invoiceNumber) =>
        string.Format(Culture, "Rechnung {0}", invoiceNumber);

    public static string NetForRate(decimal rate, decimal amount) =>
        string.Format(
            Culture,
            "Netto {0} %: {1}",
            rate.ToString("0.#", Culture),
            amount.ToString("N2", Culture)
        );

    public static string VatForRate(decimal rate, decimal amount) =>
        string.Format(
            Culture,
            "MwSt. {0} %: {1}",
            rate.ToString("0.#", Culture),
            amount.ToString("N2", Culture)
        );

    /// <summary>The VAT for one rate, stated together with the amount it was
    /// worked out from. The share block uses this rather than
    /// <see cref="VatForRate"/>: a grave invoice prints its lines at 100 %, so
    /// the recipient's own taxable base per rate appears nowhere else on the
    /// page, and a VAT invoice has to show it.</summary>
    public static string VatForRateFromNet(decimal rate, decimal net, decimal amount) =>
        string.Format(
            Culture,
            "MwSt. {0} % aus {1}: {2}",
            rate.ToString("0.#", Culture),
            net.ToString("N2", Culture),
            amount.ToString("N2", Culture)
        );

    public static string NetTotal(decimal amount) =>
        string.Format(Culture, "Netto gesamt: {0}", amount.ToString("N2", Culture));

    public static string VatTotal(decimal amount) =>
        string.Format(Culture, "MwSt. gesamt: {0}", amount.ToString("N2", Culture));

    public static string GrossTotal(decimal amount) =>
        string.Format(Culture, "Gesamtbetrag: {0}", amount.ToString("N2", Culture));

    public static string NetSumTotal(decimal amount) =>
        string.Format(Culture, "Nettosumme gesamt: {0}", amount.ToString("N2", Culture));

    public static string SharePart(decimal percent, decimal amount) =>
        string.Format(
            Culture,
            "Ihr Anteil ({0} %): {1}",
            percent.ToString("0.##", Culture),
            amount.ToString("N2", Culture)
        );
}
