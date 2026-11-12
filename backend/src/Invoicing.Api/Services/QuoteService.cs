using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Creates quotes and their revisions. A quote takes its input like an
/// ordinary invoice (<see cref="LineBuilder"/>), its own number from
/// <see cref="QuoteNumberCounter"/>, and one PDF — the invoice layout titled
/// Angebot, never a copy.</summary>
public sealed class QuoteService
{
    private readonly AppDbContext _db;
    private readonly LineBuilder _lines;
    private readonly DocumentDataBuilder _dataBuilder;
    private readonly IDocumentGenerator _documents;
    private readonly IClock _clock;

    public QuoteService(
        AppDbContext db,
        LineBuilder lines,
        DocumentDataBuilder dataBuilder,
        IDocumentGenerator documents,
        IClock clock
    )
    {
        _db = db;
        _lines = lines;
        _dataBuilder = dataBuilder;
        _documents = documents;
        _clock = clock;
    }

    public async Task<Quote> CreateAsync(
        long recipientId,
        IReadOnlyList<InvoiceLineInput> lineInputs,
        CancellationToken ct
    )
    {
        var recipient = await RecipientAsync(recipientId, ct);
        var lines = await _lines.BuildAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        RequireAddress(recipient);

        // SQLite opens the transaction with BEGIN IMMEDIATE, so it holds the
        // write lock from before the counter is read until the quote is in, as
        // InvoiceService.PersistAsync does for invoice numbers: creates at once
        // take turns rather than read the same sequence.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = _clock.GetLocalNow();
        var year = now.Year;
        var counter = await _db.QuoteNumberCounters.SingleOrDefaultAsync(c => c.Year == year, ct);
        if (counter is null)
        {
            counter = new QuoteNumberCounter { Year = year };
            _db.QuoteNumberCounters.Add(counter);
        }
        counter.LastSequence++;

        var quote = new Quote
        {
            RevisionNumber = 0,
            Year = year,
            Sequence = counter.LastSequence,
            QuoteNumber = InvoiceNumberFormatter.ForQuote(year, counter.LastSequence),
            State = QuoteState.Open,
            GeneratedAt = now,
        };
        Snapshot(quote, recipient, settings, lines);

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);
        quote.RootId = quote.Id;
        quote.Document = Render(quote, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quote;
    }

    public async Task<Quote> CreateRevisionAsync(
        long quoteId,
        long recipientId,
        IReadOnlyList<InvoiceLineInput> lineInputs,
        CancellationToken ct
    )
    {
        var edited =
            await _db.Quotes.SingleOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new ValidationException("QuoteNotFound", "The quote does not exist.");

        var recipient = await RecipientAsync(recipientId, ct);
        var lines = await _lines.BuildAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        RequireAddress(recipient);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // the revision number continues the whole chain, not the row that was
        // edited, as for invoices: editing an older version must not collide on
        // (Year, Sequence, RevisionNumber)
        var revisionNumber =
            await _db
                .Quotes.Where(q => q.RootId == edited.RootId)
                .MaxAsync(q => q.RevisionNumber, ct) + 1;
        var now = _clock.GetLocalNow();

        var revision = new Quote
        {
            RootId = edited.RootId,
            RevisionNumber = revisionNumber,
            // the number's year stays the original's, whenever it is revised
            Year = edited.Year,
            Sequence = edited.Sequence,
            QuoteNumber = InvoiceNumberFormatter.ForRevision(
                InvoiceNumberFormatter.StripRevision(edited.QuoteNumber),
                revisionNumber
            ),
            State = edited.State,
            InvoiceId = edited.InvoiceId,
            GeneratedAt = now,
        };
        Snapshot(revision, recipient, settings, lines);

        // the chain has exactly one latest version, and it is now this one
        var superseded = await _db
            .Quotes.Where(q => q.RootId == edited.RootId && q.IsLatestRevision)
            .ToListAsync(ct);
        foreach (var quote in superseded)
        {
            quote.IsLatestRevision = false;
        }

        _db.Quotes.Add(revision);
        await _db.SaveChangesAsync(ct);
        revision.Document = Render(revision, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return revision;
    }

    private async Task<Recipient> RecipientAsync(long recipientId, CancellationToken ct) =>
        await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
        ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

    private static void RequireAddress(Recipient recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException(
                "RecipientAddressRequired",
                "The recipient address is required."
            );
        }
    }

    /// <summary>What a version prints, taken as it is now: a later change to the
    /// recipient, an item or the settings never alters a version already
    /// sent.</summary>
    private static void Snapshot(
        Quote quote,
        Recipient recipient,
        GlobalSettings settings,
        List<InvoiceLine> lines
    )
    {
        quote.RecipientId = recipient.Id;
        quote.RecipientName = recipient.Name;
        quote.RecipientAddress = recipient.Address;
        quote.RecipientNumber = recipient.Number;
        quote.EndingText = settings.EndingText;
        quote.Lines = lines
            .Select(l => new QuoteLine
            {
                ItemId = l.ItemId,
                ItemNumber = l.ItemNumber,
                Description = l.Description,
                Count = l.Count,
                UnitPrice = l.UnitPrice,
                VatRate = l.VatRate,
                PriceIncludesVat = l.PriceIncludesVat,
                LineNumber = l.LineNumber,
            })
            .ToList();
    }

    private QuoteDocument Render(Quote quote, DateTime generatedAt) =>
        new()
        {
            FileName = DocumentNaming.NormalFileName(quote.QuoteNumber),
            Content = _documents.Generate(_dataBuilder.Build(quote), isCopy: false),
            GeneratedAt = generatedAt,
        };
}
