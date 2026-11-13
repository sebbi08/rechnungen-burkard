using QuestPDF.Fluent;

namespace Burkard.Invoicing.Documents;

/// <summary>The care list and the planting list as PDF.</summary>
public static class WorkListDocuments
{
    public static byte[] Generate(WorkListData data)
    {
        // as in DocumentService: the host sets the license at startup, tests and
        // other callers rely on this
        if (QuestPDF.Settings.License is null)
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        }

        return new WorkListDocument(data).GeneratePdf();
    }
}
