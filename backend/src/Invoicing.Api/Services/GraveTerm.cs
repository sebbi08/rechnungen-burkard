using System.Globalization;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>A grave's term is counted in months: start and end are stored as
/// the first of their month, and the end may be missing. Shared by SaveGrave
/// and the import so both read the same text the same way.</summary>
public static class GraveTerm
{
    /// <summary>"2026-03", or a full date whose day is dropped ("2026-03-15"),
    /// so a client that still sends days keeps working.</summary>
    public static bool TryParseMonth(string? raw, out DateOnly month)
    {
        month = default;
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (
            DateOnly.TryParseExact(
                text,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
            || DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed
            )
        )
        {
            month = new DateOnly(parsed.Year, parsed.Month, 1);
            return true;
        }

        return false;
    }

    /// <summary>A member name only: Enum.TryParse alone would also take "3".</summary>
    public static bool TryParsePosition(string? raw, out GravePosition position)
    {
        position = default;
        return raw is not null
            && Enum.GetNames<GravePosition>().Contains(raw)
            && Enum.TryParse(raw, out position);
    }

    /// <summary>Whether two terms share at least one month. Both ends are
    /// inclusive; no end means no upper bound.</summary>
    public static bool Overlap(DateOnly startA, DateOnly? endA, DateOnly startB, DateOnly? endB) =>
        startA <= (endB ?? DateOnly.MaxValue) && startB <= (endA ?? DateOnly.MaxValue);

    public static string FormatMonth(DateOnly month) =>
        month.ToString("MM/yyyy", CultureInfo.InvariantCulture);
}
