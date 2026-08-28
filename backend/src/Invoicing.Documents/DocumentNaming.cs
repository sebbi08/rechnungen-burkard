namespace Burkard.Invoicing.Documents;

/// <summary>File names of the generated document pair. The suffix is a fixed
/// marker rather than a translated word, so a bulk download stays recognisable
/// no matter which language the documents were generated in.</summary>
public static class DocumentNaming
{
    public const string CopySuffix = "-Kopie";

    public static string NormalFileName(string invoiceNumber) => $"{invoiceNumber}.pdf";

    public static string CopyFileName(string invoiceNumber) => $"{invoiceNumber}{CopySuffix}.pdf";
}
