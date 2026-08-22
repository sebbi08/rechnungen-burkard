using System.Globalization;

namespace Burkard.Invoicing.Domain;

public static class InvoiceNumberFormatter
{
    private const string RevisionMarker = "-R";

    public static string ForOriginal(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}-{sequence:D4}");

    public static string ForRevision(string baseNumber, int revisionNumber) =>
        $"{baseNumber}{RevisionMarker}{revisionNumber}";

    public static string StripRevision(string number)
    {
        var index = number.LastIndexOf(RevisionMarker, StringComparison.Ordinal);
        return index >= 0 ? number[..index] : number;
    }
}
