using System.Globalization;

namespace Burkard.Invoicing.Api.Audit;

/// <summary>How an <see cref="AuditChange"/> writes its values: invariant, so
/// the frontend can format them like the rest of the app.</summary>
public static class AuditValues
{
    /// <summary>"105.00"; more places only where the price has them.</summary>
    public static string? Money(decimal? value) =>
        value?.ToString("0.00##", CultureInfo.InvariantCulture);

    /// <summary>"2040-12".</summary>
    public static string? Month(DateOnly? value) =>
        value?.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>"2026-11-26".</summary>
    public static string? Date(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? Int(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>"&lt;rootId&gt;:&lt;invoiceNumber&gt;", so the Verlauf can link to the
    /// chain and name the number.</summary>
    public static string Invoice(long rootId, string invoiceNumber) =>
        $"{rootId.ToString(CultureInfo.InvariantCulture)}:{invoiceNumber}";
}
