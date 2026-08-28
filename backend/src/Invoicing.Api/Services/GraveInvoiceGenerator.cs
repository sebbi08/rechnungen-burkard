using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class GraveInvoiceGenerator
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly DocumentDataBuilder _dataBuilder;

    public GraveInvoiceGenerator(AppDbContext db, InvoiceNumberService numbers, DocumentDataBuilder dataBuilder)
    {
        _db = db;
        _numbers = numbers;
        _dataBuilder = dataBuilder;
    }

    /// <summary>Generates all invoices for one grave and half-year in a single
    /// transaction. Returns a result with a skip reason when the grave must be
    /// skipped (no recipients, no items, or an item without default price),
    /// otherwise the created invoices.</summary>
    public async Task<GraveGenerationResult> GenerateForGraveAsync(Grave grave, int year, int half, CancellationToken ct)
    {
        var recipients = await _db.GraveRecipients
            .Where(r => r.GraveId == grave.Id)
            .Select(r => r.Recipient)
            .ToListAsync(ct);

        var graveItems = await _db.GraveItems
            .Where(i => i.GraveId == grave.Id)
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

        if (graveItems.Any(i => i.Item.DefaultPrice is null))
        {
            return new GraveGenerationResult(null, "ItemWithoutPrice");
        }

        // total gross in cents: per line round(net cents * (100+rate)/100)
        static long TotalCents(GraveItem item)
        {
            var netCents = (long)Math.Round(item.Count * item.Item.DefaultPrice!.Value * 100m,
                0, MidpointRounding.AwayFromZero);
            return (long)Math.Round(netCents * (100m + item.Item.VatRate) / 100m,
                0, MidpointRounding.AwayFromZero);
        }

        long totalCents = 0;
        foreach (var item in graveItems)
        {
            totalCents += TotalCents(item);
        }

        var shares = ShareCalculator.SplitCents(totalCents, recipients.Count);
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
                RecipientId = recipients[r].Id,
                RecipientName = recipients[r].Name,
                RecipientAddress = recipients[r].Address,
                EndingText = settings.EndingText,
                StampWord = settings.StampWord,
                GeneratedAt = now,
            };

            invoice.Lines = DistributeShare(shares[r], graveItems);
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            invoice.RootId = invoice.Id;

            var data = _dataBuilder.Build(invoice, DocumentTexts.ShareText(sharePercent));
            invoice.Documents = new List<Document>
            {
                new()
                {
                    Kind = DocumentKind.Normal,
                    FileName = $"{invoice.InvoiceNumber}.pdf",
                    Content = DocumentService.GeneratePdf(data, isCopy: false),
                    GeneratedAt = now,
                },
                new()
                {
                    Kind = DocumentKind.Copy,
                    FileName = $"{invoice.InvoiceNumber}-Kopie.pdf",
                    Content = DocumentService.GeneratePdf(data, isCopy: true),
                    GeneratedAt = now,
                },
            };
            await _db.SaveChangesAsync(ct);
            invoices.Add(invoice);
        }

        _db.GraveInvoiceRuns.Add(new GraveInvoiceRun
        {
            GraveId = grave.Id,
            Year = year,
            Half = half,
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new GraveGenerationResult(invoices, null);
    }

    /// <summary>Distributes a share (in gross cents) across the grave's lines;
    /// the remainder is handed out one cent at a time to the first lines so the
    /// sum is exact. Each line's net is derived back from its gross cents.</summary>
    private static List<InvoiceLine> DistributeShare(long shareCents, List<GraveItem> graveItems)
    {
        var distributed = ShareCalculator.SplitCents(shareCents, graveItems.Count);

        var lines = new List<InvoiceLine>(graveItems.Count);
        for (var i = 0; i < graveItems.Count; i++)
        {
            var item = graveItems[i];
            var lineGross = distributed[i];
            var lineNet = (long)Math.Round(lineGross * 100m / (100m + item.Item.VatRate),
                0, MidpointRounding.AwayFromZero);
            var unitPrice = lineNet / 100m / item.Count;

            lines.Add(new InvoiceLine
            {
                ItemId = item.Item.Id,
                ItemNumber = item.Item.Number,
                Description = item.Item.Description,
                Count = item.Count,
                // 3 decimals keep round(count × unitPrice × 100) exactly equal to the
                // derived net for realistic counts, so the persisted line gross stays exact.
                UnitPrice = decimal.Round(unitPrice, 3, MidpointRounding.AwayFromZero),
                VatRate = item.Item.VatRate,
                LineNumber = i + 1,
            });
        }

        return lines;
    }
}

/// <summary>Result of generating a grave's invoices: either the created invoices
/// or a machine-readable skip reason.</summary>
public sealed record GraveGenerationResult(List<Invoice>? Invoices, string? SkipReason);
