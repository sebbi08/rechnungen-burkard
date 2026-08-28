using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.Net.Http.Headers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class DocumentDownloadController : ODataController
{
    private readonly AppDbContext _db;

    public DocumentDownloadController(AppDbContext db) => _db = db;

    [HttpGet("Documents({key})/$value")]
    public IActionResult GetStream([FromRoute] long key)
    {
        var document = _db.Documents.Find(key);
        if (document is null)
        {
            return NotFound();
        }

        // inline, so the browser shows the invoice in the tab the UI opens for
        // it; the file name still travels along for a save from the viewer
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(document.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();

        return File(document.Content, "application/pdf");
    }
}
