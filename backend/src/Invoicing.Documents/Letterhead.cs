using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

/// <summary>
/// The company stationery every invoice page is printed onto.
/// </summary>
/// <remarks>
/// Rebuilt from the scan kept in docs/, rather than carried as the scan itself:
/// everything that is type is set as text, and only the artwork that cannot be —
/// the rose cluster with the Burkard script it overlaps, and the rose beside the
/// bank details — is embedded as an image. That is worth roughly 350 kB on every
/// document, and it leaves behind the scan's skew, its off-white cast and the
/// scanner bed visible past the paper edge along the bottom.
///
/// Every measurement below is in millimetres from the top-left of the A4 page, read
/// off docs/Document_20220904_0001.jpg (2483x3511 at 300 dpi, so 1 mm = 11.811 px).
/// </remarks>
internal static class Letterhead
{
    // What the invoice's own page margins must be to clear the two printed bands,
    // which end at 49 mm and start at 268 mm — a few millimetres of air either way.
    public const float HeaderHeightMillimetres = 55;
    public const float FooterHeightMillimetres = 35;

    private const string Blue = "#3C60A8";
    private const string Grey = "#909090";

    // Where the address field sits, for the letterhead's sender line and the invoice's
    // recipient alike. A DIN lang window starts 20 mm from the left and 45 mm down;
    // both are kept 5 mm clear of that so folding and paper feed tolerances cannot
    // push a line under the envelope's edge.
    public const float AddressFieldLeftMillimetres = 25;
    public const float AddressFieldTopMillimetres = 50;

    // The first crease runs at 87 mm, so the field stops short of it.
    public const float AddressFieldBottomMillimetres = 85;

    // Tall enough to hold the header's own content — the sender line now ends near
    // 53 mm — with room to spare: a band that overflows paginates the background
    // instead of repeating it, and every page but the first loses the letterhead.
    private const float HeaderBand = 58;
    private const float FooterBand = 30;
    private const float PageHeight = 297;

    private static readonly Image Logo = Load("logo.png");
    private static readonly Image Rose = Load("rose.png");

