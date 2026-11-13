using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Documents;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.Net.Http.Headers;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>The care list of a year and the planting list of a quarter, as PDFs
/// made on request. The lists are not in the EDM: they are plain file routes.</summary>
[Route("odata")]
public class WorkListController : ODataController
{
    private readonly WorkListService _service;

    public WorkListController(WorkListService service) => _service = service;

    [HttpGet("CareList")]
    public async Task<IActionResult> CareList(
        [FromQuery] string? year,
        [FromQuery] string? services
    )
    {
        try
        {
            var y = ParseYear(year);
            var variant = ParseServices(services);
            var data = await _service.CareListAsync(y, variant);
            if (data is null)
            {
                return Refuse(new NoGravesOnListException(y.ToString()));
            }

            return Pdf(data, $"Pflegeliste-{y}.pdf");
        }
        catch (ValidationException ex)
        {
            return Refuse(ex);
        }
    }

    [HttpGet("PlantingList")]
    public async Task<IActionResult> PlantingList(
        [FromQuery] string? year,
        [FromQuery] string? quarter
    )
    {
        try
        {
            var y = ParseYear(year);
            var q = ParseQuarter(quarter);
            var data = await _service.PlantingListAsync(y, q);
            if (data is null)
            {
                return Refuse(
                    new NoGravesOnListException(y.ToString(), DocumentTexts.QuarterName(q))
                );
            }

            return Pdf(data, $"Pflanzliste-{y}-{DocumentTexts.QuarterFileWord(q)}.pdf");
        }
        catch (ValidationException ex)
        {
            return Refuse(ex);
        }
    }

    private IActionResult Pdf(WorkListData data, string fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(WorkListDocuments.Generate(data), "application/pdf");
    }

    private IActionResult Refuse(ValidationException ex) =>
        BadRequest(
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

    private static ValidationException Invalid(string message) => new("ValidationFailed", message);

    private static int ParseYear(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, null, out var year)
        && year is >= 2000 and <= 2100
            ? year
            : throw Invalid("The parameter 'year' must be a year from 2000 to 2100.");

    private static CareListServices ParseServices(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "none" => CareListServices.None,
            "care" => CareListServices.Care,
            "all" => CareListServices.All,
            _ => throw Invalid("The parameter 'services' must be one of none, care, all."),
        };

    private static BillingQuarter ParseQuarter(string? value) =>
        // a name only: Enum.TryParse would also take "1" and combine "Spring,Summer"
        value?.ToLowerInvariant() switch
        {
            "spring" => BillingQuarter.Spring,
            "summer" => BillingQuarter.Summer,
            "autumn" => BillingQuarter.Autumn,
            "winter" => BillingQuarter.Winter,
            _ => throw Invalid(
                "The parameter 'quarter' must be one of Spring, Summer, Autumn, Winter."
            ),
        };
}
