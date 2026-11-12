using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Serves a quote version's one PDF. The document is not in the EDM,
/// so this plain EF read is the only way to its bytes.</summary>
[Route("odata")]
public class QuoteDownloadController : ODataController
{
    private readonly AppDbContext _db;

    public QuoteDownloadController(AppDbContext db) => _db = db;

    [HttpGet("Quotes({key})/Pdf")]
    public async Task<IActionResult> GetPdf([FromRoute] long key)
    {
        var document = await _db
            .QuoteDocuments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.QuoteId == key, HttpContext.RequestAborted);
        if (document is null)
        {
            return NotFound();
        }

        // inline, as DocumentDownloadController serves an invoice: the browser
        // shows it, and the file name travels along for a save from the viewer
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(document.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();

        return File(document.Content, "application/pdf");
    }
}
