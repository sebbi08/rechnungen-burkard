using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

/// <summary>Revises a grave's invoices for one half-year. A grave invoice bills
/// one grave's own lines, so editing it edits the bill: every recipient of that
/// grave and half-year gets a new revision carrying the grave's edited lines in
/// full. Those lines have to split evenly across the recipients, as they do
/// when the grave is saved; a revision that does not is refused.</summary>
public sealed class GraveInvoiceRevisionService
{
    private readonly AppDbContext _db;
    private readonly InvoiceDocumentFactory _documentFactory;
    private readonly IClock _clock;

    public GraveInvoiceRevisionService(
        AppDbContext db,
        InvoiceDocumentFactory documentFactory,
        IClock clock
    )
    {
        _db = db;
        _documentFactory = documentFactory;
        _clock = clock;
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

        // the edited revision's descriptions carry what generation wrote, such
        // as ", anteilig 05–06/2026 (2 von 6 Monaten)"; the item's own text
        // would drop it on every edit
        var previous = await _db
            .InvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == edited.Id)
            .OrderBy(l => l.LineNumber)
            .ToListAsync(ct);

        var siblings = await _db
            .Invoices.Include(i => i.Lines)
            .Where(i =>
                i.GraveId == edited.GraveId
                && i.PeriodYear == edited.PeriodYear
                && i.PeriodHalf == edited.PeriodHalf
                && i.IsLatestRevision
            )
            // Every recipient gets the same lines, so processing order no longer
            // changes what any of them is billed; ordering by id just keeps the
            // order deterministic across runs.
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        if (siblings.Count == 0)
        {
            throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");
        }

        // the lines the dialog opened on, read the way the group endpoint read
        // them for it: a price left as shown is taken back as shown
        if (!GraveInvoiceGroupLines.CarrySameLines(siblings))
        {
            throw new ValidationException(
                "GraveInvoiceGroupInconsistent",
                "The invoices of this grave and half-year do not carry the same lines."
            );
        }
        var shown = GraveInvoiceGroupLines.AsInvoiced(siblings);
        var graveLines = await BuildGraveLinesAsync(lineInputs, previous, shown, ct);

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

        // Saving a grave and generating its invoices both refuse prices that do
        // not split evenly; without the same check here an edit issued every
        // recipient a total with fractions of a cent, PDFs included.
        var problems = GraveShare.Problems(graveLines, siblings.Count);
        if (problems.Count > 0)
        {
            throw new GraveShareValidationException(problems, siblings.Count);
        }

        // A grave's recipients are fixed once invoiced, so the group is always the grave's
        // full set. Every recipient is billed the same lines — the grave's own,
        // exactly as the dialog sent them, the same way generation bills them.
        var billedLines = graveLines;
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        // like the address, a revision takes the grave as it is now
        var grave = await _db.Graves.AsNoTracking().SingleAsync(g => g.Id == edited.GraveId, ct);
        var sharePercent = 100m / siblings.Count;
        // the wall-clock here, like the number's year (see InvoiceService)
        var now = _clock.GetLocalNow();

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
                RecipientCount = siblings.Count,
                RecipientId = recipient.Id,
                // a revision is newly generated, so it uses the recipient's
                // current name and address (FR-017)
                RecipientName = recipient.Name,
                RecipientAddress = recipient.Address,
                RecipientNumber = recipient.Number,
                GraveNumber = grave.GraveNumber,
                GraveSiteName = grave.SiteName,
                GraveFieldNumber = grave.FieldNumber,
                GravePosition = grave.Position,
                // the half-year the invoice bills, whenever it is revised
                EndingText = settings.GraveEndingText(sibling.PeriodHalf!.Value),
                StampWord = settings.StampWord,
                GeneratedAt = now,
                Lines = billedLines
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
                                LineNumber = index + 1,
                            }
                    )
                    .ToList(),
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

    /// <summary>The grave's lines from what the dialog sent: the full count at
    /// the full price, which is what every recipient is billed.
    ///
    /// The order is the request body's, preserved line for line — nothing here
    /// re-sorts or groups, so the lines come back in the order they are shown
    /// and their line numbers stay put across a revision.
    ///
    /// <paramref name="previous"/> are the edited revision's own lines and
    /// <paramref name="shown"/> the grave's lines as the dialog showed them,
    /// line for line in the same order.</summary>
    private async Task<List<GraveLine>> BuildGraveLinesAsync(
        IReadOnlyList<InvoiceLineInput> inputs,
        List<InvoiceLine> previous,
        IReadOnlyList<GraveLine> shown,
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

        var unused = previous.ToList();
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

            // matched by item in line order, each earlier line used once; a
            // newly added item takes its own description
            var earlier = unused.FirstOrDefault(l => l.ItemId == item.Id);
            decimal? shownPrice = null;
            if (earlier is not null)
            {
                var index = previous.IndexOf(earlier);
                shownPrice = index < shown.Count ? shown[index].UnitPrice : null;
                unused.Remove(earlier);
            }

            // The document prints the price to the cent and multiplies the
            // unrounded one, so a third decimal is refused wherever a price is
            // typed -- not where it was not: a grave invoice from before prices
            // were held to the cent keeps its net at three decimals, the dialog
            // shows it as stored, and a line left as shown saves as shown.
            if (
                input.UnitPrice is { } typed
                && !GraveBilling.IsWholeCents(typed)
                && typed != shownPrice
            )
            {
                throw new ValidationException(
                    "InvalidPrice",
                    $"The price for item '{item.Number}' has more than two decimals."
                );
            }

            lines.Add(
                new GraveLine(
                    item.Id,
                    item.Number,
                    earlier?.Description ?? item.Description,
                    input.Count,
                    // grave prices are net; the input's PriceIncludesVat is
                    // ignored, as its Comment is
                    unitPrice,
                    item.VatRate
                )
            );
        }

        return lines;
    }
}
