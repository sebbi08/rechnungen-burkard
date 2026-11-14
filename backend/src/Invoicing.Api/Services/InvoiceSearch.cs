using System.Globalization;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Matches a search term, word by word, against an invoice's own
/// fields — number, recipient, date and total. Line items are deliberately not
/// searched.
///
/// The comparison runs in memory rather than as a SQL LIKE: SQLite folds case
/// for ASCII only, so a term typed as 'ö' would miss a name spelled 'Ö'.</summary>
public static class InvoiceSearch
{
    public sealed record Row(
        long Id,
        string InvoiceNumber,
        string RecipientName,
        string RecipientAddress,
        DateTime GeneratedAt,
        decimal GrossTotal
    );

    /// <summary>What the invoice list's column searches ask for; a part that
    /// is null or without words asks for nothing.</summary>
    public sealed record Filter(string? Number, string? Recipient, string? Date, string? Total)
    {
        public bool IsEmpty =>
            SearchWords.Of(Number ?? "").Length == 0
            && SearchWords.Of(Recipient ?? "").Length == 0
            && SearchWords.Of(Date ?? "").Length == 0
            && SearchWords.OfAmount(Total).Length == 0;
    }

    /// <summary>Whether the row passes every part of the filter, each part
    /// against its own column only: number, recipient (name and address),
    /// the date as shown, and the total the way
    /// <see cref="SearchWords.InAmount"/> matches an amount.</summary>
    public static bool Matches(Row row, Filter filter)
    {
        var date = row.GeneratedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return SearchWords.InAny(filter.Number, row.InvoiceNumber)
            && SearchWords.InAny(filter.Recipient, row.RecipientName, row.RecipientAddress)
            && SearchWords.InAny(filter.Date, date)
            && SearchWords.InAmount(filter.Total, row.GrossTotal);
    }

    /// <summary>The invoice's gross total, rounded exactly the way
    /// DocumentDataBuilder rounds it, so the number the user reads on the
    /// document is the number they can search for.</summary>
    public static decimal GrossTotal(
        IEnumerable<(int Count, decimal UnitPrice, decimal VatRate, decimal? GrossUnitPrice)> lines
    )
    {
        decimal netCents = 0;
        decimal vatCents = 0;
        foreach (var line in lines)
        {
            var (net, vat) = LineAmounts.Cents(
                line.Count,
                line.UnitPrice,
                line.VatRate,
                line.GrossUnitPrice
            );
            netCents += net;
            vatCents += vat;
        }

        return (netCents + vatCents) / 100m;
    }
}
