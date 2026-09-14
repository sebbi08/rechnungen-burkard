using System.Globalization;
using System.IO.Compression;
using System.Text;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>Checks an archive completely before anything is written: structure,
/// headers, every row, duplicates inside the archive, cross-file references and
/// collisions with data that already exists (FR-054 – FR-060). Nothing here
/// touches the database except to read what is already stored.</summary>
public sealed class ImportValidator
{
    private readonly AppDbContext _db;

    public ImportValidator(AppDbContext db) => _db = db;

    public async Task<ImportValidationResult> ValidateAsync(Stream archive, CancellationToken ct)
    {
        var problems = new List<ImportProblem>();
        Dictionary<string, string> files;
        try
        {
            files = ReadArchive(archive);
        }
        catch (InvalidDataException)
        {
            problems.Add(
                new ImportProblem(
                    "",
                    null,
                    "ArchiveUnreadable",
                    "The file is not a readable ZIP archive."
                )
            );
            return new ImportValidationResult(problems, null);
        }

        // FR-055: all four files must be there. Extra entries are ignored.
        foreach (var name in ImportArchiveLayout.FileNames)
        {
            if (!files.ContainsKey(name))
            {
                problems.Add(
                    new ImportProblem(
                        name,
                        null,
                        "FileMissing",
                        "The file is missing from the archive."
                    )
                );
            }
        }

        if (problems.Count > 0)
        {
            return new ImportValidationResult(problems, null);
        }

        // FR-056: the header row of every file must match exactly.
        var tables = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expectedHeader) in ImportArchiveLayout.Headers)
        {
            var rows = Csv.Parse(files[name]);
            if (rows.Count == 0 || !HeaderMatches(rows[0], expectedHeader))
            {
                problems.Add(
                    new ImportProblem(
                        name,
                        1,
                        "HeaderInvalid",
                        $"The header row must be: {string.Join(", ", expectedHeader)}."
                    )
                );
                continue;
            }

            tables[name] = rows;
        }

        if (problems.Count > 0)
        {
            return new ImportValidationResult(problems, null);
        }

        var data = new ImportData();
        ReadRecipients(tables[ImportArchiveLayout.RecipientsFile], data, problems);
        ReadItems(tables[ImportArchiveLayout.ItemsFile], data, problems);
        ReadGraves(tables[ImportArchiveLayout.GravesFile], data, problems);
        ReadMapping(tables[ImportArchiveLayout.MappingFile], data, problems);

        CheckReferences(data, problems);
        await CheckAgainstStoredDataAsync(data, problems, ct);

        // Last, and only on rows that are otherwise sound: it reads counts,
        // prices and recipients across three files, and none of that means
        // anything while one of them is still wrong.
        if (problems.Count == 0)
        {
            CheckGraveSplits(data, problems);
        }

        problems.Sort(ByFileThenRow);
        return new ImportValidationResult(problems, problems.Count == 0 ? data : null);
    }

    private static int ByFileThenRow(ImportProblem a, ImportProblem b)
    {
        var byFile = string.CompareOrdinal(a.File, b.File);
        return byFile != 0 ? byFile : Comparer<int>.Default.Compare(a.Row ?? 0, b.Row ?? 0);
    }

    private static Dictionary<string, string> ReadArchive(Stream archive)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            // match on the bare file name so archives zipped with a folder still work
            var name = Path.GetFileName(entry.FullName);
            if (!ImportArchiveLayout.Headers.ContainsKey(name) || files.ContainsKey(name))
            {
                continue;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );
            files[name] = reader.ReadToEnd();
        }

        return files;
    }

    private static bool HeaderMatches(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected
    ) =>
        actual.Count == expected.Count
        && actual
            .Select(
                (value, i) => value.Trim().Equals(expected[i], StringComparison.OrdinalIgnoreCase)
            )
            .All(m => m);

    private static void ReadRecipients(
        List<List<string>> rows,
        ImportData data,
        List<ImportProblem> problems
    )
    {
        const string file = ImportArchiveLayout.RecipientsFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (!HasColumns(row, 3, file, line, problems))
                continue;

            var number = row[0].Trim();
            var name = row[1].Trim();
            var address = row[2].Trim();
            if (number.Length > 0)
                data.DeclaredRecipients.Add(number);
            if (
                Missing(number, file, line, "RecipientNumber", problems)
                | Missing(name, file, line, "Name", problems)
                | Missing(address, file, line, "Address", problems)
            )
            {
                continue;
            }

            if (seen.TryGetValue(number, out var first))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DuplicateNumber",
                        $"The recipient number '{number}' is already used in row {first}."
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Recipients.Add(new RecipientRow(line, number, name, address));
        }
    }

    private static void ReadItems(
        List<List<string>> rows,
        ImportData data,
        List<ImportProblem> problems
    )
    {
        const string file = ImportArchiveLayout.ItemsFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (!HasColumns(row, 4, file, line, problems))
                continue;

            var number = row[0].Trim();
            var description = row[1].Trim();
            var rawVat = row[2].Trim();
            var rawPrice = row[3].Trim();
            if (number.Length > 0)
                data.DeclaredItems.Add(number);
            if (
                Missing(number, file, line, "ItemNumber", problems)
                | Missing(description, file, line, "Description", problems)
                | Missing(rawVat, file, line, "VatRate", problems)
            )
            {
                continue;
            }

            if (!TryParseDecimal(rawVat, out var vatRate) || vatRate < 0)
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "VatRateInvalid",
                        $"'{rawVat}' is not a valid VAT rate."
                    )
                );
                continue;
            }

            decimal? price = null;
            if (rawPrice.Length > 0)
            {
                if (!TryParseDecimal(rawPrice, out var parsedPrice) || parsedPrice < 0)
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "PriceInvalid",
                            $"'{rawPrice}' is not a valid price."
                        )
                    );
                    continue;
                }

                price = parsedPrice;
            }

            if (seen.TryGetValue(number, out var first))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DuplicateNumber",
                        $"The item number '{number}' is already used in row {first}."
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Items.Add(
                new ItemRow(line, number, description, VatRateNormalizer.Normalize(vatRate), price)
            );
        }
    }

    private static void ReadGraves(
        List<List<string>> rows,
        ImportData data,
        List<ImportProblem> problems
    )
    {
        const string file = ImportArchiveLayout.GravesFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (!HasColumns(row, 3, file, line, problems))
                continue;

            var number = row[0].Trim();
            var rawStart = row[1].Trim();
            var rawEnd = row[2].Trim();
            if (number.Length > 0)
                data.DeclaredGraves.Add(number);
            if (
                Missing(number, file, line, "GraveNumber", problems)
                | Missing(rawStart, file, line, "StartDate", problems)
                | Missing(rawEnd, file, line, "EndDate", problems)
            )
            {
                continue;
            }

            if (!TryParseDate(rawStart, out var start))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DateInvalid",
                        $"'{rawStart}' is not a valid date (expected YYYY-MM-DD)."
                    )
                );
                continue;
            }

            if (!TryParseDate(rawEnd, out var end))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DateInvalid",
                        $"'{rawEnd}' is not a valid date (expected YYYY-MM-DD)."
                    )
                );
                continue;
            }

            if (end < start)
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DateRangeInvalid",
                        "The end date must not be before the start date."
                    )
                );
                continue;
            }

            if (seen.TryGetValue(number, out var first))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DuplicateNumber",
                        $"The grave number '{number}' is already used in row {first}."
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Graves.Add(new GraveRow(line, number, start, end));
        }
    }

    private static void ReadMapping(
        List<List<string>> rows,
        ImportData data,
        List<ImportProblem> problems
    )
    {
        const string file = ImportArchiveLayout.MappingFile;
        var itemLinks = new Dictionary<(string, string), int>();
        var recipientLinks = new Dictionary<(string, string), int>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (!HasColumns(row, 5, file, line, problems))
                continue;

            var type = row[0].Trim();
            var graveNumber = row[1].Trim();
            var reference = row[2].Trim();
            var rawCount = row[3].Trim();
            var rawIncludesVat = row[4].Trim();
            if (
                Missing(type, file, line, "Type", problems)
                | Missing(graveNumber, file, line, "GraveNumber", problems)
                | Missing(reference, file, line, "Reference", problems)
            )
            {
                continue;
            }

            if (type.Equals(ImportArchiveLayout.ItemLinkType, StringComparison.OrdinalIgnoreCase))
            {
                if (
                    !int.TryParse(
                        rawCount,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var count
                    )
                    || count <= 0
                )
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "CountInvalid",
                            $"'{rawCount}' is not a valid count; an item link needs a positive number."
                        )
                    );
                    continue;
                }

                if (!TryParseBool(rawIncludesVat, out var priceIncludesVat))
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "PriceIncludesVatInvalid",
                            $"'{rawIncludesVat}' is not a valid yes/no value."
                        )
                    );
                    continue;
                }

                var key = (graveNumber.ToUpperInvariant(), reference.ToUpperInvariant());
                if (itemLinks.TryGetValue(key, out var firstItemRow))
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "DuplicateLink",
                            $"Grave '{graveNumber}' is already linked to item '{reference}' in row {firstItemRow}."
                        )
                    );
                    continue;
                }

                itemLinks[key] = line;
                data.ItemLinks.Add(
                    new ItemLinkRow(line, graveNumber, reference, count, priceIncludesVat)
                );
            }
            else if (
                type.Equals(
                    ImportArchiveLayout.RecipientLinkType,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var key = (graveNumber.ToUpperInvariant(), reference.ToUpperInvariant());
                if (recipientLinks.TryGetValue(key, out var firstRecipientRow))
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "DuplicateLink",
                            $"Grave '{graveNumber}' is already linked to recipient '{reference}' in row {firstRecipientRow}."
                        )
                    );
                    continue;
                }

                recipientLinks[key] = line;
                data.RecipientLinks.Add(new RecipientLinkRow(line, graveNumber, reference));
            }
            else
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "TypeInvalid",
                        $"'{type}' is not a link type; use '{ImportArchiveLayout.ItemLinkType}' or "
                            + $"'{ImportArchiveLayout.RecipientLinkType}'."
                    )
                );
            }
        }
    }

    /// <summary>FR-058: every reference in the mapping file must be defined in
    /// one of the other files.</summary>
    /// <summary>Whether every grave in the archive can be billed at all.
    ///
    /// A grave's bill is split equally, so each VAT rate's net subtotal has to
    /// divide across its recipients, and a price entered as a gross has to be
    /// collectable to the cent. The dialog refuses a grave that cannot; an
    /// archive that carried one would import quietly and then never bill it —
    /// generation skips such a grave — so the whole import is refused instead,
    /// with the rate and the amounts that would work.
    ///
    /// The import never sets a link's own price, so what is billed is the
    /// item's default price from items.csv. The problem is reported against the
    /// grave's row, because the grave is what does not add up; which price to
    /// move is the reader's choice.</summary>
    private static void CheckGraveSplits(ImportData data, List<ImportProblem> problems)
    {
        var items = data.Items.ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);
        var recipients = data
            .RecipientLinks.GroupBy(link => link.GraveNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var grave in data.Graves)
        {
            // no recipients yet means no divisor, exactly as when a grave is
            // saved with its items before its recipients are known
            if (!recipients.TryGetValue(grave.Number, out var count) || count == 0)
            {
                continue;
            }

            var lines = data
                .ItemLinks.Where(link =>
                    string.Equals(
                        link.GraveNumber,
                        grave.Number,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(link =>
                {
                    var item = items[link.ItemNumber];
                    return GraveBilling.LineFor(
                        new InvoiceItem
                        {
                            Number = item.Number,
                            Description = item.Description,
                            VatRate = item.VatRate,
                            DefaultPrice = item.Price,
                        },
                        link.Count,
                        null,
                        link.PriceIncludesVat
                    );
                })
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            foreach (var problem in GraveShare.Problems(lines, count))
            {
                problems.Add(
                    new ImportProblem(
                        ImportArchiveLayout.GravesFile,
                        grave.Row,
                        "PricesDoNotDivide",
                        Describe(problem, count)
                    )
                );
            }
        }
    }

    private static string Describe(GraveShareProblem problem, int recipients)
    {
        var amount = (problem.Amount / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var message =
            $"VAT {problem.VatRate:0.##} %: {amount} does not split evenly across "
            + $"{recipients} recipients.";

        if (problem.Suggestions.Count == 0)
        {
            return $"{message} Adjust a price at this VAT rate.";
        }

        var options = string.Join(
            " or ",
            problem.Suggestions.Select(cents =>
                (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture)
            )
        );
        return $"{message} {options} would work.";
    }

    private static void CheckReferences(ImportData data, List<ImportProblem> problems)
    {
        const string file = ImportArchiveLayout.MappingFile;
        var graves = data.DeclaredGraves;
        var items = data.DeclaredItems;
        var recipients = data.DeclaredRecipients;

        foreach (var link in data.ItemLinks)
        {
            if (!graves.Contains(link.GraveNumber))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        link.Row,
                        "GraveNotDefined",
                        $"The grave '{link.GraveNumber}' is not defined in {ImportArchiveLayout.GravesFile}."
                    )
                );
            }

            if (!items.Contains(link.ItemNumber))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        link.Row,
                        "ItemNotDefined",
                        $"The item '{link.ItemNumber}' is not defined in {ImportArchiveLayout.ItemsFile}."
                    )
                );
            }
        }

        foreach (var link in data.RecipientLinks)
        {
            if (!graves.Contains(link.GraveNumber))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        link.Row,
                        "GraveNotDefined",
                        $"The grave '{link.GraveNumber}' is not defined in {ImportArchiveLayout.GravesFile}."
                    )
                );
            }

            if (!recipients.Contains(link.RecipientNumber))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        link.Row,
                        "RecipientNotDefined",
                        $"The recipient '{link.RecipientNumber}' is not defined in "
                            + $"{ImportArchiveLayout.RecipientsFile}."
                    )
                );
            }
        }
    }

    /// <summary>Version 1 imports are create-only, so an identifier that is
    /// already stored is a validation error rather than an update. An item's VAT
    /// rate must also be one of the configured ones, exactly as it must be when
    /// the item is created through the item section (FR-006).</summary>
    private async Task CheckAgainstStoredDataAsync(
        ImportData data,
        List<ImportProblem> problems,
        CancellationToken ct
    )
    {
        var configuredRates = await _db.VatRates.Select(v => v.Rate).ToListAsync(ct);
        foreach (var item in data.Items.Where(i => !configuredRates.Contains(i.VatRate)))
        {
            problems.Add(
                new ImportProblem(
                    ImportArchiveLayout.ItemsFile,
                    item.Row,
                    "VatRateNotConfigured",
                    $"The VAT rate '{item.VatRate}' is not configured in the settings."
                )
            );
        }

        var recipientNumbers = await _db.Recipients.Select(r => r.Number).ToListAsync(ct);
        var existingRecipients = recipientNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var recipient in data.Recipients.Where(r => existingRecipients.Contains(r.Number)))
        {
            problems.Add(
                new ImportProblem(
                    ImportArchiveLayout.RecipientsFile,
                    recipient.Row,
                    "AlreadyExists",
                    $"A recipient with the number '{recipient.Number}' already exists."
                )
            );
        }

        var itemNumbers = await _db.InvoiceItems.Select(i => i.Number).ToListAsync(ct);
        var existingItems = itemNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in data.Items.Where(i => existingItems.Contains(i.Number)))
        {
            problems.Add(
                new ImportProblem(
                    ImportArchiveLayout.ItemsFile,
                    item.Row,
                    "AlreadyExists",
                    $"An item with the number '{item.Number}' already exists."
                )
            );
        }

        var graveNumbers = await _db.Graves.Select(g => g.GraveNumber).ToListAsync(ct);
        var existingGraves = graveNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grave in data.Graves.Where(g => existingGraves.Contains(g.Number)))
        {
            problems.Add(
                new ImportProblem(
                    ImportArchiveLayout.GravesFile,
                    grave.Row,
                    "AlreadyExists",
                    $"A grave with the number '{grave.Number}' already exists."
                )
            );
        }
    }

    private static bool HasColumns(
        IReadOnlyList<string> row,
        int expected,
        string file,
        int line,
        List<ImportProblem> problems
    )
    {
        if (row.Count == expected)
        {
            return true;
        }

        problems.Add(
            new ImportProblem(
                file,
                line,
                "ColumnCount",
                $"The row has {row.Count} columns; {expected} are expected."
            )
        );
        return false;
    }

    private static bool Missing(
        string value,
        string file,
        int line,
        string column,
        List<ImportProblem> problems
    )
    {
        if (value.Length > 0)
        {
            return false;
        }

        problems.Add(
            new ImportProblem(
                file,
                line,
                "FieldRequired",
                $"The column '{column}' must not be empty."
            )
        );
        return true;
    }

    // Templates are filled in by hand, so both the invariant "12.5" and the
    // German "12,5" have to be understood.
    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(
            raw.Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value
        );

    // Templates are filled in by hand, in German and in English, so both
    // spellings have to be understood. An empty cell is the common case and
    // means net — what every item was before this column existed.
    private static bool TryParseBool(string raw, out bool value)
    {
        switch (raw.ToLowerInvariant())
        {
            case "":
            case "false":
            case "0":
            case "nein":
            case "no":
                value = false;
                return true;
            case "true":
            case "1":
            case "ja":
            case "yes":
                value = true;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryParseDate(string raw, out DateOnly value) =>
        DateOnly.TryParseExact(
            raw,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value
        ) || DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
