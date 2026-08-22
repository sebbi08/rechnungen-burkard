using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GraveItemsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveItemsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveItem> Get() => _db.GraveItems.AsNoTracking();

    public IActionResult Post([FromBody] GraveItem? link)
    {
        if (link is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        if (!_db.Graves.Any(g => g.Id == link.GraveId))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNotFound", message = "The grave does not exist." }
            });
        }

        if (!_db.InvoiceItems.Any(i => i.Id == link.ItemId))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNotFound", message = "The item does not exist." }
            });
        }

        if (link.Count <= 0)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidCount", message = "The count must be positive." }
            });
        }

        if (_db.GraveItems.Any(l => l.GraveId == link.GraveId && l.ItemId == link.ItemId))
        {
            return BadRequest(new
            {
                error = new { code = "GraveItemLinkExists", message = "This item is already linked to the grave." }
            });
        }

        _db.GraveItems.Add(link);
        _db.SaveChanges();
        return Created(link);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var link = _db.GraveItems.Find(key);
        if (link is null) return NotFound();
        _db.GraveItems.Remove(link);
        _db.SaveChanges();
        return NoContent();
    }
}
