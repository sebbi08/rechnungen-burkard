using System.Globalization;

namespace Burkard.Invoicing.Api.Services;

/// <summary>A search term as the words it is made of. Every search looks for
/// each word on its own, in whichever field holds it, so "Hanna Bauer" finds
/// "Bauer Hanna" and a name typed with a street finds the one who lives there.</summary>
public static class SearchWords
{
    public static string[] Of(string term) =>
        term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether every word passes — true for a term without words.</summary>
    public static bool All(string term, Func<string, bool> found) => Of(term).All(found);

    /// <summary>Whether every word of the term is found in at least one of the
    /// values (null values hold nothing) — true for a term without words. The
    /// words need not share a value.</summary>
    public static bool InAny(string? term, params string?[] values) =>
        All(
            term ?? "",
            word =>
                values.Any(v =>
                    v is not null && v.Contains(word, StringComparison.OrdinalIgnoreCase)
                )
        );

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The words of a search for an amount. A '€' is no part of
    /// them, so the amount can be typed the way a list shows it.</summary>
    public static string[] OfAmount(string? term) => Of((term ?? "").Replace("€", ""));

    /// <summary>Whether every word of the term is found in the amount as
    /// shown ("1.234,56") or as typed without the thousands separator
    /// ("1234,56"), where a '.' in the word also stands for the decimal comma.
    /// So "1.234,56 €", "1.234,56", "1234,56", "1234.56", "12.50" and "12,5"
    /// all find their amount — true for a term without words. The browser's
    /// twin is <c>matchesAmount</c> in frontend/src/lib/searchWords.ts, which
    /// the item list's price search uses.</summary>
    public static bool InAmount(string? term, decimal amount)
    {
        var shown = amount.ToString("N2", German);
        var plain = shown.Replace(".", "");
        return OfAmount(term)
            .All(word =>
                shown.Contains(word, StringComparison.Ordinal)
                || plain.Contains(word.Replace('.', ','), StringComparison.Ordinal)
            );
    }
}
