using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

/// <summary>The care list and the planting list: plain A4 working sheets,
/// without letterhead or fold marks. Every cemetery is a stack of pages of
/// its own, numbered from one. Cemeteries, graves and lines are printed in
/// the order <see cref="WorkListData"/> gives them.</summary>
internal sealed class WorkListDocument : IDocument
{
    private const float MarginMillimetre = 10;
    private const float FontSize = 9;
    private const float TwoColumnFontSize = 8;

    private const float ColumnGapMillimetre = 6;
    private const float CellGapMillimetre = 1.5f;

    // widths in millimetres, gap included, that hold their usual value at
    // 9 pt: "A-123b", "04/2015" in bold (which must never wrap), "1.234,56 €";
    // whatever runs longer wraps
    private const float NumberWidth = 14;
    private const float PositionWidth = 20;
    private const float MonthWidth = 15;
    private const float ContractWidth = 14;
    private const float AmountWidth = 18;

    // the same at 8 pt, in a column half as wide
    private const float TwoColumnNumberWidth = 11;
    private const float TwoColumnPositionWidth = 15;
    private const float TwoColumnMonthWidth = 13;
    private const float TwoColumnContractWidth = 11.5f;

    // the services variant's indent and its amount column, which also holds
    // the heading "Betrag je Halbjahr"
    private const float ServiceIndent = NumberWidth;
    private const float HalfYearAmountWidth = 32;

    private static readonly Color RuleColour = Colors.Grey.Lighten2;

    private readonly WorkListData _data;

    public WorkListDocument(WorkListData data) => _data = data;

    private string Title =>
        _data.Kind == WorkListKind.Planting
            ? DocumentTexts.PlantingListTitle(
                _data.Quarter
                    ?? throw new InvalidOperationException("A planting list names its quarter."),
                _data.Year
            )
            : DocumentTexts.CareListTitle(_data.Year);

    public DocumentMetadata GetMetadata()
    {
        var madeOn = _data.MadeOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return new()
        {
            Title = Title,
            Author = DocumentTexts.MetadataAuthor,
            Creator = DocumentTexts.MetadataAuthor,
            Producer = "QuestPDF",
            CreationDate = madeOn,
            ModifiedDate = madeOn,
        };
    }

