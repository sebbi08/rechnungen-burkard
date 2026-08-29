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
        var invoice = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, HttpContext.RequestAborted);
        if (invoice is null)
        {
            return NotFound(new
            {
                error = new { code = "InvoiceNotFound", message = "The invoice does not exist." }
            });
        }

        if (invoice.GraveId is null || invoice.PeriodYear is null || invoice.PeriodHalf is null)
        {
            return BadRequest(new
            {
                error = new { code = "NotAGraveInvoice", message = "The invoice does not belong to a grave." }
            });
        }

        var siblings = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.GraveId == invoice.GraveId
                        && i.PeriodYear == invoice.PeriodYear
                        && i.PeriodHalf == invoice.PeriodHalf
                        && i.IsLatestRevision)
            .OrderBy(i => i.RecipientName)
            .ThenBy(i => i.Id)
            .ToListAsync(HttpContext.RequestAborted);

        var graveNumber = await _db.Graves.AsNoTracking()
            .Where(g => g.Id == invoice.GraveId)
            .Select(g => g.GraveNumber)
            .FirstAsync(HttpContext.RequestAborted);

        var value = new
        {
            GraveId = invoice.GraveId.Value,
            GraveNumber = graveNumber,
            PeriodYear = invoice.PeriodYear.Value,
            PeriodHalf = invoice.PeriodHalf.Value,
            Recipients = siblings.Select(s => new { Id = s.RecipientId, Name = s.RecipientName, InvoiceId = s.Id }),
            Lines = GraveLines(siblings).Select(l => new
            {
                l.ItemId, l.ItemNumber, l.Description, l.Count, l.UnitPrice, l.VatRate,
            }),
        };

        return new JsonResult(value, ODataJsonDefaults.PascalCase);
    }

    /// <summary>The grave's lines as invoiced. Invoices generated after
    /// InvoiceLine.GraveUnitPrice exists carry the price directly; older ones are
    /// reconstructed from the recipients' shares, which is best-effort.</summary>
    private static IReadOnlyList<GraveLine> GraveLines(List<Invoice> siblings)
    {
        var reference = siblings[0].Lines.OrderBy(l => l.LineNumber).ToList();
        if (reference.All(l => l.GraveUnitPrice is not null))
        {
            return reference
                .Select(l => new GraveLine(l.ItemId ?? 0, l.ItemNumber, l.Description,
                    l.Count, l.GraveUnitPrice!.Value, l.VatRate))
                .ToList();
        }

        return GraveInvoiceSplitter.Combine(siblings
            .Select(s => (IReadOnlyList<InvoiceLine>)s.Lines.OrderBy(l => l.LineNumber).ToList())
            .ToList());
    }
}
