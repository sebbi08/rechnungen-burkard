using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Which graves bill an item, and at what price, for the item dialog.
///
/// Grouped by the price a grave is actually billed — its own link price, or the
/// item's default standing in for it. A link whose own price equals the default
/// counts as billing the default: the grave dialog copies the default into the field, so
/// most links carry it that way. Default groups come first, so the reader sees
/// what a changed default would have applied to.</summary>
[Route("odata")]
public class ItemGravesController : ODataController
{
    private readonly AppDbContext _db;

    public ItemGravesController(AppDbContext db) => _db = db;

    [HttpGet("ItemGraves")]
    public async Task<IActionResult> Get([FromQuery] long itemId)
    {
        var item = await _db
            .InvoiceItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId, HttpContext.RequestAborted);
        if (item is null)
            return NotFound();

        var links = await (
            from link in _db.GraveItems.AsNoTracking()
            join grave in _db.Graves.AsNoTracking() on link.GraveId equals grave.Id
            where link.ItemId == itemId
            select new
            {
                link.UnitPrice,
                grave.Id,
                grave.GraveNumber,
                grave.IsCooperative,
                IsDeleted = grave.DeletedAt != null,
            }
        ).ToListAsync(HttpContext.RequestAborted);

        // a deleted grave is not listed, but it still links the item: the item's
        // locks count every link, so Linked does too
        var groups = links
            .Where(l => !l.IsDeleted)
            .GroupBy(l => l.UnitPrice ?? item.DefaultPrice)
            .Select(g => new
            {
                UnitPrice = g.Key,
                IsDefault = g.Key == item.DefaultPrice,
                Graves = g.OrderBy(l => l.GraveNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(l => new
                    {
                        l.Id,
                        l.GraveNumber,
                        l.IsCooperative,
                    })
                    .ToList(),
            })
            .OrderByDescending(g => g.IsDefault)
            .ThenBy(g => g.UnitPrice)
            .ToList();

        return new JsonResult(
            new
            {
                Linked = links.Count > 0,
                item.DefaultPrice,
                Groups = groups,
            },
            ODataJsonDefaults.PascalCase
        );
    }
}
