using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Builds an invoice's document pair. Both are produced in one step so a
/// half-generated pair can never be persisted (FR-043).</summary>
public sealed class InvoiceDocumentFactory
{
    private readonly DocumentDataBuilder _dataBuilder;
    private readonly IDocumentGenerator _documents;

    public InvoiceDocumentFactory(DocumentDataBuilder dataBuilder, IDocumentGenerator documents)
    {
        _dataBuilder = dataBuilder;
        _documents = documents;
    }

    public List<Document> Build(Invoice invoice, DateTime generatedAt)
    {
        var data = _dataBuilder.Build(invoice);
        return new List<Document>
        {
            new()
            {
                Kind = DocumentKind.Normal,
                FileName = DocumentNaming.NormalFileName(invoice.InvoiceNumber),
                Content = _documents.Generate(data, isCopy: false),
                GeneratedAt = generatedAt,
            },
            new()
            {
                Kind = DocumentKind.Copy,
                FileName = DocumentNaming.CopyFileName(invoice.InvoiceNumber),
                Content = _documents.Generate(data, isCopy: true),
                GeneratedAt = generatedAt,
            },
        };
    }
}
