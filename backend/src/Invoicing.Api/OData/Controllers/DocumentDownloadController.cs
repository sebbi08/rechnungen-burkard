using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

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

        return File(document.Content, "application/pdf", document.FileName);
    }
}
