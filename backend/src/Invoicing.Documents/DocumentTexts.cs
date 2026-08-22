using System.Globalization;

namespace Burkard.Invoicing.Documents;

public static class DocumentTexts
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string ShareText(decimal percent) =>
        string.Create(German,
            $"Diese Rechnung enthält {percent:0.##} % des Gesamtbetrags.");

    public static string InvoiceTitle(string invoiceNumber) => $"Rechnung {invoiceNumber}";
    public static string ColumnDescription => "Bezeichnung";
    public static string ColumnCount => "Anzahl";
    public static string ColumnUnitPrice => "Einzelpreis";
    public static string ColumnVat => "MwSt.";
    public static string ColumnTotal => "Summe";
    public static string NetForRate(decimal rate, decimal amount) =>
        $"Netto {rate.ToString("0.#", German)} %: {amount.ToString("N2", German)}";
    public static string VatForRate(decimal rate, decimal amount) =>
        $"MwSt. {rate.ToString("0.#", German)} %: {amount.ToString("N2", German)}";
    public static string NetTotal(decimal amount) => $"Netto gesamt: {amount.ToString("N2", German)}";
    public static string VatTotal(decimal amount) => $"MwSt. gesamt: {amount.ToString("N2", German)}";
    public static string GrossTotal(decimal amount) => $"Gesamtbetrag: {amount.ToString("N2", German)}";
    public static string PageFooterPageOf => "Seite";
    public static string PageFooterOf => " von ";
    public static string VatRateSuffix => " %";
}