    public void Compose(IDocumentContainer container)
    {
        for (var index = 0; index < _data.Cemeteries.Count; index++)
        {
            var cemetery = _data.Cemeteries[index];
            // the section spans the cemetery's pages, so its page numbers
            // count that stack alone
            var section = $"cemetery-{index}";
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(MarginMillimetre, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(FontSize));

                page.Header().Element(c => Header(c, cemetery.Name, section));
                page.Content()
                    .PaddingTop(2, Unit.Millimetre)
                    .Section(section)
                    .Element(c => Content(c, cemetery.Graves));
            });
        }
    }

    private void Header(IContainer container, string cemetery, string section) =>
        container.Column(column =>
        {
            column
                .Item()
                .BorderBottom(0.75f)
                .PaddingBottom(1.5f, Unit.Millimetre)
                .Row(row =>
                {
                    row.RelativeItem()
                        .Text(text =>
                        {
                            text.Span(Title).Bold().FontSize(12);
                            text.Span(" · ").FontSize(12);
                            text.Span(cemetery).FontSize(12);
                        });
                    row.AutoItem()
                        .AlignBottom()
                        .Text(text =>
                        {
                            text.Span(DocumentTexts.WorkListMadeOn(_data.MadeOn));
                            text.Span(" · " + DocumentTexts.PageWord + " ");
                            text.PageNumberWithinSection(section);
                            text.Span(DocumentTexts.PageOf);
                            text.TotalPagesWithinSection(section);
                        });
                });

            switch (_data.Kind)
            {
                case WorkListKind.CareWithServices:
                    column.Item().PaddingTop(1.5f, Unit.Millimetre).Element(ServicesHeading);
                    break;
                case WorkListKind.Planting:
                    column
                        .Item()
                        .PaddingTop(1.5f, Unit.Millimetre)
                        .Table(table =>
                        {
                            PlantingColumns(table);
                            PlantingHeading(table);
                        });
                    break;
            }
        });

    private void Content(IContainer container, IReadOnlyList<WorkListGrave> graves)
    {
        switch (_data.Kind)
        {
            case WorkListKind.CareGravesOnly:
                GravesInTwoColumns(container, graves);
                break;
            case WorkListKind.CareWithServices:
                GravesWithServices(container, graves);
                break;
            case WorkListKind.Planting:
                PlantingGraves(container, graves);
                break;
        }
    }

    // --- care list, graves only ---------------------------------------------

    /// <summary>One line per grave in two columns: the left column of a page
    /// fills first, then the right one; the last page shares its graves out
    /// between both.</summary>
    private static void GravesInTwoColumns(
        IContainer container,
        IReadOnlyList<WorkListGrave> graves
    ) =>
        container
            .DefaultTextStyle(x => x.FontSize(TwoColumnFontSize))
            .MultiColumn(columns =>
            {
                columns.Columns(2);
                columns.Spacing(ColumnGapMillimetre, Unit.Millimetre);
                columns.BalanceHeight();
                columns.Spacer().AlignCenter().LineVertical(0.5f).LineColor(RuleColour);
                columns
                    .Content()
                    .Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(TwoColumnNumberWidth, Unit.Millimetre);
                            c.RelativeColumn();
                            c.ConstantColumn(TwoColumnPositionWidth, Unit.Millimetre);
                            c.ConstantColumn(TwoColumnMonthWidth, Unit.Millimetre);
                            c.ConstantColumn(TwoColumnMonthWidth, Unit.Millimetre);
                            c.ConstantColumn(TwoColumnContractWidth, Unit.Millimetre);
                        });

                        // repeated at the top of every column
                        table.Header(header =>
                        {
                            foreach (
                                var heading in new[]
                                {
                                    DocumentTexts.WorkListColumnNumber,
                                    DocumentTexts.WorkListColumnSite,
                                    DocumentTexts.WorkListColumnPosition,
                                    DocumentTexts.WorkListColumnStart,
                                    DocumentTexts.WorkListColumnEnd,
                                    DocumentTexts.WorkListColumnContract,
                                }
                            )
                            {
                                header.Cell().Element(HeadingCell).Text(heading).Bold();
                            }
                        });

                        foreach (var grave in graves)
                        {
                            table.Cell().Element(Cell).Text(grave.GraveNumber);
                            table.Cell().Element(Cell).Text(grave.SiteName);
                            table.Cell().Element(Cell).Text(Position(grave));
                            table.Cell().Element(Cell).Text(t => Start(t, "", grave));
                            table.Cell().Element(Cell).Text(t => End(t, "", grave));
                            table.Cell().Element(Cell).Text(grave.ContractNumber ?? "");
                        }
                    });
            });

    // --- care list with services ---------------------------------------------

    private static void ServicesHeading(IContainer container) =>
        container
            .PaddingLeft(ServiceIndent, Unit.Millimetre)
            .BorderBottom(0.5f)
            .PaddingBottom(0.5f, Unit.Millimetre)
            .Row(row =>
            {
                row.RelativeItem().Text(DocumentTexts.WorkListColumnService).Bold();
                row.ConstantItem(HalfYearAmountWidth, Unit.Millimetre)
                    .AlignRight()
                    .Text(DocumentTexts.WorkListColumnHalfYearAmount)
                    .Bold();
            });

    /// <summary>A detail line per grave and its services indented under it;
    /// a grave moves to the next page as a whole when it fits there.</summary>
    private static void GravesWithServices(
        IContainer container,
        IReadOnlyList<WorkListGrave> graves
    ) =>
        container.Column(column =>
        {
            foreach (var grave in graves)
            {
                column
                    .Item()
                    .PreventPageBreak()
                    .BorderBottom(0.25f)
                    .BorderColor(RuleColour)
                    .PaddingVertical(1, Unit.Millimetre)
                    .Column(block =>
                    {
                        block.Item().Row(row => GraveDetailLine(row, grave));
                        foreach (var line in grave.Lines)
                        {
                            block
                                .Item()
                                .PaddingLeft(ServiceIndent, Unit.Millimetre)
                                .Row(row =>
                                {
                                    row.RelativeItem()
                                        .PaddingRight(CellGapMillimetre, Unit.Millimetre)
                                        .Text(
                                            DocumentTexts.WorkListService(
                                                line.Count,
                                                line.ItemNumber,
                                                line.Description,
                                                line.Quarter
                                            )
                                        );
                                    row.ConstantItem(HalfYearAmountWidth, Unit.Millimetre)
                                        .AlignRight()
                                        .Text(DocumentTexts.Amount(line.Amount));
                                });
                        }
                    });
            }
        });

    private static void GraveDetailLine(RowDescriptor row, WorkListGrave grave)
    {
        row.ConstantItem(NumberWidth, Unit.Millimetre)
            .PaddingRight(CellGapMillimetre, Unit.Millimetre)
            .Text(grave.GraveNumber)
            .Bold();
        row.RelativeItem().PaddingRight(CellGapMillimetre, Unit.Millimetre).Text(grave.SiteName);
        row.ConstantItem(PositionWidth + 5, Unit.Millimetre)
            .PaddingRight(CellGapMillimetre, Unit.Millimetre)
            .Text(Position(grave));
        row.ConstantItem(MonthWidth + 10, Unit.Millimetre)
            .Text(t => Start(t, DocumentTexts.WorkListColumnStart + " ", grave));
        row.ConstantItem(MonthWidth + 8, Unit.Millimetre)
            .Text(t => End(t, DocumentTexts.WorkListColumnEnd + " ", grave));
        row.ConstantItem(HalfYearAmountWidth, Unit.Millimetre)
            .AlignRight()
            .Text(
                grave.ContractNumber is { } contract
                    ? $"{DocumentTexts.WorkListColumnContract} {contract}"
                    : ""
            );
    }

    // --- planting list -------------------------------------------------------

    private static void PlantingColumns(TableDescriptor table) =>
        table.ColumnsDefinition(c =>
        {
            c.ConstantColumn(NumberWidth, Unit.Millimetre);
            c.RelativeColumn(3);
            c.ConstantColumn(PositionWidth, Unit.Millimetre);
            c.RelativeColumn(4);
            c.ConstantColumn(AmountWidth, Unit.Millimetre);
            c.ConstantColumn(ContractWidth, Unit.Millimetre);
            c.ConstantColumn(MonthWidth, Unit.Millimetre);
            c.ConstantColumn(MonthWidth, Unit.Millimetre);
        });

    /// <summary>The headings in the page header: a table of their own with
    /// the graves' column widths, so they stand once on every page.</summary>
    private static void PlantingHeading(TableDescriptor table)
    {
        foreach (
            var heading in new[]
            {
                DocumentTexts.WorkListColumnNumber,
                DocumentTexts.WorkListColumnSite,
                DocumentTexts.WorkListColumnPosition,
                DocumentTexts.WorkListColumnService,
            }
        )
        {
            table.Cell().Element(HeadingCell).Text(heading).Bold();
        }
        table
            .Cell()
            .Element(HeadingCell)
            .AlignRight()
            .Text(DocumentTexts.WorkListColumnAmount)
            .Bold();
        // the contract cells below are padded on the left
        table
            .Cell()
            .Element(HeadingCell)
            .PaddingLeft(CellGapMillimetre, Unit.Millimetre)
            .Text(DocumentTexts.WorkListColumnContract)
            .Bold();
        foreach (
            var heading in new[]
            {
                DocumentTexts.WorkListColumnStart,
                DocumentTexts.WorkListColumnEnd,
            }
        )
        {
            table.Cell().Element(HeadingCell).Text(heading).Bold();
        }
    }

    /// <summary>One line per service. The grave's own cells stand once, on its
    /// first line, and reach down beside all of its services, so a grave reads
    /// as one block; the block moves to the next page as a whole when it fits
    /// there.</summary>
    private static void PlantingGraves(IContainer container, IReadOnlyList<WorkListGrave> graves) =>
        container.Column(column =>
        {
            foreach (var grave in graves)
            {
                column
                    .Item()
                    .PreventPageBreak()
                    .BorderBottom(0.25f)
                    .BorderColor(RuleColour)
                    .PaddingVertical(0.5f, Unit.Millimetre)
                    .Table(table =>
                    {
                        PlantingColumns(table);
                        // a grave without a service still gets its line
                        var lines = (uint)Math.Max(grave.Lines.Count, 1);

                        IContainer GraveCell(uint position) =>
                            table.Cell().Row(1).Column(position).RowSpan(lines).Element(PlainCell);

                        GraveCell(1).Text(grave.GraveNumber);
                        GraveCell(2).Text(grave.SiteName);
                        GraveCell(3).Text(Position(grave));
                        GraveCell(6)
                            .PaddingLeft(CellGapMillimetre, Unit.Millimetre)
                            .Text(grave.ContractNumber ?? "");
                        GraveCell(7).Text(t => Start(t, "", grave));
                        GraveCell(8).Text(t => End(t, "", grave));

                        for (var i = 0; i < grave.Lines.Count; i++)
                        {
                            var line = grave.Lines[i];
                            var row = (uint)i + 1;
                            table
                                .Cell()
                                .Row(row)
                                .Column(4)
                                .Element(PlainCell)
                                .Text(
                                    DocumentTexts.WorkListService(
                                        line.Count,
                                        null,
                                        line.Description,
                                        null
                                    )
                                );
                            table
                                .Cell()
                                .Row(row)
                                .Column(5)
                                .Element(PlainCell)
                                .AlignRight()
                                .Text(DocumentTexts.Amount(line.Amount));
                        }
                    });
            }
        });

    // --- shared --------------------------------------------------------------

    private static string Position(WorkListGrave grave) =>
        DocumentTexts.GravePositionText(grave.FieldNumber, grave.Position);

    /// <summary>The start month after <paramref name="label"/>, bold when it
    /// falls inside the list's period.</summary>
    private static void Start(TextDescriptor text, string label, WorkListGrave grave) =>
        Month(text, label, grave.Start, grave.StartInPeriod);

    /// <summary>The end month after <paramref name="label"/>, or "–", bold
    /// when it falls inside the list's period.</summary>
    private static void End(TextDescriptor text, string label, WorkListGrave grave) =>
        Month(text, label, grave.End, grave.EndInPeriod);

    private static void Month(TextDescriptor text, string label, DateOnly? month, bool inPeriod)
    {
        if (label.Length > 0)
        {
            text.Span(label);
        }

        var span = text.Span(DocumentTexts.Month(month));
        if (inPeriod)
        {
            span.Bold();
        }
    }

    private static IContainer HeadingCell(IContainer container) =>
        container
            .BorderBottom(0.5f)
            .PaddingBottom(0.5f, Unit.Millimetre)
            .PaddingRight(CellGapMillimetre, Unit.Millimetre);

    /// <summary>A cell of the two-column list. A grave's line is never split:
    /// with a wrapped site name it moves to the next column as a whole.</summary>
    private static IContainer Cell(IContainer container) =>
        container
            .ShowEntire()
            .BorderBottom(0.25f)
            .BorderColor(RuleColour)
            .PaddingVertical(0.6f, Unit.Millimetre)
            .PaddingRight(CellGapMillimetre, Unit.Millimetre);

    private static IContainer PlainCell(IContainer container) =>
        container
            .PaddingVertical(0.3f, Unit.Millimetre)
            .PaddingRight(CellGapMillimetre, Unit.Millimetre);
}
