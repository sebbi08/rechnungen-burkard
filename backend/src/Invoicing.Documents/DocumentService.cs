using QuestPDF.Fluent;

namespace Burkard.Invoicing.Documents;

public static class DocumentService
{
    public static byte[] GeneratePdf(InvoiceDocumentData data, bool isCopy)
    {
        // The host application sets the QuestPDF license in Program.cs before startup;
        // ensure a license is set here too so library consumers (tests, jobs) don't crash.
        if (QuestPDF.Settings.License is null)
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        }

        var document = new InvoiceDocument(data, isCopy);
        return document.GeneratePdf();
    }
}
