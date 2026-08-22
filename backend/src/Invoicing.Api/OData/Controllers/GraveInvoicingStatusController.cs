using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class GraveInvoicingStatusController : ODataController
{
    // The endpoint returns an OData-style { value: [...] } payload with the same
    // PascalCase keys the EDM-serialized entities use.
    private static readonly JsonSerializerOptions PascalCaseJson = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly AppDbContext _db;

    public GraveInvoicingStatusController(AppDbContext db) => _db = db;

    [HttpGet("GraveInvoicingStatus")]
    public async Task<IActionResult> Status([FromQuery] int year, [FromQuery] int half)
    {
        var invoicedIds = _db.GraveInvoiceRuns
            .Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToHashSet();

        var graves = await _db.Graves
            .OrderBy(g => !invoicedIds.Contains(g.Id))
            .ThenBy(g => g.GraveNumber)
            .Select(g => new { g.Id, g.GraveNumber })
            .ToListAsync();

        var value = graves.Select(g => new
        {
            g.Id,
            g.GraveNumber,
            Invoiced = invoicedIds.Contains(g.Id),
        });

        return new JsonResult(new { value }, PascalCaseJson);
    }
}
