using QuestPDF.Elements.Table;
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
            // a browser shows the title in the tab of an opened PDF: German,
            // like everything else on the document
            Title = _data.IsQuote
                ? DocumentTexts.QuoteTitle(_data.InvoiceNumber)
                : $"{DocumentTexts.MetadataTitle} {_data.InvoiceNumber}",
            Author = DocumentTexts.MetadataAuthor,
            Creator = DocumentTexts.MetadataAuthor,
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
                    column.Item().ShowOnce().Element(FirstPageHead);
                    column.Item().SkipOnce().Element(ContinuationLine);
                });

            page.Content()
                .Column(column =>
                {
                    // The old share note above the table only ever applied when the
                    // lines were already the share (Share is null); once Share is
                    // set, the VAT block bills the share and says so itself.
                    if (_data.Share is null && !string.IsNullOrEmpty(_data.ShareText))
                    {
                        column.Item().PaddingBottom(8).Text(_data.ShareText).SemiBold();
                    }

                    column.Item().Table(LineTable);

                    if (!string.IsNullOrEmpty(_data.EndingText))
                    {
                        column.Item().PaddingTop(16).Text(_data.EndingText);
                    }

                    // At the foot of the last page, as on the old stationery: the
                    // item takes the rest of the page and puts the block at its
                    // bottom. ShowEntire moves it to a new page as a whole when it
                    // does not fit under the lines.
                    column
                        .Item()
                        .ExtendVertical()
                        .AlignBottom()
                        .ShowEntire()
                        .PaddingTop(10)
                        .Element(VatBlock);
                });
        });
    }

    // The details start right of the DIN lang window's edge at 110 mm.
    private const float DetailsWidthMillimetre = 72;

    /// <summary>The first page's head: the recipient in the envelope's window,
    /// the invoice's details beside it.</summary>
    private void FirstPageHead(IContainer container) =>
        container
            .PaddingBottom(6, Unit.Millimetre)
            .Row(row =>
            {
                // The letterhead prints the sender line immediately above this, so
                // the recipient goes directly under it: folded for a DIN lang
                // envelope, both addresses then show through the window, which
                // reveals the sheet from 45 mm to 90 mm below its top edge. The
                // row is at least that tall, so the table always starts clear of
                // the window; a longer address pushes it down instead.
                row.RelativeItem()
                    .PaddingLeft(
                        Letterhead.AddressFieldLeftMillimetres - SideMarginMillimetre,
                        Unit.Millimetre
                    )
                    .MinHeight(
                        WindowBottomMillimetre + 2 - Letterhead.HeaderHeightMillimetres,
                        Unit.Millimetre
                    )
                    .Column(recipient =>
                    {
                        recipient.Item().Text(_data.RecipientName).SemiBold();
                        recipient.Item().Text(_data.RecipientAddress);
                    });
                row.ConstantItem(DetailsWidthMillimetre, Unit.Millimetre).Element(Details);
            });

    private void Details(IContainer container) =>
        container.Column(column =>
        {
            column
                .Item()
                .PaddingBottom(2, Unit.Millimetre)
                .Text(_data.IsQuote ? DocumentTexts.QuoteHeading : DocumentTexts.Title);
            column
                .Item()
                .Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(28, Unit.Millimetre);
                        cols.RelativeColumn();
                    });

                    void Row(string label, string value)
                    {
                        table.Cell().Element(DetailCell).Text(label);
                        table.Cell().Element(DetailCell).Text(value);
                    }

                    Row(DocumentTexts.LabelNumber, _data.InvoiceNumber);
                    if (!string.IsNullOrWhiteSpace(_data.RecipientNumber))
                    {
                        Row(DocumentTexts.LabelCustomerNumber, _data.RecipientNumber);
                    }

                    if (!string.IsNullOrWhiteSpace(_data.GraveNumber))
                    {
                        Row(DocumentTexts.LabelGraveNumber, _data.GraveNumber);
                    }

                    Row(
                        DocumentTexts.LabelDate,
                        _data.GeneratedAt.ToString("d", DocumentTexts.Culture)
                    );
                    table.Cell().Element(DetailCell).Text(DocumentTexts.LabelPage);
                    table
                        .Cell()
                        .Element(DetailCell)
                        .Text(text =>
                        {
                            text.CurrentPageNumber();
                            text.Span(DocumentTexts.PageOf);
                            text.TotalPages();
                        });
                });
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
                text.Span(
                        _data.IsQuote
                            ? DocumentTexts.QuoteTitle(_data.InvoiceNumber)
                            : DocumentTexts.InvoiceTitle(_data.InvoiceNumber)
                    )
                    .SemiBold();
                if (!string.IsNullOrWhiteSpace(_data.RecipientNumber))
                {
                    text.Span($" · {DocumentTexts.CustomerNumberShort} {_data.RecipientNumber}");
                }

                text.Span($" · {DocumentTexts.PageWord} ");
                text.CurrentPageNumber();
                text.Span(DocumentTexts.PageOf);
                text.TotalPages();
            });

    private void LineTable(TableDescriptor table)
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(16, Unit.Millimetre); // item number
            cols.RelativeColumn(); // description
            cols.ConstantColumn(16, Unit.Millimetre); // count
            cols.ConstantColumn(22, Unit.Millimetre); // unit price
            cols.ConstantColumn(24, Unit.Millimetre); // total
            cols.ConstantColumn(20, Unit.Millimetre); // VAT rate
        });

        table.Header(header =>
        {
            header.Cell().Element(HeaderCell);
            header.Cell().Element(HeaderCell).Text(DocumentTexts.ColumnDescription);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnCount);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnUnitPrice);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnTotal);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnVat);
        });

        var site = SiteLines();
        for (var i = 0; i < site.Count; i++)
        {
            table.Cell();
            table
                .Cell()
                .ColumnSpan(5)
                .PaddingTop(i == 0 ? 2 : 0, Unit.Millimetre)
                .PaddingBottom(i == site.Count - 1 ? 3 : 0, Unit.Millimetre)
                .Text(site[i])
                .SemiBold();
        }

        // the free text starts where the descriptions start, not under the
        // item numbers, and may run on under the figures
        if (!string.IsNullOrWhiteSpace(_data.IntroText))
        {
            table.Cell();
            table
                .Cell()
                .ColumnSpan(5)
                .PaddingTop(site.Count == 0 ? 2 : 0, Unit.Millimetre)
                .PaddingBottom(3, Unit.Millimetre)
                .Text(_data.IntroText);
        }

        foreach (var line in _data.Lines)
        {
            // A VAT-included line prints the gross it was entered as. A grave
            // refuses a gross that does not survive the conversion to net, so
            // there the unit price comes back unchanged; an ordinary invoice
            // does not, and 3 x 10,00 at 19 % can bill 29,99. The line total
            // therefore prints what is billed, the net plus its own VAT, so
            // the line agrees with the VAT block.
            var unitPrice = line.PriceIncludesVat
                ? decimal.Round(
                    line.UnitPrice * (1m + line.VatRate / 100m),
                    2,
                    MidpointRounding.AwayFromZero
                )
                : line.UnitPrice;
            var total = line.PriceIncludesVat ? line.LineTotal + line.VatAmount : line.LineTotal;
            var rate = DocumentTexts.VatRate(line.VatRate);

            table.Cell().Text(line.ItemNumber);
            table.Cell().Text(line.Description);
            table.Cell().AlignRight().Text(line.Count.ToString(DocumentTexts.Culture));
            table.Cell().AlignRight().Text(UnitPrice(unitPrice));
            table.Cell().AlignRight().Text(Money(total));
            table
                .Cell()
                .AlignRight()
                .Text(line.PriceIncludesVat ? $"{rate} {DocumentTexts.VatIncluded}" : rate);

            // under the description and only in its column, a little smaller so
            // it reads as a note on the line rather than a line of its own
            if (!string.IsNullOrWhiteSpace(line.Comment))
            {
                table.Cell();
                table.Cell().PaddingBottom(1, Unit.Millimetre).Text(line.Comment).FontSize(9);
                table.Cell().ColumnSpan(4);
            }
        }
    }

    /// <summary>The grave site lines above the items; each one only when there
    /// is something to print on it.</summary>
    private List<string> SiteLines()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_data.GraveSiteName))
        {
            lines.Add(DocumentTexts.GraveSite(_data.GraveSiteName));
        }

        var shortForm = DocumentTexts.PositionShortForm(_data.GravePosition);
        if (!string.IsNullOrWhiteSpace(_data.GraveFieldNumber) || shortForm is not null)
        {
            lines.Add(DocumentTexts.GraveField(_data.GraveFieldNumber, shortForm));
        }

        return lines;
    }

    /// <summary>Net and VAT per rate on the left, the totals on the right. On a
    /// grave invoice split across recipients it bills this recipient's share,
    /// and a line above it says what that is a share of.</summary>
    private void VatBlock(IContainer container)
    {
        var summaries = _data.Share?.VatSummaries ?? _data.VatSummaries;
        var net = _data.Share?.NetTotal ?? _data.NetTotal;
        var vat = _data.Share?.VatTotal ?? _data.TotalVat;
        var gross = _data.Share?.GrossTotal ?? _data.GrossTotal;

        container.Column(column =>
        {
            if (_data.Share is { } share)
            {
                column
                    .Item()
                    .PaddingBottom(2, Unit.Millimetre)
                    .Text(DocumentTexts.ShareLine(_data.NetTotal, share.Percent));
            }

            column
                .Item()
                .BorderTop(1)
                .BorderColor(Colors.Grey.Darken2)
                .PaddingTop(2)
                .Row(row =>
                {
                    row.RelativeItem()
                        .Table(rates =>
                        {
                            rates.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(16, Unit.Millimetre);
                                foreach (var _ in summaries)
                                {
                                    cols.RelativeColumn();
                                }
                            });

                            rates.Cell();
                            foreach (var s in summaries)
                            {
                                rates
                                    .Cell()
                                    .AlignRight()
                                    .Text(DocumentTexts.VatRateHeading(s.Rate));
                            }

                            rates.Cell().Text(DocumentTexts.NetRow);
                            foreach (var s in summaries)
                            {
                                rates.Cell().AlignRight().Text(Money(s.NetTotal));
                            }

                            rates.Cell().Text(DocumentTexts.VatRow);
                            foreach (var s in summaries)
                            {
                                rates.Cell().AlignRight().Text(Money(s.VatAmount));
                            }
                        });

                    row.ConstantItem(10, Unit.Millimetre);

                    row.ConstantItem(60, Unit.Millimetre)
                        .Table(totals =>
                        {
                            totals.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn();
                                cols.ConstantColumn(24, Unit.Millimetre);
                            });

                            totals.Cell();
                            totals.Cell().AlignRight().Text(DocumentTexts.Currency).SemiBold();
                            totals.Cell().Text(DocumentTexts.NetAmount);
                            totals.Cell().AlignRight().Text(Money(net));
                            totals.Cell().Text(DocumentTexts.VatAmount);
                            totals.Cell().AlignRight().Text(Money(vat));
                            totals.Cell().Text(DocumentTexts.GrossAmount).SemiBold();
                            totals.Cell().AlignRight().Text(Money(gross)).SemiBold();
                        });
                });
        });
    }

    private static IContainer DetailCell(IContainer container) =>
        container.PaddingBottom(1.5f, Unit.Millimetre);

    private string Money(decimal value) => value.ToString("N2", DocumentTexts.Culture);

    // Two decimals, like every other price on the page. A VAT-included price
    // used to keep a third so a line's gross stayed exact above count 1; it is
    // rounded to the cent and raised instead, so nothing here has more.
    private string UnitPrice(decimal value) => value.ToString("#,0.00", DocumentTexts.Culture);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);
}
