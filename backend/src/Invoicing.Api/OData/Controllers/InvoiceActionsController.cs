using System.Text.Json;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Microsoft.AspNetCore.Mvc.Route("odata")]
public class InvoiceActionsController : ODataController
{
    private readonly InvoiceService _service;
    private readonly GraveInvoiceRevisionService _graveRevisions;
    private readonly JobService _jobs;
    private readonly AppDbContext _db;
    private readonly GraveCorrectionCheck _corrections;
    private readonly IClock _clock;
    private readonly ILogger<InvoiceActionsController> _logger;

    public InvoiceActionsController(
        InvoiceService service,
        GraveInvoiceRevisionService graveRevisions,
        JobService jobs,
        AppDbContext db,
        GraveCorrectionCheck corrections,
        IClock clock,
        ILogger<InvoiceActionsController> logger
    )
    {
        _service = service;
        _graveRevisions = graveRevisions;
        _jobs = jobs;
        _db = db;
        _corrections = corrections;
        _clock = clock;
        _logger = logger;
    }

    [HttpPost("CreateInvoice")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest? request)
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

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return BadRequest(
                new { error = new { code = "NoLines", message = "At least one line is required." } }
            );
        }

        try
        {
            var invoice = await _service.CreateAsync(
                request.RecipientId,
                request.Lines,
                HttpContext.RequestAborted
            );
            return Created($"/odata/Invoices({invoice.Id})", invoice);
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
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    [HttpPost("CreateRevision")]
    public async Task<IActionResult> CreateRevision([FromBody] CreateRevisionRequest? request)
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

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return BadRequest(
                new { error = new { code = "NoLines", message = "At least one line is required." } }
            );
        }

        try
        {
            var invoice = await _service.CreateRevisionAsync(
                request.InvoiceId,
                request.RecipientId,
                request.Lines,
                HttpContext.RequestAborted
            );
            return Created($"/odata/Invoices({invoice.Id})", invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRevision failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    /// <summary>The invoices of a deleted grave are frozen: they are no longer
    /// listed, so neither a payment nor a revision may change them. Restoring the
    /// grave lifts this.</summary>
    private async Task<IActionResult?> RefuseIfGraveDeletedAsync(long invoiceId)
    {
        var deleted = await _db.Invoices.AnyAsync(
            i =>
                i.Id == invoiceId
                && i.GraveId != null
                && _db.Graves.Any(g => g.Id == i.GraveId && g.DeletedAt != null),
            HttpContext.RequestAborted
        );
        return deleted
            ? BadRequest(
                new
                {
                    error = new { code = "GraveDeleted", message = "The grave has been deleted." },
                }
            )
            : null;
    }

    [HttpPost("CreateGraveInvoiceRevision")]
    public async Task<IActionResult> CreateGraveInvoiceRevision(
        [FromBody] CreateGraveInvoiceRevisionRequest? request
    )
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

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return BadRequest(
                new { error = new { code = "NoLines", message = "At least one line is required." } }
            );
        }

        if (await RefuseIfGraveDeletedAsync(request.InvoiceId) is { } refused)
        {
            return refused;
        }

        try
        {
            var revisions = await _graveRevisions.CreateAsync(
                request.InvoiceId,
                request.Lines,
                HttpContext.RequestAborted
            );
            return Ok(revisions);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateGraveInvoiceRevision failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    [HttpPost("SetInvoicePaid")]
    public async Task<IActionResult> SetInvoicePaid([FromBody] SetInvoicePaidRequest? request)
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

        if (await RefuseIfGraveDeletedAsync(request.InvoiceId) is { } refused)
        {
            return refused;
        }

        try
        {
            var invoice = await _service.SetPaidAsync(
                request.InvoiceId,
                request.Paid,
                HttpContext.RequestAborted
            );
            return Ok(invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetInvoicePaid failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    /// <summary>The grave list's "mark paid": the invoices the user ticked, all
    /// marked or none.</summary>
    [HttpPost("MarkInvoicesPaid")]
    public async Task<IActionResult> MarkInvoicesPaid([FromBody] MarkInvoicesPaidRequest? request)
    {
        if (request is null || request.InvoiceIds is null || request.InvoiceIds.Count == 0)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidRequest",
                        message = "At least one invoice is required.",
                    },
                }
            );
        }

        var ids = request.InvoiceIds;
        var onDeletedGrave = await _db.Invoices.AnyAsync(
            i =>
                ids.Contains(i.Id)
                && i.GraveId != null
                && _db.Graves.Any(g => g.Id == i.GraveId && g.DeletedAt != null),
            HttpContext.RequestAborted
        );
        if (onDeletedGrave)
        {
            return BadRequest(
                new
                {
                    error = new { code = "GraveDeleted", message = "The grave has been deleted." },
                }
            );
        }

        try
        {
            await _service.MarkPaidAsync(ids, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkInvoicesPaid failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    [HttpPost("GenerateGraveInvoices")]
    public async Task<IActionResult> GenerateGraveInvoices(
        [FromBody] GenerateGraveInvoicesRequest? request
    )
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

        if (request.Year < 2000 || request.Year > 2100 || request.Half is not (1 or 2))
        {
            return BadRequest(
                new
                {
                    error = new { code = "InvalidHalfYear", message = "The half-year is invalid." },
                }
            );
        }

        // an invoice is generated for the current half-year or one of the two
        // before it; anything older is a mistake that would bill a long-closed period
        var (earliestYear, earliestHalf) = HalfYearBilling.EarliestGeneratable(
            HalfYearBilling.HalfYearOf(_clock.GetLocalNow())
        );
        if (request.Year * 2 + request.Half < earliestYear * 2 + earliestHalf)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "HalfYearTooOld",
                        message = "Invoices can be generated at most two half-years back.",
                        args = new[] { $"H{earliestHalf} {earliestYear}" },
                    },
                }
            );
        }

        var outdated = await _corrections.OutdatedGraveNumbersAsync(
            request.Year,
            request.Half,
            HttpContext.RequestAborted
        );
        if (outdated.Count > 0)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "CorrectionsOutdated",
                        message = "The rounding corrections of some graves have changed.",
                        args = new[] { string.Join(", ", outdated) },
                    },
                }
            );
        }

        // refused once every grave due in the half-year has its invoices; a
        // grave outside its term does not keep it open
        var (open, invoiced) = await GraveBilling.OpenForHalfYearAsync(
            _db,
            request.Year,
            request.Half,
            HttpContext.RequestAborted
        );
        if (open.Count == 0 && invoiced > 0)
        {
            return Conflict(
                new
                {
                    error = new
                    {
                        code = "GravesAlreadyInvoiced",
                        message = "Invoices for this half-year were already generated.",
                    },
                }
            );
        }

        if (_jobs.HasActive("GraveInvoices"))
        {
            return Conflict(
                new
                {
                    error = new
                    {
                        code = "JobAlreadyRunning",
                        message = "A generation is already running.",
                    },
                }
            );
        }

        var job = await _jobs.EnqueueAsync(
            "GraveInvoices",
            JsonSerializer.Serialize(new GraveInvoicesPayload(request.Year, request.Half)),
            open.Count,
            HttpContext.RequestAborted
        );
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

    public sealed class CreateGraveInvoiceRevisionRequest
    {
        public long InvoiceId { get; set; }

        /// <summary>Grave-level lines: the grave's count at the grave's price,
        /// before the even split across its recipients.</summary>
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }

    public sealed class SetInvoicePaidRequest
    {
        public long InvoiceId { get; set; }
        public bool Paid { get; set; }
    }

    public sealed class MarkInvoicesPaidRequest
    {
        public List<long>? InvoiceIds { get; set; }
    }
}
