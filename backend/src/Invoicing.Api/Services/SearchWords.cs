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
}
