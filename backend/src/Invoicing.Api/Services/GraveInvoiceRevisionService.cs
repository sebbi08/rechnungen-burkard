using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Revises a grave's invoices for one half-year. A grave invoice is one
/// recipient's share of one grave bill, so editing it edits the bill: every
/// recipient of that grave and half-year gets a new revision, split the same way
/// generation splits it.</summary>
public sealed class GraveInvoiceRevisionService
{
    private readonly AppDbContext _db;
    private readonly InvoiceDocumentFactory _documentFactory;

    public GraveInvoiceRevisionService(AppDbContext db, InvoiceDocumentFactory documentFactory)
    {
        _db = db;
        _documentFactory = documentFactory;
    }

    public async Task<List<Invoice>> CreateAsync(
        long invoiceId,
        IReadOnlyList<InvoiceLineInput> lineInputs,
        CancellationToken ct
    )
    {
        var edited =
            await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

        if (edited.GraveId is null || edited.PeriodYear is null || edited.PeriodHalf is null)
        {
            throw new ValidationException(
                "NotAGraveInvoice",
                "The invoice does not belong to a grave."
            );
        }

        var graveLines = await BuildGraveLinesAsync(lineInputs, ct);

        var siblings = await _db
            .Invoices.Where(i =>
                i.GraveId == edited.GraveId
                && i.PeriodYear == edited.PeriodYear
                && i.PeriodHalf == edited.PeriodHalf
                && i.IsLatestRevision
            )
            // generation assigned these ids in the order it split the grave's
            // lines (one SaveChangesAsync per recipient, in recipient-list
            // order), so ordering by id puts every recipient back at the same
            // GraveInvoiceSplitter position generation gave them — ordering by
            // name instead would hand SplitCents' remainder cent to whichever
            // recipient sorts first alphabetically, silently moving it away
            // from the recipient generation gave it to.
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        if (siblings.Count == 0)
        {
            throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");
        }

        // One edit revises every recipient of the half-year, so one paid
        // recipient blocks all of them. Naming them is the point: from the row
        // the user pressed edit on, another recipient's payment is invisible.
        var paidRecipients = siblings
            .Where(s => s.PaidOn is not null)
            .Select(s => s.RecipientName)
            .ToList();
        if (paidRecipients.Count > 0)
        {
            throw new ValidationException(
                "GraveInvoicePaid",
                $"The invoices of {string.Join(", ", paidRecipients)} are paid and cannot be revised."
            );
        }

        // A grave's recipients cannot change, so the group is always the grave's
        // full set and the split matches the one generation used.
        var perRecipient = GraveInvoiceSplitter.Split(graveLines, siblings.Count);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        var sharePercent = 100m / siblings.Count;
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var created = new List<Invoice>(siblings.Count);
        for (var r = 0; r < siblings.Count; r++)
        {
            var sibling = siblings[r];
            var recipient =
                await _db.Recipients.FindAsync(new object[] { sibling.RecipientId }, ct)
                ?? throw new ValidationException(
                    "RecipientNotFound",
                    "The recipient does not exist."
                );
            if (string.IsNullOrWhiteSpace(recipient.Address))
            {
                throw new ValidationException(
                    "RecipientAddressRequired",
                    "The recipient address is required."
                );
            }

            // the number continues the whole chain, not just the row that was edited
            var lastRevision =
                await _db
                    .Invoices.Where(i => i.RootId == sibling.RootId)
                    .MaxAsync(i => (int?)i.RevisionNumber, ct)
                ?? sibling.RevisionNumber;
            var revisionNumber = lastRevision + 1;
            var baseNumber = InvoiceNumberFormatter.StripRevision(sibling.InvoiceNumber);

            var revision = new Invoice
            {
                RootId = sibling.RootId,
                RevisionNumber = revisionNumber,
                Year = sibling.Year,
                Sequence = sibling.Sequence,
                InvoiceNumber = InvoiceNumberFormatter.ForRevision(baseNumber, revisionNumber),
                GraveId = sibling.GraveId,
                PeriodYear = sibling.PeriodYear,
                PeriodHalf = sibling.PeriodHalf,
                SharePercent = sharePercent,
                RecipientId = recipient.Id,
                // a revision is newly generated, so it uses the recipient's
                // current name and address (FR-017)
                RecipientName = recipient.Name,
                RecipientAddress = recipient.Address,
                EndingText = settings.EndingText,
                StampWord = settings.StampWord,
                Language = settings.Language,
                GeneratedAt = now,
                Lines = perRecipient[r].ToList(),
            };

            sibling.IsLatestRevision = false;
            _db.Invoices.Add(revision);
            await _db.SaveChangesAsync(ct);

            revision.Documents = _documentFactory.Build(revision, now);
            await _db.SaveChangesAsync(ct);
            created.Add(revision);
        }

        await tx.CommitAsync(ct);
        return created;
    }

    /// <summary>The grave's lines from what the dialog sent: the full count at the
    /// full price, before any split.</summary>
    private async Task<List<GraveLine>> BuildGraveLinesAsync(
        IReadOnlyList<InvoiceLineInput> inputs,
        CancellationToken ct
    )
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db
            .InvoiceItems.Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var lines = new List<GraveLine>(inputs.Count);
        foreach (var input in inputs)
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException(
                    $"ItemNotFound:{input.ItemId}",
                    $"The item {input.ItemId} does not exist."
                );
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice =
                input.UnitPrice
                ?? item.DefaultPrice
                ?? throw new ValidationException(
                    "PriceRequired",
                    $"A price is required for item '{item.Number}'."
                );

            lines.Add(
                new GraveLine(
                    item.Id,
                    item.Number,
                    item.Description,
                    input.Count,
                    // the dialog sends the grave's price as it shows it, which
                    // for a VAT-included line is the gross; the split needs the net
                    VatInclusivePrice.NetOf(unitPrice, item.VatRate, input.PriceIncludesVat),
                    item.VatRate,
                    input.PriceIncludesVat
                )
            );
        }

        return lines;
    }
}
