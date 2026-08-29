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
    private readonly IDocumentGenerator _documents;

    public GraveInvoiceGenerator(AppDbContext db, InvoiceNumberService numbers,
        DocumentDataBuilder dataBuilder, IDocumentGenerator documents)
    {
        _db = db;
        _numbers = numbers;
        _dataBuilder = dataBuilder;
        _documents = documents;
    }

    /// <summary>Generates all invoices for one grave and half-year in a single
    /// transaction. Returns a result with a skip reason when the grave must be
    /// skipped (no recipients, no items, or an item with no price at all),
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

        if (graveItems.Any(i => EffectivePrice(i) is null))
        {
            return new GraveGenerationResult(null, "ItemWithoutPrice");
        }

        var graveLines = graveItems
            .Select(item => new GraveLine(
                item.Item.Id, item.Item.Number, item.Item.Description,
                item.Count, EffectivePrice(item)!.Value, item.Item.VatRate))
            .ToList();
        var perRecipient = GraveInvoiceSplitter.Split(graveLines, recipients.Count);

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
                Language = settings.Language,
                GeneratedAt = now,
            };

            invoice.Lines = perRecipient[r].ToList();
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            invoice.RootId = invoice.Id;

            var data = _dataBuilder.Build(invoice);
            invoice.Documents = new List<Document>
            {
                new()
                {
                    Kind = DocumentKind.Normal,
                    FileName = DocumentNaming.NormalFileName(invoice.InvoiceNumber),
                    Content = _documents.Generate(data, isCopy: false),
                    GeneratedAt = now,
                },
                new()
                {
                    Kind = DocumentKind.Copy,
                    FileName = DocumentNaming.CopyFileName(invoice.InvoiceNumber),
                    Content = _documents.Generate(data, isCopy: true),
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

    /// <summary>The price a grave's line is billed at: the link's own price when
    /// the grave overrides it, otherwise the item's default price.</summary>
    private static decimal? EffectivePrice(GraveItem item) => item.UnitPrice ?? item.Item.DefaultPrice;
}

/// <summary>Result of generating a grave's invoices: either the created invoices
/// or a machine-readable skip reason.</summary>
public sealed record GraveGenerationResult(List<Invoice>? Invoices, string? SkipReason);
