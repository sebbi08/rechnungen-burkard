namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>The four CSV files an import archive is made of, with the header row
/// each one must carry. The template download and the validator share this
/// definition, so a downloaded template always validates.</summary>
public static class ImportArchiveLayout
{
    public const string RecipientsFile = "recipients.csv";
    public const string ItemsFile = "items.csv";
    public const string GravesFile = "graves.csv";
    public const string MappingFile = "mapping.csv";

    /// <summary>The most one file may unpack to: far beyond any business's
    /// recipients, items and graves, and far below what would strain the app.</summary>
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static readonly string[] RecipientsHeader = { "RecipientNumber", "Name", "Address" };
    public static readonly string[] ItemsHeader =
    {
        "ItemNumber",
        "Description",
        "VatRate",
        "Price",
        "Quarter",
    };
    public static readonly string[] GravesHeader =
    {
        "GraveNumber",
        "FieldNumber",
        "SiteName",
        "Position",
        "StartDate",
        "EndDate",
    };

    /// <summary>Both link types live in one file, told apart by the Type column:
    /// "Item" rows carry a count and a VAT-included flag, "Recipient" rows leave
    /// both empty.</summary>
    public static readonly string[] MappingHeader =
    {
        "Type",
        "GraveNumber",
        "Reference",
        "Count",
        "PriceIncludesVat",
    };

    public const string ItemLinkType = "Item";
    public const string RecipientLinkType = "Recipient";

    public static readonly IReadOnlyDictionary<string, string[]> Headers = new Dictionary<
        string,
        string[]
    >(StringComparer.OrdinalIgnoreCase)
    {
        [RecipientsFile] = RecipientsHeader,
        [ItemsFile] = ItemsHeader,
        [GravesFile] = GravesHeader,
        [MappingFile] = MappingHeader,
    };

    public static IReadOnlyList<string> FileNames { get; } =
        new[] { RecipientsFile, ItemsFile, GravesFile, MappingFile };
}
