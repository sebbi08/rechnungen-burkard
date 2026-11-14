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

    public static readonly string[] RecipientsHeader =
    {
        "RecipientNumber",
        "Name",
        "Address",
        "Email",
    };
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
        "Cemetery",
        "Position",
        "StartDate",
        "EndDate",
        "Cooperative",
        "ContractNumber",
    };

    /// <summary>Both link types live in one file, told apart by the Type column:
    /// "Item" rows carry a count and optionally the grave's own net unit price,
    /// "Recipient" rows leave both empty.</summary>
    public static readonly string[] MappingHeader =
    {
        "Type",
        "GraveNumber",
        "Reference",
        "Count",
        "UnitPrice",
    };

    /// <summary>The column grave prices used to be flagged VAT-included in. It
    /// is gone from the template, but archives prepared while it existed (the
    /// converter wrote it, always false) carry it between Count and UnitPrice,
    /// and still import: a false or empty value is ignored, a true one is
    /// refused, because grave prices are always net.</summary>
    public const string LegacyVatColumn = "PriceIncludesVat";

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

    /// <summary>How many of a file's last columns may be left out, header and
    /// all: columns added after archives were already being prepared, so a file
    /// written before them still imports. A file whose header leaves them out
    /// reads as if every row had them empty. Any number of them, from the
    /// last backwards.</summary>
    public static readonly IReadOnlyDictionary<string, int> OptionalTrailingColumns =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [RecipientsFile] = 1,
            [ItemsFile] = 0,
            [GravesFile] = 2,
            [MappingFile] = 1,
        };

    /// <summary>The header rows a file may carry: the full one, and the ones
    /// without its last one, two, ... optional trailing columns.</summary>
    public static IEnumerable<string[]> AcceptedHeaders(string file)
    {
        var header = Headers[file];
        yield return header;
        for (var left = 1; left <= OptionalTrailingColumns[file]; left++)
        {
            yield return header[..^left];
        }

        if (file.Equals(MappingFile, StringComparison.OrdinalIgnoreCase))
        {
            // the headers with the legacy column, with and without UnitPrice
            string[] legacy = [.. header[..4], LegacyVatColumn, header[4]];
            yield return legacy;
            yield return legacy[..^1];
        }
    }

    public static IReadOnlyList<string> FileNames { get; } =
        new[] { RecipientsFile, ItemsFile, GravesFile, MappingFile };
}
