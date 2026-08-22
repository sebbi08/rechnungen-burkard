using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceLineInput
{
    public long ItemId { get; set; }
    public int Count { get; set; }
    public decimal? UnitPrice { get; set; }
}

public sealed class InvoiceService
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly DocumentDataBuilder _dataBuilder;
    private readonly IDocumentGenerator _documents;

    public InvoiceService(AppDbContext db, InvoiceNumberService numbers,
        DocumentDataBuilder dataBuilder, IDocumentGenerator documents)
    {
        _db = db;
        _numbers = numbers;
        _dataBuilder = dataBuilder;
        _documents = documents;
    }

    public async Task<Invoice> CreateAsync(long recipientId, IReadOnlyList<InvoiceLineInput> lineInputs, CancellationToken ct)
    {
        var recipient = await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

        var lines = await BuildLinesAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(recipient, settings, lines, ct);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // unique (Year, Sequence) race — drop the failed attempt's tracked
                // inserts (they stay Added otherwise and poison every retry),
                // then retry with a fresh number
                _db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<Invoice> CreateRevisionAsync(long invoiceId, long recipientId,
        IReadOnlyList<InvoiceLineInput> lineInputs, CancellationToken ct)
    {
        var original = await _db.Invoices
            .Include(i => i.Lines).Include(i => i.Documents)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        var recipient = await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

        var lines = await BuildLinesAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        var now = DateTime.UtcNow;
        var baseNumber = InvoiceNumberFormatter.StripRevision(original.InvoiceNumber);

        var revision = new Invoice
        {
            RootId = original.RootId,
            RevisionNumber = original.RevisionNumber + 1,
            Year = original.Year,
            Sequence = original.Sequence,
            InvoiceNumber = InvoiceNumberFormatter.ForRevision(baseNumber, original.RevisionNumber + 1),
            GraveId = original.GraveId,
            PeriodYear = original.PeriodYear,
            PeriodHalf = original.PeriodHalf,
            SharePercent = original.SharePercent,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            Lines = lines,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Invoices.Add(revision);
        await _db.SaveChangesAsync(ct);

        var data = _dataBuilder.Build(revision, shareText: null);
        revision.Documents = new List<Document>
        {
            new()
            {
                Kind = DocumentKind.Normal,
                FileName = $"{revision.InvoiceNumber}.pdf",
                Content = _documents.Generate(data, isCopy: false),
                GeneratedAt = now,
            },
            new()
            {
                Kind = DocumentKind.Copy,
                FileName = $"{revision.InvoiceNumber}-Kopie.pdf",
                Content = _documents.Generate(data, isCopy: true),
                GeneratedAt = now,
            },
        };

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return revision;
    }

    private async Task<List<InvoiceLine>> BuildLinesAsync(IReadOnlyList<InvoiceLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db.InvoiceItems
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var result = new List<InvoiceLine>(inputs.Count);
        foreach (var (input, index) in inputs.Select((l, i) => (l, i)))
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException($"ItemNotFound:{input.ItemId}", $"The item {input.ItemId} does not exist.");
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice = input.UnitPrice ?? item.DefaultPrice;
            if (unitPrice is null)
            {
                throw new ValidationException("PriceRequired", $"A price is required for item '{item.Number}'.");
            }

            result.Add(new InvoiceLine
            {
                ItemId = item.Id,
                ItemNumber = item.Number,
                Description = item.Description,
                Count = input.Count,
                UnitPrice = unitPrice.Value,
                VatRate = item.VatRate,
                LineNumber = index + 1,
            });
        }

        return result;
    }

    private async Task<Invoice> PersistAsync(Recipient recipient, GlobalSettings settings,
        List<InvoiceLine> lines, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException("RecipientAddressRequired", "The recipient address is required.");
        }

        var (number, year, sequence) = await _numbers.AllocateAsync(ct);
        var now = DateTime.UtcNow;

        var invoice = new Invoice
        {
            RevisionNumber = 0,
            Year = year,
            Sequence = sequence,
            InvoiceNumber = number,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            Lines = lines,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        invoice.RootId = invoice.Id;

        var data = _dataBuilder.Build(invoice, shareText: null);
        invoice.Documents = new List<Document>
        {
            new()
            {
                Kind = DocumentKind.Normal,
                FileName = $"{invoice.InvoiceNumber}.pdf",
                Content = _documents.Generate(data, isCopy: false),
                GeneratedAt = now,
            },
            new()
            {
                Kind = DocumentKind.Copy,
                FileName = $"{invoice.InvoiceNumber}-Kopie.pdf",
                Content = _documents.Generate(data, isCopy: true),
                GeneratedAt = now,
            },
        };

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice;
    }
}
