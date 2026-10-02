using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

internal sealed class InvoiceDocument : IDocument
{
    private const float SideMarginMillimetre = 20;

    // The lower edge of a DIN lang envelope's window, measured from the sheet's top.
    private const float WindowBottomMillimetre = 90;

    private readonly InvoiceDocumentData _data;
    private readonly bool _isCopy;

    public InvoiceDocument(InvoiceDocumentData data, bool isCopy)
    {
        _data = data;
        _isCopy = isCopy;
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
            // The letterhead prints its own logo block and bank details, so the
            // margins that clear them are asymmetric rather than a flat 2 cm.
            page.MarginTop(Letterhead.HeaderHeightMillimetres, Unit.Millimetre);
            page.MarginBottom(Letterhead.FooterHeightMillimetres, Unit.Millimetre);
            page.MarginHorizontal(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            // Background is drawn per page, so every page of a multi-page invoice
            // gets the stationery.
            page.Background().Element(Letterhead.Compose);

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
                    column.Item().ShowOnce().Element(AddressBlock);
                    column.Item().SkipOnce().Element(ContinuationLine);
                });

            page.Content()
                .Column(column =>
                {
                    // The old share note above the table only ever applied when the
                    // lines were already the share (Share is null); the summary
                    // column prints the share block itself once Share is set, so the
                    // note would be redundant there.
                    if (_data.Share is null && !string.IsNullOrEmpty(_data.ShareText))
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
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .Text(DocumentTexts.ColumnDescription);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(DocumentTexts.ColumnCount);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(DocumentTexts.ColumnUnitPrice);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(DocumentTexts.ColumnVat);
                                header
                                    .Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text(DocumentTexts.ColumnTotal);
                            });

                            foreach (var line in _data.Lines)
                            {
                                table.Cell().Text(line.Description);
                                table
                                    .Cell()
                                    .AlignRight()
                                    .Text(line.Count.ToString(DocumentTexts.Culture));
                                table.Cell().AlignRight().Text(UnitPrice(line.UnitPrice));
                                table
                                    .Cell()
                                    .AlignRight()
                                    .Text(
                                        $"{line.VatRate.ToString("0.#", DocumentTexts.Culture)}{DocumentTexts.VatRateSuffix}"
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
                            // Only the invoice's own per-rate block, never both. On a
                            // grave invoice the 100 % rows are not what anyone is
                            // billed, and printing them beside the share's rows put
                            // two different amounts under one "MwSt. 7 %" label on a
                            // tax document, with nothing saying which is the invoiced
                            // tax. The per-rate split stays visible either way: the
                            // share block prints its own rows.
                            if (_data.Share is null)
                            {
                                foreach (var vat in _data.VatSummaries)
                                {
                                    summary
                                        .Item()
                                        .Text(DocumentTexts.NetForRate(vat.Rate, vat.NetTotal));
                                    summary
                                        .Item()
                                        .Text(DocumentTexts.VatForRate(vat.Rate, vat.VatAmount));
                                }
                            }

                            if (_data.Share is { } share)
                            {
                                // The grave's own lines print in full above; this
                                // recipient pays a share of them, worked out per rate
                                // from that share rather than from the 100 % figures.
                                summary
                                    .Item()
                                    .PaddingTop(6)
                                    .Text(DocumentTexts.NetSumTotal(_data.NetTotal))
                                    .SemiBold();
                                summary
                                    .Item()
                                    .Text(DocumentTexts.SharePart(share.Percent, share.NetTotal))
                                    .SemiBold();
                                foreach (var vat in share.VatSummaries)
                                {
                                    // with the base it was worked out from: the
                                    // lines above are the whole grave, so this is
                                    // the only place the recipient's own taxable
                                    // amount per rate appears
                                    summary
                                        .Item()
                                        .Text(
                                            DocumentTexts.VatForRateFromNet(
                                                vat.Rate,
                                                vat.NetTotal,
                                                vat.VatAmount
                                            )
                                        );
                                }

                                summary
                                    .Item()
                                    .Text(DocumentTexts.GrossTotal(share.GrossTotal))
                                    .SemiBold()
                                    .FontSize(13);
                            }
                            else
                            {
                                summary
                                    .Item()
                                    .PaddingTop(6)
                                    .Text(DocumentTexts.NetTotal(_data.NetTotal))
                                    .SemiBold();
                                summary
                                    .Item()
                                    .Text(DocumentTexts.VatTotal(_data.TotalVat))
                                    .SemiBold();
                                summary
                                    .Item()
                                    .Text(DocumentTexts.GrossTotal(_data.GrossTotal))
                                    .SemiBold()
                                    .FontSize(13);
                            }
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
                    text.Span(DocumentTexts.PageFooterPageOf + " ");
                    text.CurrentPageNumber();
                    text.Span(DocumentTexts.PageFooterOf);
                    text.TotalPages();
                });
        });
    }

    /// <summary>
    /// The first page's head: the recipient in the envelope's window, the invoice
    /// number below it.
    /// </summary>
    private void AddressBlock(IContainer container) =>
        container.Column(column =>
        {
            // The letterhead prints the sender line immediately above this, so the
            // recipient goes directly under it: folded for a DIN lang envelope, both
            // addresses then show through the window, which reveals the sheet from
            // 45 mm to 90 mm below its top edge.
            //
            // The address gets that whole zone whether it fills it or not, so the
            // invoice number always starts clear of the window instead of being sliced
            // by its edge on short addresses. A longer address pushes the number down
            // rather than being clipped.
            column
                .Item()
                .PaddingLeft(
                    Letterhead.AddressFieldLeftMillimetres - SideMarginMillimetre,
                    Unit.Millimetre
                )
                .MinHeight(
                    Letterhead.AddressFieldBottomMillimetres - Letterhead.HeaderHeightMillimetres,
                    Unit.Millimetre
                )
                .Column(recipient =>
                {
                    recipient.Item().Text(_data.RecipientName).SemiBold();
                    recipient.Item().Text(_data.RecipientAddress);
                });

            // just past the window's lower edge, so the number is never framed by it
            column
                .Item()
                .PaddingTop(
                    WindowBottomMillimetre + 2 - Letterhead.AddressFieldBottomMillimetres,
                    Unit.Millimetre
                )
                .Text(DocumentTexts.InvoiceTitle(_data.InvoiceNumber))
                .FontSize(18)
                .SemiBold();
            column
                .Item()
                .PaddingBottom(8, Unit.Millimetre)
                .Text(_data.GeneratedAt.ToString("d", DocumentTexts.Culture));
        });

    /// <summary>
    /// Every page after the first. Only the sheet that goes in the envelope needs the
    /// address block; the rest just have to say which invoice they belong to.
    /// </summary>
    private void ContinuationLine(IContainer container) =>
        container
            .PaddingBottom(6, Unit.Millimetre)
            .Text(text =>
            {
                text.Span(DocumentTexts.InvoiceTitle(_data.InvoiceNumber)).SemiBold();
                text.Span(" · ");
                text.Span(_data.GeneratedAt.ToString("d", DocumentTexts.Culture));
            });

    private string Money(decimal value) => value.ToString("N2", DocumentTexts.Culture);

    // Two decimals, like every other price on the page. A VAT-included price
    // used to keep a third so a line's gross stayed exact above count 1; it is
    // rounded to the cent and raised instead, so nothing here has more.
    private string UnitPrice(decimal value) => value.ToString("#,0.00", DocumentTexts.Culture);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);
}
