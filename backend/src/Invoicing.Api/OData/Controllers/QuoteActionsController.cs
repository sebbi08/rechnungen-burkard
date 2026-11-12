using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Microsoft.AspNetCore.Mvc.Route("odata")]
public class QuoteActionsController : ODataController
{
    private readonly QuoteService _service;
    private readonly ILogger<QuoteActionsController> _logger;

    public QuoteActionsController(QuoteService service, ILogger<QuoteActionsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("CreateQuote")]
    public async Task<IActionResult> CreateQuote([FromBody] CreateQuoteRequest? request)
    {
        if (request is null)
        {
            return MissingBody();
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return NoLines();
        }

        try
        {
            var quote = await _service.CreateAsync(
                request.RecipientId,
                request.Lines,
                HttpContext.RequestAborted
            );
            return Created($"/odata/Quotes({quote.Id})", quote);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            // escaping, the exception would be rethrown on the test host's client
            // instead of answered with a 500, unlike production
            _logger.LogError(ex, "CreateQuote failed");
            return InternalError();
        }
    }

    [HttpPost("CreateQuoteRevision")]
    public async Task<IActionResult> CreateQuoteRevision(
        [FromBody] CreateQuoteRevisionRequest? request
    )
    {
        if (request is null)
        {
            return MissingBody();
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return NoLines();
        }

        try
        {
            var quote = await _service.CreateRevisionAsync(
                request.QuoteId,
                request.RecipientId,
                request.Lines,
                HttpContext.RequestAborted
            );
            return Created($"/odata/Quotes({quote.Id})", quote);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateQuoteRevision failed");
            return InternalError();
        }
    }

    private BadRequestObjectResult MissingBody() =>
        BadRequest(
            new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." },
            }
        );

    private BadRequestObjectResult NoLines() =>
        BadRequest(
            new { error = new { code = "NoLines", message = "At least one line is required." } }
        );

    private ObjectResult InternalError() =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            new { error = new { code = "InternalError", message = "An internal error occurred." } }
        );

    public sealed class CreateQuoteRequest
    {
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }

    public sealed class CreateQuoteRevisionRequest
    {
        public long QuoteId { get; set; }
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }
}
