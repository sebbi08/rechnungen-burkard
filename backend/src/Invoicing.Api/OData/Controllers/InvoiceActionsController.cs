using System.Text.Json;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Microsoft.AspNetCore.Mvc.Route("odata")]
public class InvoiceActionsController : ODataController
{
    private readonly InvoiceService _service;
    private readonly JobService _jobs;
    private readonly AppDbContext _db;
    private readonly ILogger<InvoiceActionsController> _logger;

    public InvoiceActionsController(InvoiceService service, JobService jobs, AppDbContext db, ILogger<InvoiceActionsController> logger)
    {
        _service = service;
        _jobs = jobs;
        _db = db;
        _logger = logger;
    }

    [HttpPost("CreateInvoice")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var invoice = await _service.CreateAsync(request.RecipientId, request.Lines, HttpContext.RequestAborted);
            return Created($"odata/Invoices({invoice.Id})", invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            // Letting the exception escape would cause the test host to rethrow it on the
            // client instead of producing a 500 response; mirror production behavior.
            _logger.LogError(ex, "CreateInvoice failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = new { code = "InternalError", message = "An internal error occurred." } });
        }
    }

    [HttpPost("CreateRevision")]
    public async Task<IActionResult> CreateRevision([FromBody] CreateRevisionRequest request)
    {
        try
        {
            var invoice = await _service.CreateRevisionAsync(
                request.InvoiceId, request.RecipientId, request.Lines, HttpContext.RequestAborted);
            return Created($"odata/Invoices({invoice.Id})", invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRevision failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = new { code = "InternalError", message = "An internal error occurred." } });
        }
    }

    [HttpPost("GenerateGraveInvoices")]
    public async Task<IActionResult> GenerateGraveInvoices([FromBody] GenerateGraveInvoicesRequest request)
    {
        if (request.Year < 2000 || request.Year > 2100 || request.Half is not (1 or 2))
        {
            return BadRequest(new
            {
                error = new { code = "InvalidHalfYear", message = "The half-year is invalid." }
            });
        }

        var alreadyInvoicedCount = _db.GraveInvoiceRuns
            .Where(r => r.Year == request.Year && r.Half == request.Half)
            .Select(r => r.GraveId)
            .Distinct()
            .Count();
        if (alreadyInvoicedCount > 0 && alreadyInvoicedCount >= _db.Graves.Count())
        {
            return Conflict(new
            {
                error = new { code = "GravesAlreadyInvoiced", message = "Invoices for this half-year were already generated." }
            });
        }

        if (_jobs.HasActive("GraveInvoices"))
        {
            return Conflict(new
            {
                error = new { code = "JobAlreadyRunning", message = "A generation is already running." }
            });
        }

        var total = _db.Graves.Count();
        var job = await _jobs.EnqueueAsync("GraveInvoices",
            JsonSerializer.Serialize(new GraveInvoicesPayload(request.Year, request.Half)),
            total, HttpContext.RequestAborted);
        return Accepted(job);
    }

    public sealed class GenerateGraveInvoicesRequest
    {
        public int Year { get; set; }
        public int Half { get; set; }
    }

    public sealed class CreateInvoiceRequest
    {
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }

    public sealed class CreateRevisionRequest
    {
        public long InvoiceId { get; set; }
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }
}
