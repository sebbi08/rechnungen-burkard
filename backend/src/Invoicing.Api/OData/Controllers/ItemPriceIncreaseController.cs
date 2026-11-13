using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Raising an item's prices by a percentage: what the increase would
/// need, checked against the server before the item dialog saves it.</summary>
[Route("odata")]
public class ItemPriceIncreaseController : ODataController
{
    private readonly ItemPriceIncreaseService _service;

    public ItemPriceIncreaseController(ItemPriceIncreaseService service) => _service = service;

    [HttpPost("PreviewItemPriceIncrease")]
    public async Task<IActionResult> Preview([FromBody] ItemPriceIncreaseRequest? request)
    {
        try
        {
            var preview = await _service.PreviewAsync(request, HttpContext.RequestAborted);
            return new JsonResult(
                new
                {
                    Rows = preview.RowProblems.Select(p => new { Problems = p }).ToList(),
                    preview.Corrections,
                },
                ODataJsonDefaults.PascalCase
            );
        }
        catch (ItemNotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = ex.Code,
                        message = ex.Message,
                        args = ex.Args,
                    },
                }
            );
        }
    }

    [HttpPost("ApplyItemPriceIncrease")]
    public async Task<IActionResult> Apply([FromBody] ItemPriceIncreaseRequest? request)
    {
        try
        {
            await _service.ApplyAsync(request, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (ItemNotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = ex.Code,
                        message = ex.Message,
                        args = ex.Args,
                    },
                }
            );
        }
    }
}
