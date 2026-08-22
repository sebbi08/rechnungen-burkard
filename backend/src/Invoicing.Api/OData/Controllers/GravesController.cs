using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GravesController : ODataController
{
    private readonly AppDbContext _db;

    public GravesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Grave> Get() => _db.Graves.AsNoTracking();

    // The OData single-result $expand validator rejects collection navigations
    // ("The property 'Items' cannot be used in the $expand query option"), so
    // build the expected payload explicitly: PascalCase, same shape the EDM
    // serializer would produce for $expand=Items($expand=Item),
    // Recipients($expand=Recipient).
    private static readonly JsonSerializerOptions PascalCaseJson = new()
    {
        PropertyNamingPolicy = null,
    };

    public IActionResult Get([FromRoute] long key)
    {
        var grave = _db.Graves
            .Include(g => g.Items)
                .ThenInclude(i => i.Item)
            .Include(g => g.Recipients)
                .ThenInclude(r => r.Recipient)
            .AsNoTracking()
            .FirstOrDefault(g => g.Id == key);
        if (grave is null) return NotFound();

        var result = new
        {
            grave.Id,
            grave.GraveNumber,
            grave.StartDate,
            grave.EndDate,
            Items = grave.Items.Select(i => new
            {
                i.Id,
                i.Count,
                Item = new { i.Item.Id, i.Item.Number, i.Item.Description, i.Item.DefaultPrice, i.Item.VatRate },
            }).ToList(),
            Recipients = grave.Recipients.Select(r => new
            {
                r.Id,
                Recipient = new { r.Recipient.Id, r.Recipient.Name, r.Recipient.Address },
            }).ToList(),
        };

        return new JsonResult(result, PascalCaseJson);
    }

    public IActionResult Post([FromBody] Grave? grave)
    {
        if (grave is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        if (string.IsNullOrWhiteSpace(grave.GraveNumber))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNumberRequired", message = "The grave number is required." }
            });
        }

        grave.CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.Graves.Add(grave);
        _db.SaveChanges();
        return Created(grave);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Microsoft.AspNetCore.OData.Deltas.Delta<Grave>? delta)
    {
        if (delta is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        var grave = _db.Graves.Find(key);
        if (grave is null) return NotFound();
        delta.Patch(grave);
        if (string.IsNullOrWhiteSpace(grave.GraveNumber))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNumberRequired", message = "The grave number is required." }
            });
        }

        _db.SaveChanges();
        return Updated(grave);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var grave = _db.Graves.Find(key);
        if (grave is null) return NotFound();
        _db.Graves.Remove(grave); // cascades to GraveItems/GraveRecipients
        _db.SaveChanges();
        return NoContent();
    }

    [ODataAttributeRouting]
    [HttpGet("odata/SearchGraves")]
    public async Task<IActionResult> Search([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Ok(new { value = Array.Empty<object>() });
        }

        var graves = await _db.Graves
            .Select(g => new
            {
                g.Id,
                g.GraveNumber,
                RecipientNames = g.Recipients.Select(r => r.Recipient.Name).ToList(),
            })
            .ToListAsync();

        // Match in memory: SQL-side lower() folds ASCII only and would miss
        // non-ASCII case differences (e.g. 'ö' vs 'Ö'), which matters for German names.
        var matches = graves
            .Where(g => g.GraveNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || g.RecipientNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return Ok(new { value = matches });
    }
}