    public static void Compose(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Height(HeaderBand, Unit.Millimetre).Element(Header);
            // the invoice's own area; the three bands add up to the full page, so the
            // footer lands on the bottom edge. Keep them exact: a background that
            // overflows paginates instead of repeating, and every page but the first
            // then loses the letterhead entirely.
            column
                .Item()
                .Height(PageHeight - HeaderBand - FooterBand, Unit.Millimetre)
                .Element(c => FoldMarks.Compose(c, HeaderBand));
            column.Item().Height(FooterBand, Unit.Millimetre).Element(Footer);
        });

    private static void Header(IContainer container) =>
        container.Column(column =>
        {
            column
                .Item()
                .PaddingTop(7.03f, Unit.Millimetre)
                .Row(row =>
                {
                    // 88.98 mm is where the logo starts, so the service list gets
                    // everything to its left and the logo its measured width.
                    row.ConstantItem(88.98f, Unit.Millimetre)
                        .PaddingLeft(14.5f, Unit.Millimetre)
                        .PaddingTop(CapOffset(9.48f - 7.03f, 9), Unit.Millimetre)
                        .Column(list =>
                        {
                            foreach (var line in ServiceList)
                            {
                                list.Item()
                                    .Height(7.02f, Unit.Millimetre)
                                    .Text(line)
                                    .FontSize(9)
                                    .SemiBold()
                                    .FontColor(Grey);
                            }
                        });
                    row.ConstantItem(118.28f, Unit.Millimetre).Image(Logo).UseOriginalImage();
                });

            // The sender line sits just under the logo. The scan has it at 20.66 mm and
            // 45.80 mm, hard against the envelope window's corner; it is nudged into the
            // address field's inset instead so a crooked fold cannot cut it off.
            column
                .Item()
                .PaddingTop(
                    CapOffset(AddressFieldTopMillimetres - 7.03f - 38.35f, 8),
                    Unit.Millimetre
                )
                .PaddingLeft(AddressFieldLeftMillimetres, Unit.Millimetre)
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Blue));
                    text.Span("Blumen Burkard").SemiBold();
                    text.Span(" | Landwehrstraße 26 | 67346 Speyer");
                });
        });

    private static void Footer(IContainer container) =>
        container.Column(column =>
        {
            // The rule stops short of the rose rather than running the page width.
            column
                .Item()
                .PaddingTop(268.40f - (PageHeight - FooterBand), Unit.Millimetre)
                .PaddingLeft(12.70f, Unit.Millimetre)
                .PaddingRight(210 - 175.85f, Unit.Millimetre)
                .LineHorizontal(0.7f)
                .LineColor(Blue);

            column
                .Item()
                .PaddingTop(CapOffset(270.90f - 268.40f, 8.5f), Unit.Millimetre)
                .Row(row =>
                {
                    row.ConstantItem(14.73f, Unit.Millimetre);
                    row.ConstantItem(49.62f, Unit.Millimetre).Element(c => Lines(c, Owner, true));
                    row.ConstantItem(58.42f, Unit.Millimetre).Element(c => Lines(c, Sparkasse));
                    row.ConstantItem(55.43f, Unit.Millimetre).Element(c => Lines(c, Volksbank));
                    row.ConstantItem(22.27f, Unit.Millimetre)
                        .PaddingTop(276.40f - 270.90f, Unit.Millimetre)
                        .Image(Rose)
                        .UseOriginalImage();
                });
        });

    // The six footer rows, at the heights the letterhead sets them; an empty string
    // leaves a row blank in a column that has nothing on that line. They are tighter
    // than 8.5 pt type sets by default, so the text gets a line height that fits
    // inside them — otherwise the band overflows, and an overflowing background
    // paginates instead of repeating, taking the whole letterhead with it.
    private static readonly float[] RowHeights = { 3.40f, 3.50f, 4.90f, 3.90f, 3.00f, 3.50f };
    private const float FooterLineHeight = 0.9f;

    private static readonly string[] ServiceList =
    {
        "Schnittblumen | Topfpflanzen | Kränze |",
        "Fleurop-Service | Dekoration | Grabanlagen |",
        "Grabpflege | Gartenanlage & Pflege |",
        "Treuhandstelle der Friedhofsgen.",
    };

    private static readonly string[] Owner =
    {
        "Inhaber: Guido Burkard",
        "Landwehrstraße 26",
        "67346 Speyer",
        "Tel.:      06232 - 4 33 20",
        "Fax:      06232 - 4 09 22",
        "burkard-speyer@t-online.de",
    };

    private static readonly string[] Sparkasse =
    {
        "Sparkasse Vorderpfalz",
        "IBAN DE38 5455 0010 0380 0922 70",
        "BIC    LUHSDE6AXXX",
        "Steuer-Nr.: 41/024/7006/5",
        "",
        "www.blumen-burkard.de",
    };

    private static readonly string[] Volksbank =
    {
        "Volksbank Kur- und Rheinpfalz eG",
        "IBAN DE75 5479 0000 0001 4167 90",
        "BIC    GENODE61SPE",
    };

    private static void Lines(IContainer container, string[] lines, bool bold = false) =>
        container.Column(column =>
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var item = column.Item().Height(RowHeights[i], Unit.Millimetre);
                if (lines[i].Length == 0)
                {
                    continue;
                }

                var text = item.Text(lines[i])
                    .FontSize(8.5f)
                    .FontColor(Blue)
                    .LineHeight(FooterLineHeight);
                // the owner block and the web address are the letterhead's bold ones
                if (bold || lines[i].StartsWith("www.", StringComparison.Ordinal))
                {
                    text.SemiBold();
                }
            }
        });

    // QuestPDF starts a line box at the font's ascender, which stands above the cap
    // height the scan was measured at. For Lato the two are 0.27 em apart, so a cap
    // top measured at y needs the box placed that much higher.
    private static float CapOffset(float millimetresBelow, float fontSizePoints) =>
        Math.Max(0, millimetresBelow - 0.27f * fontSizePoints * 25.4f / 72f);

    private static Image Load(string file)
    {
        var name = $"Burkard.Invoicing.Documents.Assets.{file}";
        using var stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");

        return Image.FromStream(stream);
    }
}
