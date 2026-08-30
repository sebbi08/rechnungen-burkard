using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

/// <summary>
/// The two creases that fold an A4 sheet into a DIN lang envelope, marked in the left
/// margin so the fold can be made by hand without measuring.
/// </summary>
/// <remarks>
/// Creasing at 105 mm and 210 mm leaves panels of 105, 105 and 87 mm. The top panel
/// stays flat and faces out; the other two fold behind it, and because neither is
/// taller than it, the packet's top edge is the sheet's top edge. The envelope's
/// window then reveals exactly 45 mm to 90 mm of the sheet, which is what the address
/// field is laid out for.
///
/// That registration is the whole point, and it is easy to get wrong: creases at
/// 87 mm and 192 mm also add up to 297 mm and also fold into a 105 mm packet, but the
/// front panel is then 18 mm shorter than the one behind it. The taller panel stands
/// proud of the sheet's top edge, the packet sits 18 mm high in the envelope, and the
/// address — printed exactly where it belongs — arrives sunk to the bottom of the
/// window with the letterhead's service list peering in above it.
/// </remarks>
internal static class FoldMarks
{
    public const float FirstMillimetre = 105;
    public const float SecondMillimetre = 210;

    // Far enough from the paper edge that a laser printer's unprintable border cannot
    // swallow them, and far short of the 20 mm text margin.
    private const float LeftMillimetre = 8;
    private const float LengthMillimetre = 5;

    private const string Colour = "#B4B4B4";

    /// <param name="bandTopMillimetre">
    /// Where the containing band starts, measured from the top of the page.
    /// </param>
    public static void Compose(IContainer container, float bandTopMillimetre) =>
        container.Column(column =>
        {
            column.Item().Height(FirstMillimetre - bandTopMillimetre, Unit.Millimetre);
            column.Item().Element(Mark);
            column.Item().Height(SecondMillimetre - FirstMillimetre, Unit.Millimetre);
            column.Item().Element(Mark);
        });

    private static void Mark(IContainer container) =>
        container
            .PaddingLeft(LeftMillimetre, Unit.Millimetre)
            .Width(LengthMillimetre, Unit.Millimetre)
            .LineHorizontal(0.5f)
            .LineColor(Colour);
}
