using System.IO.Compression;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

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
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidRequest",
                        message = "The request body is required.",
                    },
                }
            );
        }

        // FR-047: a copy is downloaded on its own, never in bulk. The kind stays
        // in the request so a client that asks for copies is told no.
        if (request.Kind != "Normal")
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidKind",
                        message = "Only normal documents are downloaded in bulk.",
                    },
                }
            );
        }

        // FR-045: the grave invoices of one half-year. Without both parts the
        // filter used to be dropped, and a year alone downloaded every invoice
        // there is.
        if (request.Year is not { } year || request.Half is not (1 or 2))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidHalfYear",
                        message = "A year and a half (1 or 2) are required.",
                    },
                }
            );
        }

        // a superseded revision's documents were replaced; a bulk download must
        // not put them next to the revision that replaced them
        var query = _db.Documents.Where(d =>
            d.Kind == DocumentKind.Normal
            && d.Invoice != null
            && d.Invoice.IsLatestRevision
            && d.Invoice.PeriodYear == year
            && d.Invoice.PeriodHalf == request.Half
        );

        var documents = await query.OrderBy(d => d.FileName).ToListAsync();

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
        return File(bytes, "application/zip", "rechnungen.zip");
    }

    public sealed class BulkDownloadRequest
    {
        public string Kind { get; set; } = "";
        public int? Year { get; set; }
        public int? Half { get; set; }
    }
}
