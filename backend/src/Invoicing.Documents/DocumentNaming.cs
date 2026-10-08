namespace Burkard.Invoicing.Documents;

/// <summary>File names of the generated document pair. The copy's suffix is
/// fixed rather than the configurable stamp word, so a copy's file name stays
/// the same whatever the stamp on it says.</summary>
public static class DocumentNaming
{
    public const string CopySuffix = "-Kopie";

    public static string NormalFileName(string invoiceNumber) => $"{invoiceNumber}.pdf";

    public static string CopyFileName(string invoiceNumber) => $"{invoiceNumber}{CopySuffix}.pdf";
}
