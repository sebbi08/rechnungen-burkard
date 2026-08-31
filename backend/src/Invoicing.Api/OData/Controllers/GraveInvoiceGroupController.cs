using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>The grave, half-year and recipients one grave invoice belongs to,
/// together with the grave's lines as they were invoiced. Editing a grave invoice
/// edits the grave's lines, not one recipient's share, so the dialog opens on
/// this rather than on the grave's current configuration — a grave edited after
/// the invoice was generated must not leak into a revision of that period.</summary>
[Route("odata")]
public class GraveInvoiceGroupController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoiceGroupController(AppDbContext db) => _db = db;

    [HttpGet("GraveInvoiceGroup")]
    public async Task<IActionResult> Group([FromQuery] long invoiceId)
    {
        var invoice = await _db
            .Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, HttpContext.RequestAborted);
        if (invoice is null)
        {
            return NotFound(
                new
                {
                    error = new
                    {
                        code = "InvoiceNotFound",
                        message = "The invoice does not exist.",
                    },
                }
            );
        }

        if (invoice.GraveId is null || invoice.PeriodYear is null || invoice.PeriodHalf is null)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "NotAGraveInvoice",
                        message = "The invoice does not belong to a grave.",
                    },
                }
            );
        }

        var siblings = await _db
            .Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i =>
                i.GraveId == invoice.GraveId
                && i.PeriodYear == invoice.PeriodYear
                && i.PeriodHalf == invoice.PeriodHalf
                && i.IsLatestRevision
            )
            .OrderBy(i => i.RecipientName)
            .ThenBy(i => i.Id)
            .ToListAsync(HttpContext.RequestAborted);

        var orderedLines = siblings
            .Select(s => (IReadOnlyList<InvoiceLine>)s.Lines.OrderBy(l => l.LineNumber).ToList())
            .ToList();

        // GraveUnitPrice is only uniform across the group because generation writes
        // it for every recipient in one batch. A sibling revised on its own through
        // CreateRevision never sets it and can also gain or lose lines, which would
        // make GraveInvoiceSplitter.Combine throw below — so a line-count mismatch
        // is reported instead of risking a wrong or crashing reconstruction.
        if (orderedLines.Select(lines => lines.Count).Distinct().Count() > 1)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "GraveInvoiceGroupInconsistent",
                        message = "The invoices of this grave and half-year do not carry the same lines.",
                    },
                }
            );
        }

        var graveNumber = await _db
            .Graves.AsNoTracking()
            .Where(g => g.Id == invoice.GraveId)
            .Select(g => g.GraveNumber)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (graveNumber is null)
        {
            return NotFound(
                new
                {
                    error = new { code = "GraveNotFound", message = "The grave does not exist." },
                }
            );
        }

        var value = new
        {
            GraveId = invoice.GraveId.Value,
            GraveNumber = graveNumber,
            PeriodYear = invoice.PeriodYear.Value,
            PeriodHalf = invoice.PeriodHalf.Value,
            // RootId is what the frontend navigates siblings with — InvoiceId is
            // that sibling's latest revision id, kept so the caller can filter the
            // currently-shown invoice out of the list.
            Recipients = siblings.Select(s => new
            {
                Id = s.RecipientId,
                Name = s.RecipientName,
                InvoiceId = s.Id,
                RootId = s.RootId,
            }),
            Lines = GraveLines(orderedLines)
                .Select(l => new
                {
                    l.ItemId,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.UnitPrice,
                    l.VatRate,
                    l.PriceIncludesVat,
                }),
        };

        return new JsonResult(value, ODataJsonDefaults.PascalCase);
    }

    /// <summary>The grave's lines as invoiced, given every sibling's lines already
    /// ordered by line number and confirmed to carry the same count. Invoices
    /// generated after InvoiceLine.GraveUnitPrice exists carry the price directly
    /// — but only when every sibling has it, not just the first, since a sibling
    /// revised on its own through the ordinary CreateRevision path never sets it.
    /// Any sibling missing it, and pre-migration rows in general, are reconstructed
    /// from the recipients' shares, which is best-effort.</summary>
    private static IReadOnlyList<GraveLine> GraveLines(
        IReadOnlyList<IReadOnlyList<InvoiceLine>> orderedLines
    )
    {
        var reference = orderedLines[0];
        if (orderedLines.All(lines => lines.All(l => l.GraveUnitPrice is not null)))
        {
            return reference
                .Select(l => new GraveLine(
                    l.ItemId ?? 0,
                    l.ItemNumber,
                    l.Description,
                    l.Count,
                    l.GraveUnitPrice!.Value,
                    l.VatRate,
                    l.PriceIncludesVat
                ))
                .ToList();
        }

        return GraveInvoiceSplitter.Combine(orderedLines);
    }
}
