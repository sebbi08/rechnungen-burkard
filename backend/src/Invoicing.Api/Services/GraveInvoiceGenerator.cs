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
    /// skipped (no recipients, no items, or an item with no price at all),
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

        var graveLines = graveItems
            .Select(item => new GraveLine(
                item.Item.Id,
                item.Item.Number,
                item.Item.Description,
                item.Count,
                // the split works in net cents — the grave's subtotals are what
                // has to divide evenly across the recipients — so a VAT-included
                // price gives up its VAT here, before it is billed rather than
                // after
                VatInclusivePrice.NetOf(
                    EffectivePrice(item)!.Value,
                    item.Item.VatRate,
                    item.PriceIncludesVat
                ),
                item.Item.VatRate,
                item.PriceIncludesVat,
                item.PriceIncludesVat ? EffectivePrice(item)!.Value : null
            ))
            .ToList();

        // Every recipient is billed the same lines — the grave's own, exactly as
        // configured. The share is applied when the document is built, not by
        // dividing the lines, and nothing here adjusts a price: a grave whose
        // subtotals do not divide is refused when it is saved, and skipped here
        // if it reaches generation anyway.
        var problems = GraveShare.Problems(graveLines, recipients.Count);
        if (problems.Count > 0)
        {
            return new GraveGenerationResult(null, "PricesDoNotDivide");
        }

        var billedLines = graveLines;

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
