using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class GraveInvoiceGenerator
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly InvoiceDocumentFactory _documentFactory;

    public GraveInvoiceGenerator(
        AppDbContext db,
        InvoiceNumberService numbers,
        InvoiceDocumentFactory documentFactory
    )
    {
        _db = db;
        _numbers = numbers;
        _documentFactory = documentFactory;
    }

    /// <summary>Generates all invoices for one grave and half-year in a single
    /// transaction. Returns a result with a skip reason when the grave must be
    /// skipped (no recipients, no items, an item with no price at all, a
    /// half-year outside the grave's term, or nothing to bill in it),
    /// otherwise the created invoices.</summary>
    public async Task<GraveGenerationResult> GenerateForGraveAsync(
        Grave grave,
        int year,
        int half,
        CancellationToken ct
    )
    {
        // ordered explicitly by link id, so which recipient gets which sequence
        // number is deterministic and reproducible across runs. Revision no
        // longer depends on this order: every recipient is billed the same
        // lines regardless of it.
        var recipients = await _db
            .GraveRecipients.Where(r => r.GraveId == grave.Id)
            .OrderBy(r => r.Id)
            .Select(r => r.Recipient)
            .ToListAsync(ct);

        // ordered explicitly for the same reason: line order decides which line
        // takes a cent under GraveShare's tie-break, so leaving it to SQLite's
        // unspecified row order would make the split depend on something outside
        // our own code, which is exactly what the tie-break exists to avoid.
        var graveItems = await _db
            .GraveItems.Where(i => i.GraveId == grave.Id)
            .OrderBy(i => i.Id)
            .Include(i => i.Item)
            .ToListAsync(ct);

        if (recipients.Count == 0)
        {
            return new GraveGenerationResult(null, "NoRecipients");
        }

        if (graveItems.Count == 0)
        {
            return new GraveGenerationResult(null, "NoItems");
        }

        if (graveItems.Any(i => EffectivePrice(i) is null))
        {
            return new GraveGenerationResult(null, "ItemWithoutPrice");
        }

        // only the months of the half-year inside the term are billed; the job
        // never hands over a grave outside it, but a direct caller might
        var period = HalfYearBilling.PeriodIn(grave.StartDate, grave.EndDate, year, half);
        if (period is null)
        {
            return new GraveGenerationResult(null, "OutsideTerm");
        }

        var links = graveItems
            .Select(item =>
                GraveBilling.LinkFor(item.Item, item.Count, item.UnitPrice, item.PriceIncludesVat)
            )
            .ToList();
        if (HalfYearBilling.Lines(links, period).Count == 0)
        {
            return new GraveGenerationResult(null, "NothingToBill");
        }

        // The block this half-year is bills the stored corrections of that
        // block: the line of each correction's item and rate is raised to the
        // price the user confirmed when saving the grave. A grave that needs
        // none has no rows and is billed as configured. A run is refused before
        // it starts when the stored rows no longer match the rule, so a split
        // that still fails here means the grave cannot be corrected at all.
        var example = HalfYearBilling.ExampleFor(grave.StartDate, grave.EndDate, year, half)!;
        var stored = await _db
            .GraveBillingCorrections.AsNoTracking()
            .Where(c =>
                c.GraveId == grave.Id
                && c.Kind == example.Kind
                && c.Year == example.Period.Year
                && c.Half == example.Period.Half
            )
            .ToListAsync(ct);
        var billedLines = GraveBillingCheck
            .ApplyCorrections(
                links,
                HalfYearBilling.Bill(links, period),
                stored.Select(c => (c.ItemId, c.VatRate, c.CorrectedPrice))
            )
            .Select(b => b.Line)
            .ToList();

        // Every recipient is billed the same lines. The share is applied when
        // the document is built, not by dividing the lines.
        var problems = GraveShare.Problems(billedLines, recipients.Count);
        if (problems.Count > 0)
        {
            return new GraveGenerationResult(null, "PricesDoNotDivide");
        }

        var settings = await _db.GlobalSettings.FirstAsync(ct);
        var sharePercent = 100m / recipients.Count;
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var invoices = new List<Invoice>();
        for (var r = 0; r < recipients.Count; r++)
        {
            var (number, seqYear, sequence) = await _numbers.AllocateAsync(ct);
            var invoice = new Invoice
            {
                RevisionNumber = 0,
                Year = seqYear,
                Sequence = sequence,
                InvoiceNumber = number,
                GraveId = grave.Id,
                PeriodYear = year,
                PeriodHalf = half,
                SharePercent = sharePercent,
                RecipientCount = recipients.Count,
                RecipientId = recipients[r].Id,
                RecipientName = recipients[r].Name,
                RecipientAddress = recipients[r].Address,
                RecipientNumber = recipients[r].Number,
                GraveNumber = grave.GraveNumber,
                GraveSiteName = grave.SiteName,
                GraveFieldNumber = grave.FieldNumber,
                GravePosition = grave.Position,
                EndingText = settings.EndingText,
                StampWord = settings.StampWord,
                GeneratedAt = now,
            };

            invoice.Lines = billedLines
                .Select(
                    (line, index) =>
                        new InvoiceLine
                        {
                            ItemId = line.ItemId,
                            ItemNumber = line.ItemNumber,
                            Description = line.Description,
                            Count = line.Count,
                            UnitPrice = line.UnitPrice,
                            VatRate = line.VatRate,
                            PriceIncludesVat = line.PriceIncludesVat,
                            LineNumber = index + 1,
                        }
                )
                .ToList();
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            invoice.RootId = invoice.Id;

            invoice.Documents = _documentFactory.Build(invoice, now);
            await _db.SaveChangesAsync(ct);
            invoices.Add(invoice);
        }

        _db.GraveInvoiceRuns.Add(
            new GraveInvoiceRun
            {
                GraveId = grave.Id,
                Year = year,
                Half = half,
                CreatedAt = now,
            }
        );
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new GraveGenerationResult(invoices, null);
    }

    /// <summary>The price a grave's line is billed at: the link's own price when
    /// the grave overrides it, otherwise the item's default price.</summary>
    private static decimal? EffectivePrice(GraveItem item) =>
        item.UnitPrice ?? item.Item.DefaultPrice;
}

/// <summary>Result of generating a grave's invoices: either the created invoices
/// or a machine-readable skip reason.</summary>
public sealed record GraveGenerationResult(List<Invoice>? Invoices, string? SkipReason);
