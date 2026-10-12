namespace Burkard.Invoicing.Api.Services;

/// <summary>A recipient's e-mail address. It is optional, and checked only for
/// the obvious -- no whitespace, one "@" with text on both sides, a dot in the
/// domain -- since nothing is sent to it yet. The recipient section and the
/// import share it, so both accept the same addresses.</summary>
public static class EmailAddress
{
    /// <summary>The address trimmed, or null for none: an empty one is no
    /// address.</summary>
    public static string? Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    public static bool IsPlausible(string address)
    {
        if (address.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parts = address.Split('@');
        if (parts.Length != 2 || parts[0].Length == 0)
        {
            return false;
        }

        var domain = parts[1];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
