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
    private readonly IClock _clock;

    public ImportValidator(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ImportValidationResult> ValidateAsync(Stream archive, CancellationToken ct)
    {
        var problems = new List<ImportProblem>();
        Dictionary<string, string?> files;
        try
        {
            files = ReadArchive(archive, problems);
        }
        catch (InvalidDataException)
        {
            problems.Add(
                new ImportProblem(
                    "",
                    null,
                    "ArchiveUnreadable",
                    "The file is not a readable ZIP archive.",
                    "ArchiveUnreadable"
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
                        "The file is missing from the archive.",
                        "FileMissing"
                    )
                );
            }
        }

        if (problems.Count > 0)
        {
            return new ImportValidationResult(problems, null);
        }

        // FR-056: the header row of every file must match exactly.
        var tables = new Dictionary<string, List<CsvRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expectedHeader) in ImportArchiveLayout.Headers)
        {
            var rows = Csv.Parse(files[name]!);
            if (rows.Count == 0 || !HeaderMatches(rows[0], expectedHeader))
            {
                problems.Add(
                    new ImportProblem(
                        name,
                        1,
                        "HeaderInvalid",
                        $"The header row must be: {string.Join(", ", expectedHeader)}.",
                        "HeaderInvalid",
                        [string.Join(", ", expectedHeader)]
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
        CheckGraveRecipients(data, problems);
        CheckGraveAllYearItems(data, problems);
        CheckLinkedItemPrices(data, problems);
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

    // Strict: an invalid byte throws instead of turning into U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    // spelled out: the strict encoding above emits none, so its Preamble is empty
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>The four files' texts by name. A file that is there but cannot
    /// be read -- too large, or not UTF-8 -- is reported in
    /// <paramref name="problems"/> and maps to null, so it is not also reported
    /// as missing.</summary>
    private static Dictionary<string, string?> ReadArchive(
        Stream archive,
        List<ImportProblem> problems
    )
    {
        var files = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            // match on the bare file name so archives zipped with a folder still work
            var name = Path.GetFileName(entry.FullName);
            if (!ImportArchiveLayout.Headers.ContainsKey(name) || files.ContainsKey(name))
            {
                continue;
            }

            files[name] = null;
            // The upload is capped, a compressed entry is not: a few megabytes
            // can unpack to more than the app can hold. The size the archive
            // states could be anything, so the bytes are counted as they come.
            var bytes = ReadAtMost(entry, ImportArchiveLayout.MaxFileBytes);
            if (bytes is null)
            {
                problems.Add(
                    new ImportProblem(
                        name,
                        null,
                        "FileTooLarge",
                        $"The file is larger than {ImportArchiveLayout.MaxFileBytes / (1024 * 1024)} MB.",
                        "FileTooLarge",
                        [
                            (ImportArchiveLayout.MaxFileBytes / (1024 * 1024)).ToString(
                                CultureInfo.InvariantCulture
                            ),
                        ]
                    )
                );
                continue;
            }

            // Excel's plain "CSV" on Windows is Windows-1252, and read as UTF-8
            // its umlauts became U+FFFD -- imported, and printed on invoices,
            // without a word. Refused instead, so it can be saved as UTF-8.
            // the template carries a BOM for Excel's sake; it is not text
            var bom = bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
            try
            {
                files[name] = StrictUtf8.GetString(bytes, bom, bytes.Length - bom);
            }
            catch (DecoderFallbackException)
            {
                problems.Add(
                    new ImportProblem(
                        name,
                        null,
                        "EncodingInvalid",
                        "The file is not UTF-8 encoded.",
                        "EncodingInvalid"
                    )
                );
            }
        }

        return files;
    }

    /// <summary>The entry's bytes, or null once there are more than
    /// <paramref name="limit"/>.</summary>
    private static byte[]? ReadAtMost(ZipArchiveEntry entry, int limit)
    {
        using var stream = entry.Open();
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (content.Length + read > limit)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
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
        List<CsvRow> rows,
        ImportData data,
        List<ImportProblem> problems
    )
    {
        const string file = ImportArchiveLayout.RecipientsFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = row.Number;
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
                        $"The recipient number '{number}' is already used in row {first}.",
                        "DuplicateRecipientNumber",
                        [number, first.ToString(CultureInfo.InvariantCulture)]
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Recipients.Add(new RecipientRow(line, number, name, address));
        }
    }

    private static void ReadItems(List<CsvRow> rows, ImportData data, List<ImportProblem> problems)
    {
        const string file = ImportArchiveLayout.ItemsFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = row.Number;
            if (!HasColumns(row, 5, file, line, problems))
                continue;

            var number = row[0].Trim();
            var description = row[1].Trim();
            var rawVat = row[2].Trim();
            var rawPrice = row[3].Trim();
            var rawQuarter = row[4].Trim();
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
                        $"'{rawVat}' is not a valid VAT rate.",
                        "VatRateInvalid",
                        [rawVat]
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
                            $"'{rawPrice}' is not a valid price.",
                            "PriceInvalid",
                            [rawPrice]
                        )
                    );
                    continue;
                }

                // prices are cents: a third decimal would be judged rounded by
                // the split check and billed unrounded by generation
                if (!GraveBilling.IsWholeCents(parsedPrice))
                {
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "PriceInvalid",
                            $"'{rawPrice}' has more than two decimals.",
                            "PriceDecimals",
                            [rawPrice]
                        )
                    );
                    continue;
                }

                price = parsedPrice;
            }

            BillingQuarter? quarter = null;
            if (rawQuarter.Length > 0)
            {
                // a member name only, as for a grave's position: Enum.TryParse
                // alone would also take "2"
                if (
                    !Enum.GetNames<BillingQuarter>().Contains(rawQuarter)
                    || !Enum.TryParse(rawQuarter, out BillingQuarter parsedQuarter)
                )
                {
                    var allowed = string.Join(", ", Enum.GetNames<BillingQuarter>());
                    problems.Add(
                        new ImportProblem(
                            file,
                            line,
                            "QuarterInvalid",
                            $"'{rawQuarter}' is not a quarter (expected one of {allowed}).",
                            "QuarterInvalid",
                            [rawQuarter, allowed]
                        )
                    );
                    continue;
                }

                quarter = parsedQuarter;
            }

            if (seen.TryGetValue(number, out var first))
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DuplicateNumber",
                        $"The item number '{number}' is already used in row {first}.",
                        "DuplicateItemNumber",
                        [number, first.ToString(CultureInfo.InvariantCulture)]
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Items.Add(
                new ItemRow(
                    line,
                    number,
                    description,
                    VatRateNormalizer.Normalize(vatRate),
                    price,
                    quarter
                )
            );
        }
    }

    private static void ReadGraves(List<CsvRow> rows, ImportData data, List<ImportProblem> problems)
    {
        const string file = ImportArchiveLayout.GravesFile;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = row.Number;
            if (!HasColumns(row, 6, file, line, problems))
                continue;

            var number = row[0].Trim();
            var fieldNumber = row[1].Trim();
            var siteName = row[2].Trim();
            var rawPosition = row[3].Trim();
            var rawStart = row[4].Trim();
            var rawEnd = row[5].Trim();
            if (number.Length > 0)
                data.DeclaredGraves.Add(number);
            if (
                Missing(number, file, line, "GraveNumber", problems)
                | Missing(fieldNumber, file, line, "FieldNumber", problems)
                | Missing(siteName, file, line, "SiteName", problems)
                | Missing(rawPosition, file, line, "Position", problems)
                | Missing(rawStart, file, line, "StartDate", problems)
            )
            {
                continue;
            }

            if (!GraveTerm.TryParsePosition(rawPosition, out var position))
            {
                var allowed = string.Join(", ", Enum.GetNames<GravePosition>());
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "PositionInvalid",
                        $"'{rawPosition}' is not a grave position (expected one of {allowed}).",
                        "PositionInvalid",
                        [rawPosition, allowed]
                    )
                );
                continue;
            }

            if (!GraveTerm.TryParseMonth(rawStart, out var start))
            {
                problems.Add(MonthInvalid(file, line, rawStart));
                continue;
            }

            DateOnly? end = null;
            if (rawEnd.Length > 0)
            {
                if (!GraveTerm.TryParseMonth(rawEnd, out var parsedEnd))
                {
                    problems.Add(MonthInvalid(file, line, rawEnd));
                    continue;
                }

                end = parsedEnd;
            }

            if (end < start)
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DateRangeInvalid",
                        "The end month must not be before the start month.",
                        "DateRangeInvalid"
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
                        $"The grave number '{number}' is already used in row {first}.",
                        "DuplicateGraveNumber",
                        [number, first.ToString(CultureInfo.InvariantCulture)]
                    )
                );
                continue;
            }

            seen[number] = line;
            data.Graves.Add(
                new GraveRow(line, number, fieldNumber, siteName, position, start, end)
            );
        }
    }

    private static void ReadMapping(
        List<CsvRow> rows,
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
            var line = row.Number;
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
                            $"'{rawCount}' is not a valid count; an item link needs a positive number.",
                            "CountInvalid",
                            [rawCount]
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
                            $"'{rawIncludesVat}' is not a valid yes/no value.",
                            "PriceIncludesVatInvalid",
                            [rawIncludesVat]
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
                            $"Grave '{graveNumber}' is already linked to item '{reference}' in row {firstItemRow}.",
                            "DuplicateItemLink",
                            [
                                graveNumber,
                                reference,
                                firstItemRow.ToString(CultureInfo.InvariantCulture),
                            ]
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
                            $"Grave '{graveNumber}' is already linked to recipient '{reference}' in row {firstRecipientRow}.",
                            "DuplicateRecipientLink",
                            [
                                graveNumber,
                                reference,
                                firstRecipientRow.ToString(CultureInfo.InvariantCulture),
                            ]
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
                            + $"'{ImportArchiveLayout.RecipientLinkType}'.",
                        "TypeInvalid",
                        [
                            type,
                            ImportArchiveLayout.ItemLinkType,
                            ImportArchiveLayout.RecipientLinkType,
                        ]
                    )
                );
            }
        }
    }

    /// <summary>Whether every invoice of every grave's term can be split across
    /// its recipients.
    ///
    /// A grave's bill is split equally, so each VAT rate's net subtotal has to
    /// divide across its recipients, and a price entered as a gross has to be
    /// collectable to the cent — in every invoice of the term, the prorated
    /// first and last half-years included. The import corrects what it can (the
    /// corrections are stored by <c>ImportService</c>) and reports only a block
    /// no line can fix within 1,00 €; an archive carrying one is refused as a
    /// whole, since generation would otherwise skip that grave.
    ///
    /// The import never sets a link's own price, so what is billed is the
    /// item's default price from items.csv. The problems are reported against
    /// the grave's row, because the grave is what does not add up. A grave over
    /// the recipient cap is left to <see cref="CheckGraveRecipients"/>.</summary>
    private void CheckGraveSplits(ImportData data, List<ImportProblem> problems)
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
            // every grave has at least one: CheckGraveRecipients reported any
            // without, and this runs only on a report with nothing in it
            var count = recipients[grave.Number];
            if (count > GraveBilling.MaxRecipients)
            {
                continue;
            }

            var links = data
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
                    return GraveBilling.LinkFor(
                        new InvoiceItem
                        {
                            Number = item.Number,
                            Description = item.Description,
                            VatRate = item.VatRate,
                            DefaultPrice = item.Price,
                            Quarter = item.Quarter,
                        },
                        link.Count,
                        null,
                        link.PriceIncludesVat
                    );
                })
                .ToList();

            if (links.Count == 0)
            {
                continue;
            }

            var evaluation = GraveBillingCheck.Evaluate(
                links,
                grave.StartDate,
                grave.EndDate,
                count,
                HalfYearBilling.HalfYearOf(_clock.GetLocalNow())
            );
            if (evaluation.FirstFailing is not { } failing)
            {
                continue;
            }

            var label = HalfYearBilling.Label(failing.Example);
            foreach (var problem in failing.Problems)
            {
                problems.Add(
                    Describe(ImportArchiveLayout.GravesFile, grave.Row, label, problem, count)
                );
            }
        }
    }

    /// <summary>One rate of the grave's first failing invoice, named by its
    /// label.</summary>
    private static ImportProblem Describe(
        string file,
        int row,
        string invoice,
        GraveShareProblem problem,
        int recipients
    )
    {
        var rate = problem.VatRate.ToString("0.##", CultureInfo.InvariantCulture);
        var across = recipients == 1 ? "1 recipient" : $"{recipients} recipients";
        return new ImportProblem(
            file,
            row,
            "PricesDoNotDivide",
            $"{invoice}: VAT {rate} %: {Euros(problem.Amount)} does not split evenly across {across}. "
                + "Adjust a price at this VAT rate.",
            "PricesDoNotDivideIn",
            [
                invoice,
                rate,
                Euros(problem.Amount),
                recipients.ToString(CultureInfo.InvariantCulture),
            ]
        );
    }

    private static string Euros(long cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A grave's recipients are fixed at creation (FR-020b), so a grave
    /// imported without one could never be billed: generation would skip it and
    /// nothing could add a recipient afterwards. SaveGrave refuses the same. This
    /// runs with the other cross-file checks, so the report names it in the same
    /// pass as everything else. A grave is also capped at
    /// <see cref="GraveBilling.MaxRecipients"/> recipients, as SaveGrave does.</summary>
    private static void CheckGraveRecipients(ImportData data, List<ImportProblem> problems)
    {
        var linked = new HashSet<string>(
            data.RecipientLinks.Select(link => link.GraveNumber),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var grave in data.Graves)
        {
            if (!linked.Contains(grave.Number))
            {
                problems.Add(
                    new ImportProblem(
                        ImportArchiveLayout.GravesFile,
                        grave.Row,
                        "NoRecipients",
                        "The grave needs at least one recipient.",
                        "NoRecipients"
                    )
                );
            }
        }

        var counts = data
            .RecipientLinks.GroupBy(link => link.GraveNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var grave in data.Graves)
        {
            if (
                counts.TryGetValue(grave.Number, out var count)
                && count > GraveBilling.MaxRecipients
            )
            {
                var max = GraveBilling.MaxRecipients.ToString(CultureInfo.InvariantCulture);
                problems.Add(
                    new ImportProblem(
                        ImportArchiveLayout.GravesFile,
                        grave.Row,
                        "TooManyRecipients",
                        $"A grave can have at most {max} recipients.",
                        "TooManyRecipients",
                        [max]
                    )
                );
            }
        }
    }

    /// <summary>A grave needs at least one item billed all year, as SaveGrave
    /// requires. A link to an item that is not declared (or was rejected) is
    /// reported by the reference check already, and might have been the all-year
    /// one, so a grave with such a link is left alone here.</summary>
    private static void CheckGraveAllYearItems(ImportData data, List<ImportProblem> problems)
    {
        var items = data.Items.ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);
        var linksByGrave = data.ItemLinks.ToLookup(
            link => link.GraveNumber,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var grave in data.Graves)
        {
            var links = linksByGrave[grave.Number].ToList();
            if (
                links.Any(link => !items.ContainsKey(link.ItemNumber))
                || links.Any(link => items[link.ItemNumber].Quarter is null)
            )
            {
                continue;
            }

            problems.Add(
                new ImportProblem(
                    ImportArchiveLayout.GravesFile,
                    grave.Row,
                    "AllYearItemRequired",
                    "The grave needs at least one item billed all year.",
                    "AllYearItemRequired"
                )
            );
        }
    }

    /// <summary>An item may come without a price (FR-051), but a grave cannot be
    /// billed for one: SaveGrave refuses such a link, and generation would skip
    /// the grave. Refused on the mapping row, so the item itself still imports as
    /// long as no grave links it. A link to an item row that was rejected on its
    /// own carries that problem already and is left alone here.</summary>
    private static void CheckLinkedItemPrices(ImportData data, List<ImportProblem> problems)
    {
        var items = data.Items.ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);
        foreach (var link in data.ItemLinks)
        {
            if (items.TryGetValue(link.ItemNumber, out var item) && item.Price is null)
            {
                problems.Add(
                    new ImportProblem(
                        ImportArchiveLayout.MappingFile,
                        link.Row,
                        "PriceRequired",
                        $"A price is required for item '{item.Number}': a grave links it.",
                        "PriceRequired",
                        [item.Number]
                    )
                );
            }
        }
    }

    /// <summary>FR-058: every reference in the mapping file must be defined in
    /// one of the other files.</summary>
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
                        $"The grave '{link.GraveNumber}' is not defined in {ImportArchiveLayout.GravesFile}.",
                        "GraveNotDefined",
                        [link.GraveNumber]
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
                        $"The item '{link.ItemNumber}' is not defined in {ImportArchiveLayout.ItemsFile}.",
                        "ItemNotDefined",
                        [link.ItemNumber]
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
                        $"The grave '{link.GraveNumber}' is not defined in {ImportArchiveLayout.GravesFile}.",
                        "GraveNotDefined",
                        [link.GraveNumber]
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
                            + $"{ImportArchiveLayout.RecipientsFile}.",
                        "RecipientNotDefined",
                        [link.RecipientNumber]
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
                    $"The VAT rate '{item.VatRate}' is not configured in the settings.",
                    "VatRateNotConfigured",
                    [item.VatRate.ToString("0.##", CultureInfo.InvariantCulture)]
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
                    $"A recipient with the number '{recipient.Number}' already exists.",
                    "RecipientExists",
                    [recipient.Number]
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
                    $"An item with the number '{item.Number}' already exists.",
                    "ItemExists",
                    [item.Number]
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
                    $"A grave with the number '{grave.Number}' already exists.",
                    "GraveExists",
                    [grave.Number]
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
                $"The row has {row.Count} columns; {expected} are expected.",
                "ColumnCount",
                [
                    row.Count.ToString(CultureInfo.InvariantCulture),
                    expected.ToString(CultureInfo.InvariantCulture),
                ]
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
                $"The column '{column}' must not be empty.",
                "FieldRequired",
                [column]
            )
        );
        return true;
    }

    // Templates are filled in by hand, so both the invariant "12.5" and the
    // German "12,5" have to be understood. No thousands separator in either:
    // "1.234,50" and "1,234.50" are one amount in two spellings, and allowing
    // the invariant one (NumberStyles.Number does) read "12,50" as 12 050 -- a
    // price a hundred times too high that passed every later check.
    private static bool TryParseDecimal(string raw, out decimal value)
    {
        const NumberStyles plain =
            NumberStyles.AllowLeadingWhite
            | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint;
        if (raw.Contains('.') && raw.Contains(','))
        {
            value = 0;
            return false;
        }

        return decimal.TryParse(
            raw.Replace(',', '.'),
            plain,
            CultureInfo.InvariantCulture,
            out value
        );
    }

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

    private static ImportProblem MonthInvalid(string file, int line, string raw) =>
        new(
            file,
            line,
            "DateInvalid",
            $"'{raw}' is not a valid month (expected YYYY-MM).",
            "DateInvalid",
            [raw]
        );
}
