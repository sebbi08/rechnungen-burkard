using System.IO.Compression;
using System.Text;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>Downloads the template and imports an archive. The import validates
/// first and writes everything in one transaction, so an archive is either
/// stored completely or not at all (FR-060), even if the connection drops.</summary>
public sealed class ImportService
{
    private readonly AppDbContext _db;
    private readonly ImportValidator _validator;
    private readonly IClock _clock;

    public ImportService(AppDbContext db, ImportValidator validator, IClock clock)
    {
        _db = db;
        _validator = validator;
        _clock = clock;
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
                Email = row.Email,
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
                Quarter = row.Quarter,
            },
            StringComparer.OrdinalIgnoreCase
        );
        _db.InvoiceItems.AddRange(items.Values);

        var createdAt = DateOnly.FromDateTime(_clock.GetLocalNow());
        var graves = data.Graves.ToDictionary(
            row => row.Number,
            row => new Grave
            {
                GraveNumber = row.Number,
                FieldNumber = row.FieldNumber,
                SiteName = row.SiteName,
                CemeteryId = data.CemeteryIds[row.Cemetery],
                Position = row.Position,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                IsCooperative = row.Cooperative,
                ContractNumber = row.ContractNumber,
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
                    UnitPrice = link.UnitPrice,
                    PriceIncludesVat = link.PriceIncludesVat,
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

        // a grave that does not split evenly is billed with raised prices, as a
        // save would have confirmed them; the links go in id order, like the
        // save and the generator read them
        var correctedGraves = 0;
        foreach (var (number, grave) in graves)
        {
            // a cooperative grave has no recipients to split across
            if (grave.IsCooperative)
            {
                continue;
            }

            var count = data.RecipientLinks.Count(l =>
                string.Equals(l.GraveNumber, number, StringComparison.OrdinalIgnoreCase)
            );
            var links = data
                .ItemLinks.Where(l =>
                    string.Equals(l.GraveNumber, number, StringComparison.OrdinalIgnoreCase)
                )
                .Select(l =>
                    GraveBilling.LinkFor(
                        items[l.ItemNumber],
                        l.Count,
                        l.UnitPrice,
                        l.PriceIncludesVat
                    )
                )
                .ToList();
            if (count == 0 || links.Count == 0)
            {
                continue;
            }

            var corrections = GraveBillingCheck
                .Evaluate(
                    links,
                    grave.StartDate,
                    grave.EndDate,
                    count,
                    HalfYearBilling.HalfYearOf(_clock.GetLocalNow())
                )
                .Invoices.SelectMany(invoice =>
                    invoice.Corrections.Select(c => new GraveBillingCorrection
                    {
                        GraveId = grave.Id,
                        Kind = invoice.Example.Kind,
                        Year = invoice.Example.Period.Year,
                        Half = invoice.Example.Period.Half,
                        VatRate = c.VatRate,
                        ItemId = c.ItemId,
                        EnteredPrice = c.EnteredPrice,
                        CorrectedPrice = c.CorrectedPrice,
                    })
                )
                .ToList();
            if (corrections.Count > 0)
            {
                _db.GraveBillingCorrections.AddRange(corrections);
                correctedGraves++;
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var summary = new ImportSummary(
            recipients.Count,
            items.Count,
            graves.Count,
            data.ItemLinks.Count,
            data.RecipientLinks.Count,
            correctedGraves
        );
        return new ImportResult(Array.Empty<ImportProblem>(), summary);
    }
}

/// <summary>Either the problems that stopped the import, or what was written.</summary>
public sealed record ImportResult(IReadOnlyList<ImportProblem> Problems, ImportSummary? Summary);
