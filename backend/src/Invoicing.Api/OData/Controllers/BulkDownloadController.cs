using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class BulkDownloadController : ODataController
{
    private readonly AppDbContext _db;

    public BulkDownloadController(AppDbContext db) => _db = db;

    [HttpPost("BulkDownloadDocuments")]
    public async Task<IActionResult> Bulk([FromBody] BulkDownloadRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        if (request.Kind is not ("Normal" or "Copy"))
        {
            return BadRequest(new
            {
                error = new { code = "InvalidKind", message = "Kind must be Normal or Copy." }
            });
        }

        var kind = request.Kind == "Normal" ? DocumentKind.Normal : DocumentKind.Copy;
        IQueryable<Document> query = _db.Documents.Where(d => d.Kind == kind);
        if (request.Year is not null && request.Half is not null)
        {
            query = query.Where(d => d.Invoice != null
                && d.Invoice.PeriodYear == request.Year && d.Invoice.PeriodHalf == request.Half);
        }

        var documents = await query
            .OrderBy(d => d.FileName)
            .ToListAsync();

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in documents)
            {
                var entry = zip.CreateEntry(document.FileName);
                await using var stream = entry.Open();
                await stream.WriteAsync(document.Content);
            }
        }

        var bytes = memory.ToArray();
        var fileName = request.Kind == "Normal" ? "rechnungen.zip" : "kopien.zip";
        return File(bytes, "application/zip", fileName);
    }

    public sealed class BulkDownloadRequest
    {
        public string Kind { get; set; } = "";
        public int? Year { get; set; }
        public int? Half { get; set; }
    }
}
