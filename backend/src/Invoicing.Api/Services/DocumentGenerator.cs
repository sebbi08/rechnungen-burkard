using Burkard.Invoicing.Documents;

namespace Burkard.Invoicing.Api.Services;

public interface IDocumentGenerator
{
    byte[] Generate(InvoiceDocumentData data, bool isCopy);
}

public sealed class QuestPdfDocumentGenerator : IDocumentGenerator
{
    public byte[] Generate(InvoiceDocumentData data, bool isCopy) =>
        DocumentService.GeneratePdf(data, isCopy);
}
