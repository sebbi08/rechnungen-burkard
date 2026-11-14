using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>FR-045a: a half-year's grave invoices as one PDF to print — the
/// documents the bulk download hands out, in its order, joined as stored.</summary>
[Route("odata")]
public class GraveInvoicePrintController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoicePrintController(AppDbContext db) => _db = db;

    [HttpGet("GraveInvoicesPdf")]
    public async Task<IActionResult> Print([FromQuery] string? year, [FromQuery] string? half)
    {
        if (
            !int.TryParse(year, System.Globalization.NumberStyles.None, null, out var y)
            || half is not ("1" or "2")
        )
        {
            return Refuse("InvalidHalfYear", "A year and a half (1 or 2) are required.");
        }

        var h = half == "1" ? 1 : 2;
        var documents = await GraveInvoiceDocuments
            .OfHalfYear(_db, y, h)
            .Select(d => d.Content)
            .ToListAsync();
        if (documents.Count == 0)
        {
            return Refuse("NoGraveInvoices", "There are no grave invoices for this half-year.");
        }

        // inline, so the browser shows the invoices in the tab the UI opens for
        // them; the file name still travels along for a save from the viewer
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName($"Grabrechnungen-{y}-{h}.pdf");
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(PdfJoiner.Join(documents), "application/pdf");
    }

    private BadRequestObjectResult Refuse(string code, string message) =>
        BadRequest(new { error = new { code, message } });
}
