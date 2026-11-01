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

    /// <summary>Whether every word of the term is found in one of the fields;
    /// the words need not share a field.</summary>
    public static bool Matches(Row row, string term) =>
        SearchWords.All(
            term,
            word =>
                Contains(row.InvoiceNumber, word)
                || Contains(row.RecipientName, word)
                || Contains(row.RecipientAddress, word)
                // the date as the list shows it, so what the user reads is what they can type
                || Contains(
                    row.GeneratedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    word
                )
                // the total is rendered with a '.'; a German ',' in the term still matches
                || Contains(
                    row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture),
                    word.Replace(',', '.')
                )
        );

    /// <summary>The invoice's gross total, rounded exactly the way
    /// DocumentDataBuilder rounds it, so the number the user reads on the
    /// document is the number they can search for.</summary>
    public static decimal GrossTotal(
        IEnumerable<(int Count, decimal UnitPrice, decimal VatRate)> lines
    )
    {
        decimal netCents = 0;
        decimal vatCents = 0;
        foreach (var line in lines)
        {
            var lineCents = Math.Round(
                line.Count * line.UnitPrice * 100m,
                0,
                MidpointRounding.AwayFromZero
            );
            netCents += lineCents;
            vatCents += Math.Round(
                lineCents * line.VatRate / 100m,
                0,
                MidpointRounding.AwayFromZero
            );
        }

        return (netCents + vatCents) / 100m;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
