using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class GraveInvoicingStatusController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoicingStatusController(AppDbContext db) => _db = db;

    /// <summary>The grave overview for one half-year: graves that still need an
    /// invoice for it come first (FR-028), each with the flag the list shows,
    /// plus how many invoices that half-year produced for the grave and how
    /// many of them are paid (latest revisions only).</summary>
    [HttpGet("GraveInvoicingStatus")]
    public async Task<IActionResult> Status([FromQuery] int year, [FromQuery] int half)
    {
        var invoicedIds = _db
            .GraveInvoiceRuns.Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToHashSet();

        // How many invoices this half-year produced per grave, and how many of
        // them are paid. Latest revisions only, matching every list that shows
        // them. Counted here rather than derived by the client so the overview
        // stays one request.
        var payment = await _db
            .Invoices.Where(i =>
                i.GraveId != null
                && i.PeriodYear == year
                && i.PeriodHalf == half
                && i.IsLatestRevision
            )
            .GroupBy(i => i.GraveId!.Value)
            .Select(g => new
            {
                GraveId = g.Key,
                InvoiceCount = g.Count(),
                PaidCount = g.Count(i => i.PaidOn != null),
            })
            .ToDictionaryAsync(row => row.GraveId);

        var graves = await _db
            .Graves
            // false sorts before true, so ordering by "is invoiced" puts the
            // graves that are still open at the top
            .OrderBy(g => invoicedIds.Contains(g.Id))
            .ThenBy(g => g.GraveNumber)
            .Select(g => new
            {
                g.Id,
                g.GraveNumber,
                g.FieldNumber,
                g.SiteName,
                g.StartDate,
                g.EndDate,
            })
            .ToListAsync();

        var links = await GraveBilling.LinksByGraveAsync(_db);

        // a grave is due when its term reaches this half-year and its links bill
        // something in it; one that was invoiced for it anyway stays listed
        var value = graves
            .Where(g =>
                invoicedIds.Contains(g.Id)
                || GraveBilling.IsDue(
                    g.StartDate,
                    g.EndDate,
                    links.GetValueOrDefault(g.Id) ?? Array.Empty<BillableLink>(),
                    year,
                    half
                )
            )
            .Select(g => new
            {
                g.Id,
                g.GraveNumber,
                g.FieldNumber,
                g.SiteName,
                g.StartDate,
                g.EndDate,
                Invoiced = invoicedIds.Contains(g.Id),
                InvoiceCount = payment.TryGetValue(g.Id, out var counts) ? counts.InvoiceCount : 0,
                PaidCount = payment.TryGetValue(g.Id, out var paid) ? paid.PaidCount : 0,
            });

        return new JsonResult(new { value }, ODataJsonDefaults.PascalCase);
    }
}
