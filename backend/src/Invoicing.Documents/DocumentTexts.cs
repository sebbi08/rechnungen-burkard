using System.Globalization;

namespace Burkard.Invoicing.Documents;

/// <summary>Every string a generated document shows, in the document's language.
/// The invoice snapshots its language, so switching the setting never changes
/// documents that were already generated.</summary>
public sealed class DocumentTexts
{
    public const string DefaultLanguage = "de";

    private static readonly DocumentTexts GermanTexts = new(
        CultureInfo.GetCultureInfo("de-DE"),
        share: "Diese Rechnung enthält {0} % des Gesamtbetrags.",
        title: "Rechnung {0}",
        description: "Bezeichnung",
        count: "Anzahl",
        unitPrice: "Einzelpreis",
        vat: "MwSt.",
        total: "Summe",
        netForRate: "Netto {0} %: {1}",
        vatForRate: "MwSt. {0} %: {1}",
        netTotal: "Netto gesamt: {0}",
        vatTotal: "MwSt. gesamt: {0}",
        grossTotal: "Gesamtbetrag: {0}",
        netSumTotal: "Nettosumme gesamt: {0}",
        sharePart: "Ihr Anteil ({0} %): {1}",
        page: "Seite",
        of: " von "
    );

    private static readonly DocumentTexts EnglishTexts = new(
        CultureInfo.GetCultureInfo("en-GB"),
        share: "This invoice covers {0} % of the total amount.",
        title: "Invoice {0}",
        description: "Description",
        count: "Quantity",
        unitPrice: "Unit price",
        vat: "VAT",
        total: "Total",
        netForRate: "Net {0} %: {1}",
        vatForRate: "VAT {0} %: {1}",
        netTotal: "Net total: {0}",
        vatTotal: "VAT total: {0}",
        grossTotal: "Amount due: {0}",
        netSumTotal: "Net sum total: {0}",
        sharePart: "Your share ({0} %): {1}",
        page: "Page",
        of: " of "
    );

    private readonly string _share;
    private readonly string _title;
    private readonly string _netForRate;
    private readonly string _vatForRate;
    private readonly string _netTotal;
    private readonly string _vatTotal;
    private readonly string _grossTotal;
    private readonly string _netSumTotal;
    private readonly string _sharePart;

    private DocumentTexts(
        CultureInfo culture,
        string share,
        string title,
        string description,
        string count,
        string unitPrice,
        string vat,
        string total,
        string netForRate,
        string vatForRate,
        string netTotal,
        string vatTotal,
        string grossTotal,
        string netSumTotal,
        string sharePart,
        string page,
        string of
    )
    {
        Culture = culture;
        _share = share;
        _title = title;
        _netForRate = netForRate;
        _vatForRate = vatForRate;
        _netTotal = netTotal;
        _vatTotal = vatTotal;
        _grossTotal = grossTotal;
        _netSumTotal = netSumTotal;
        _sharePart = sharePart;
        ColumnDescription = description;
        ColumnCount = count;
        ColumnUnitPrice = unitPrice;
        ColumnVat = vat;
        ColumnTotal = total;
        PageFooterPageOf = page;
        PageFooterOf = of;
    }

    /// <summary>The supported document languages, in the order the UI offers them.</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { "de", "en" };

    public static bool IsSupported(string? language) =>
        language is not null && SupportedLanguages.Contains(language);

    /// <summary>Falls back to the default language for anything unknown, so an
    /// old row with an unexpected value still renders.</summary>
    public static DocumentTexts For(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? EnglishTexts
            : GermanTexts;

    public CultureInfo Culture { get; }
    public string ColumnDescription { get; }
    public string ColumnCount { get; }
    public string ColumnUnitPrice { get; }
    public string ColumnVat { get; }
    public string ColumnTotal { get; }
    public string PageFooterPageOf { get; }
    public string PageFooterOf { get; }
    public string VatRateSuffix => " %";

    public string ShareText(decimal percent) =>
        string.Format(Culture, _share, percent.ToString("0.##", Culture));

    public string InvoiceTitle(string invoiceNumber) =>
        string.Format(Culture, _title, invoiceNumber);

    public string NetForRate(decimal rate, decimal amount) =>
        string.Format(
            Culture,
            _netForRate,
            rate.ToString("0.#", Culture),
            amount.ToString("N2", Culture)
        );

    public string VatForRate(decimal rate, decimal amount) =>
        string.Format(
            Culture,
            _vatForRate,
            rate.ToString("0.#", Culture),
            amount.ToString("N2", Culture)
        );

    public string NetTotal(decimal amount) =>
        string.Format(Culture, _netTotal, amount.ToString("N2", Culture));

    public string VatTotal(decimal amount) =>
        string.Format(Culture, _vatTotal, amount.ToString("N2", Culture));

    public string GrossTotal(decimal amount) =>
        string.Format(Culture, _grossTotal, amount.ToString("N2", Culture));

    public string NetSumTotal(decimal amount) =>
        string.Format(Culture, _netSumTotal, amount.ToString("N2", Culture));

    public string SharePart(decimal percent, decimal amount) =>
        string.Format(
            Culture,
            _sharePart,
            percent.ToString("0.##", Culture),
            amount.ToString("N2", Culture)
        );
}
