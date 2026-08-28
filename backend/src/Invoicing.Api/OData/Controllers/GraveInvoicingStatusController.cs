using Burkard.Invoicing.Api.Persistence;
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
    /// invoice for it come first (FR-028), each with the flag the list shows.</summary>
    [HttpGet("GraveInvoicingStatus")]
    public async Task<IActionResult> Status([FromQuery] int year, [FromQuery] int half)
    {
        var invoicedIds = _db.GraveInvoiceRuns
            .Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToHashSet();

        var graves = await _db.Graves
            // false sorts before true, so ordering by "is invoiced" puts the
            // graves that are still open at the top
            .OrderBy(g => invoicedIds.Contains(g.Id))
            .ThenBy(g => g.GraveNumber)
            .Select(g => new { g.Id, g.GraveNumber, g.StartDate, g.EndDate })
            .ToListAsync();

        var value = graves.Select(g => new
        {
            g.Id,
            g.GraveNumber,
            g.StartDate,
            g.EndDate,
            Invoiced = invoicedIds.Contains(g.Id),
        });

        return new JsonResult(new { value }, ODataJsonDefaults.PascalCase);
    }
}
