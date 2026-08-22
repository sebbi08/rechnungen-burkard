using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Microsoft.AspNetCore.Mvc.Route("odata")]
public class InvoiceActionsController : ODataController
{
    private readonly InvoiceService _service;

    public InvoiceActionsController(InvoiceService service) => _service = service;

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
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = new { code = "InternalError", message = ex.Message } });
        }
    }

    public sealed class CreateInvoiceRequest
    {
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }
}
