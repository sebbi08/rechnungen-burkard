using System.IO.Compression;
using System.Text;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>Downloads the template and imports an archive. The import validates
/// first and writes everything in one transaction, so an archive is either
/// stored completely or not at all (FR-060), even if the connection drops.</summary>
public sealed class ImportService
{
    private readonly AppDbContext _db;
    private readonly ImportValidator _validator;

    public ImportService(AppDbContext db, ImportValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    /// <summary>The empty archive users fill in offline: four CSV files, each
    /// with nothing but its header row (FR-049). Always downloadable, including
    /// on a fresh installation.</summary>
    public static byte[] BuildTemplate()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in ImportArchiveLayout.FileNames)
            {
                var header = ImportArchiveLayout.Headers[name];
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                // UTF-8 with a BOM so spreadsheet tools open the umlauts correctly
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                );
                writer.Write(Csv.Write(header, Array.Empty<string[]>()));
            }
        }

        return memory.ToArray();
    }

    public async Task<ImportResult> ImportAsync(Stream archive, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(archive, ct);
        if (!validation.IsValid)
        {
            return new ImportResult(validation.Problems, null);
        }

        var data = validation.Data!;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var recipients = data.Recipients.ToDictionary(
            row => row.Number,
            row => new Recipient
            {
                Number = row.Number,
                Name = row.Name,
                Address = row.Address,
            },
            StringComparer.OrdinalIgnoreCase
        );
        _db.Recipients.AddRange(recipients.Values);

        var items = data.Items.ToDictionary(
            row => row.Number,
            row => new InvoiceItem
            {
                Number = row.Number,
                Description = row.Description,
                VatRate = row.VatRate,
                DefaultPrice = row.Price,
            },
            StringComparer.OrdinalIgnoreCase
        );
        _db.InvoiceItems.AddRange(items.Values);

        var createdAt = DateOnly.FromDateTime(DateTime.UtcNow);
        var graves = data.Graves.ToDictionary(
            row => row.Number,
            row => new Grave
            {
                GraveNumber = row.Number,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                CreatedAt = createdAt,
            },
            StringComparer.OrdinalIgnoreCase
        );
        _db.Graves.AddRange(graves.Values);

        // the links need the generated keys, so the parents go in first
        await _db.SaveChangesAsync(ct);

        foreach (var link in data.ItemLinks)
        {
            _db.GraveItems.Add(
                new GraveItem
                {
                    GraveId = graves[link.GraveNumber].Id,
                    ItemId = items[link.ItemNumber].Id,
                    Count = link.Count,
                }
            );
        }

        foreach (var link in data.RecipientLinks)
        {
            _db.GraveRecipients.Add(
                new GraveRecipient
                {
                    GraveId = graves[link.GraveNumber].Id,
                    RecipientId = recipients[link.RecipientNumber].Id,
                }
            );
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var summary = new ImportSummary(
            recipients.Count,
            items.Count,
            graves.Count,
            data.ItemLinks.Count,
            data.RecipientLinks.Count
        );
        return new ImportResult(Array.Empty<ImportProblem>(), summary);
    }
}

/// <summary>Either the problems that stopped the import, or what was written.</summary>
public sealed record ImportResult(IReadOnlyList<ImportProblem> Problems, ImportSummary? Summary);
