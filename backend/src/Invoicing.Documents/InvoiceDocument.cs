using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

internal sealed class InvoiceDocument : IDocument
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly InvoiceDocumentData _data;
    private readonly bool _isCopy;

    public InvoiceDocument(InvoiceDocumentData data, bool isCopy)
    {
        _data = data;
        _isCopy = isCopy;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Invoice {_data.InvoiceNumber}",
        Author = "Burkard Invoicing",
        Creator = "Burkard Invoicing",
        Producer = "QuestPDF",
        CreationDate = _data.GeneratedAt,
        ModifiedDate = _data.GeneratedAt,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            if (_isCopy)
            {
                // scale the stamp down for very long words so it always fits.
                // alignment wraps the rotation: QuestPDF's Rotate pivots around the
                // measured content center, so AlignCenter/AlignMiddle must come first,
                // otherwise the rotated stamp lands outside the page and is culled.
                var stampFontSize = Math.Clamp(48f * 20 / Math.Max(_data.StampWord.Length, 1), 24f, 72f);
                page.Foreground()
                    .AlignCenter()
                    .AlignMiddle()
                    .Rotate(45)
                    .Text(_data.StampWord)
                    .FontSize(stampFontSize)
                    .FontColor(Colors.Grey.Lighten2.WithAlpha(160));
            }

            page.Header().Column(column =>
            {
                column.Item().Text(DocumentTexts.InvoiceTitle(_data.InvoiceNumber)).FontSize(18).SemiBold();
                column.Item().Text(_data.GeneratedAt.ToString("dd.MM.yyyy"));
                column.Item().PaddingTop(10).Text(_data.RecipientName).SemiBold();
                column.Item().Text(_data.RecipientAddress);
            });

            page.Content().Column(column =>
            {
                if (!string.IsNullOrEmpty(_data.ShareText))
                {
                    column.Item().PaddingBottom(8).Text(_data.ShareText).SemiBold();
                }

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(80);
                        cols.ConstantColumn(50);
                        cols.ConstantColumn(80);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text(DocumentTexts.ColumnDescription);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnCount);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnUnitPrice);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnVat);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnTotal);
                    });

                    foreach (var line in _data.Lines)
                    {
                        table.Cell().Text(line.Description);
                        table.Cell().AlignRight().Text(line.Count.ToString());
                        table.Cell().AlignRight().Text(Money(line.UnitPrice));
                        table.Cell().AlignRight().Text($"{line.VatRate.ToString("0.#", German)}{DocumentTexts.VatRateSuffix}");
                        table.Cell().AlignRight().Text(Money(line.LineTotal));
                    }
                });

                column.Item().PaddingTop(10).AlignRight().Column(summary =>
                {
                    foreach (var vat in _data.VatSummaries)
                    {
                        summary.Item().Text(DocumentTexts.NetForRate(vat.Rate, vat.NetTotal));
                        summary.Item().Text(DocumentTexts.VatForRate(vat.Rate, vat.VatAmount));
                    }

                    summary.Item().PaddingTop(6).Text(DocumentTexts.NetTotal(_data.NetTotal)).SemiBold();
                    summary.Item().Text(DocumentTexts.VatTotal(_data.TotalVat)).SemiBold();
                    summary.Item().Text(DocumentTexts.GrossTotal(_data.GrossTotal)).SemiBold().FontSize(13);
                });

                if (!string.IsNullOrEmpty(_data.EndingText))
                {
                    column.Item().PaddingTop(16).Text(_data.EndingText);
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(DocumentTexts.PageFooterPageOf + " ");
                text.CurrentPageNumber();
                text.Span(DocumentTexts.PageFooterOf);
                text.TotalPages();
            });
        });
    }

    private static string Money(decimal value) => value.ToString("N2", German);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);
}
