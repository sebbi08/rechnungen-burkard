using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

internal sealed class InvoiceDocument : IDocument
{
    private readonly InvoiceDocumentData _data;
    private readonly bool _isCopy;
    private readonly DocumentTexts _texts;
    private readonly CultureInfo _culture;

    public InvoiceDocument(InvoiceDocumentData data, bool isCopy)
    {
        _data = data;
        _isCopy = isCopy;
        _texts = DocumentTexts.For(data.Language);
        _culture = _texts.Culture;
    }

    public DocumentMetadata GetMetadata() =>
        new()
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
                var stampFontSize = Math.Clamp(
                    48f * 20 / Math.Max(_data.StampWord.Length, 1),
                    24f,
                    72f
                );
                page.Foreground()
                    .AlignCenter()
                    .AlignMiddle()
                    .Rotate(45)
                    .Text(_data.StampWord)
                    .FontSize(stampFontSize)
                    .FontColor(Colors.Grey.Lighten2.WithAlpha(160));
            }

            page.Header()
                .Column(column =>
                {
                    column
                        .Item()
                        .Text(_texts.InvoiceTitle(_data.InvoiceNumber))
                        .FontSize(18)
                        .SemiBold();
                    column.Item().Text(_data.GeneratedAt.ToString("d", _culture));
                    column.Item().PaddingTop(10).Text(_data.RecipientName).SemiBold();
                    column.Item().Text(_data.RecipientAddress);
                });

            page.Content()
                .Column(column =>
                {
                    if (!string.IsNullOrEmpty(_data.ShareText))
                    {
                        column.Item().PaddingBottom(8).Text(_data.ShareText).SemiBold();
                    }

                    column
                        .Item()
                        .Table(table =>
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
                                header.Cell().Element(HeaderCell).Text(_texts.ColumnDescription);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(_texts.ColumnCount);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(_texts.ColumnUnitPrice);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(_texts.ColumnVat);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(_texts.ColumnTotal);
                            });

                            foreach (var line in _data.Lines)
                            {
                                table.Cell().Text(line.Description);
                                table.Cell().AlignRight().Text(line.Count.ToString(_culture));
                                table.Cell().AlignRight().Text(UnitPrice(line.UnitPrice));
                                table
                                    .Cell()
                                    .AlignRight()
                                    .Text(
                                        $"{line.VatRate.ToString("0.#", _culture)}{_texts.VatRateSuffix}"
                                    );
                                table.Cell().AlignRight().Text(Money(line.LineTotal));
                            }
                        });

                    column
                        .Item()
                        .PaddingTop(10)
                        .AlignRight()
                        .Column(summary =>
                        {
                            foreach (var vat in _data.VatSummaries)
                            {
                                summary.Item().Text(_texts.NetForRate(vat.Rate, vat.NetTotal));
                                summary.Item().Text(_texts.VatForRate(vat.Rate, vat.VatAmount));
                            }

                            summary
                                .Item()
                                .PaddingTop(6)
                                .Text(_texts.NetTotal(_data.NetTotal))
                                .SemiBold();
                            summary.Item().Text(_texts.VatTotal(_data.TotalVat)).SemiBold();
                            summary
                                .Item()
                                .Text(_texts.GrossTotal(_data.GrossTotal))
                                .SemiBold()
                                .FontSize(13);
                        });

                    if (!string.IsNullOrEmpty(_data.EndingText))
                    {
                        column.Item().PaddingTop(16).Text(_data.EndingText);
                    }
                });

            page.Footer()
                .AlignCenter()
                .Text(text =>
                {
                    text.Span(_texts.PageFooterPageOf + " ");
                    text.CurrentPageNumber();
                    text.Span(_texts.PageFooterOf);
                    text.TotalPages();
                });
        });
    }

    private string Money(decimal value) => value.ToString("N2", _culture);

    // at least 2, up to 3 decimals (e.g. 120,00 or 33,335) — unit prices are rounded
    // to 3 decimals so persisted line grosses stay exact to the cent.
    private string UnitPrice(decimal value) => value.ToString("#,0.00#", _culture);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);
}
