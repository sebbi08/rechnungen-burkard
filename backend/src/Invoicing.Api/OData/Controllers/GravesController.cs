using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GravesController : ODataController
{
    private readonly AppDbContext _db;
    private readonly GraveService _graves;
    private readonly ILogger<GravesController> _logger;

    public GravesController(AppDbContext db, GraveService graves, ILogger<GravesController> logger)
    {
        _db = db;
        _graves = graves;
        _logger = logger;
    }

    [EnableQuery]
    public IQueryable<Grave> Get() => _db.Graves.AsNoTracking();

    // The OData single-result $expand validator rejects collection navigations
    // ("The property 'Items' cannot be used in the $expand query option"), so
    // build the expected payload explicitly: same shape the EDM serializer
    // would produce for $expand=Items($expand=Item),Recipients($expand=Recipient).
    public IActionResult Get([FromRoute] long key)
    {
        var grave = _db
            .Graves.Include(g => g.Items)
                .ThenInclude(i => i.Item)
            .Include(g => g.Recipients)
                .ThenInclude(r => r.Recipient)
            .AsNoTracking()
            .FirstOrDefault(g => g.Id == key);
        if (grave is null)
            return NotFound();

        var result = new
        {
            grave.Id,
            grave.GraveNumber,
            grave.StartDate,
            grave.EndDate,
            grave.CreatedAt,
            Items = grave
                .Items.Select(i => new
                {
                    i.Id,
                    i.Count,
                    i.UnitPrice,
                    i.PriceIncludesVat,
                    Item = new
                    {
                        i.Item.Id,
                        i.Item.Number,
                        i.Item.Description,
                        i.Item.DefaultPrice,
                        i.Item.VatRate,
                    },
                })
                .ToList(),
            Recipients = grave
                .Recipients.Select(r => new
                {
                    r.Id,
                    Recipient = new
                    {
                        r.Recipient.Id,
                        r.Recipient.Name,
                        r.Recipient.Address,
                    },
                })
                .ToList(),
        };

        return new JsonResult(result, ODataJsonDefaults.PascalCase);
    }

    // Seeding only, like the link collections: the body is the whole graph, so
    // nested Items and Recipients are written along with the grave without any
    // of SaveGrave's checks.
    [SeedingOnly]
    public IActionResult Post([FromBody] Grave? grave)
    {
        if (grave is null)
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

        if (string.IsNullOrWhiteSpace(grave.GraveNumber))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "GraveNumberRequired",
                        message = "The grave number is required.",
                    },
                }
            );
        }

        grave.CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.Graves.Add(grave);
        _db.SaveChanges();
        return Created(grave);
    }

    // No PATCH and no DELETE on the set. The dialog changes a grave through
    // SaveGrave, which is where the checks live; a Delta with a nested Items
    // or Recipients set would write links past every one of them, even on an
    // invoiced grave. Deleting a grave would leave its invoices and runs
    // pointing at nothing — those hold the id without a foreign key.

    [ODataAttributeRouting]
    [HttpGet("odata/SearchGraves")]
    public async Task<IActionResult> Search([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return new JsonResult(
                new { value = Array.Empty<object>() },
                ODataJsonDefaults.PascalCase
            );
        }

        var graves = await _db
            .Graves.Select(g => new
            {
                g.Id,
                g.GraveNumber,
                RecipientNames = g.Recipients.Select(r => r.Recipient.Name).ToList(),
            })
            .ToListAsync();

        // Match in memory: SQL-side lower() folds ASCII only and would miss
        // non-ASCII case differences (e.g. 'ö' vs 'Ö'), which matters for German names.
        var matches = graves
            .Where(g =>
                g.GraveNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.RecipientNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase))
            )
            .ToList();

        return new JsonResult(new { value = matches }, ODataJsonDefaults.PascalCase);
    }

    // The grave dialog collects the grave, its items and its recipients before
    // anything is saved, so the whole set arrives as one request.
    [ODataAttributeRouting]
    [HttpPost("odata/SaveGrave")]
    public async Task<IActionResult> Save([FromBody] SaveGraveRequest? request)
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

        if (
            !DateOnly.TryParse(request.StartDate, out var startDate)
            || !DateOnly.TryParse(request.EndDate, out var endDate)
        )
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidDate",
                        message = "The start and end dates are required.",
                    },
                }
            );
        }

        try
        {
            var grave = await _graves.SaveAsync(
                request.GraveId,
                request.GraveNumber ?? "",
                startDate,
                endDate,
                request.Items ?? new List<GraveItemInput>(),
                request.RecipientIds ?? new List<long>(),
                HttpContext.RequestAborted
            );
            // PascalCase like every other entity payload; MVC's default camelCase
            // would make this the odd one out
            return new JsonResult(new { grave.Id }, ODataJsonDefaults.PascalCase);
        }
        // the split problems travel with the refusal: the dialog names the rate,
        // the amounts and what would work instead, which it cannot work out for
        // itself without a second copy of the rule
        catch (GraveShareValidationException ex)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = ex.Code,
                        message = ex.Message,
                        recipients = ex.Recipients,
                        problems = ex
                            .Problems.Select(p => new
                            {
                                vatRate = p.VatRate,
                                kind = p.Kind.ToString(),
                                amount = p.Amount / 100m,
                                suggestions = p.Suggestions.Select(c => c / 100m).ToList(),
                            })
                            .ToList(),
                    },
                }
            );
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
        catch (Exception ex)
        {
            // Letting the exception escape would cause the test host to rethrow it on
            // the client instead of producing a 500 response; mirror production behavior.
            _logger.LogError(ex, "SaveGrave failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }

    public sealed class SaveGraveRequest
    {
        public long? GraveId { get; set; }
        public string? GraveNumber { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public List<GraveItemInput>? Items { get; set; }
        public List<long>? RecipientIds { get; set; }
    }
}
