using System.Globalization;

namespace Burkard.Invoicing.Domain;

/// <summary>Sorts grave numbers the way a person reads them: runs of digits
/// compare as numbers ("A-2" before "A-10"), everything else by German
/// collation without regard to case. Digit runs are never parsed, so a number
/// of any length sorts correctly. Strings that are equal in every run fall back
/// to an ordinal comparison, which makes the order total.</summary>
public sealed class GraveNumberOrder : IComparer<string>
{
    public static GraveNumberOrder Instance { get; } = new();

    private static readonly CompareInfo German = CultureInfo.GetCultureInfo("de-DE").CompareInfo;

    private GraveNumberOrder() { }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0,
            j = 0;
        while (i < x.Length && j < y.Length)
        {
            var left = Run(x, i);
            var right = Run(y, j);
            var a = x.AsSpan(i, left);
            var b = y.AsSpan(j, right);
            var result =
                char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j])
                    ? CompareDigits(a, b)
                    : German.Compare(a, b, CompareOptions.IgnoreCase);
            if (result != 0)
            {
                return result;
            }

            i += left;
            j += right;
        }

        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(x, y);
    }

    private static int Run(string text, int start)
    {
        var digits = char.IsAsciiDigit(text[start]);
        var end = start + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]) == digits)
        {
            end++;
        }

        return end - start;
    }

    private static int CompareDigits(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);
    }
}
