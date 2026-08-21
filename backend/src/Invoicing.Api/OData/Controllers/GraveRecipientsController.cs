using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GraveRecipientsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveRecipientsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveRecipient> Get() => _db.GraveRecipients.AsNoTracking();

    public IActionResult Post([FromBody] GraveRecipient link)
    {
        if (!_db.Graves.Any(g => g.Id == link.GraveId))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNotFound", message = "The grave does not exist." }
            });
        }

        if (!_db.Recipients.Any(r => r.Id == link.RecipientId))
        {
            return BadRequest(new
            {
                error = new { code = "RecipientNotFound", message = "The recipient does not exist." }
            });
        }

        _db.GraveRecipients.Add(link);
        _db.SaveChanges();
        return Created(link);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var link = _db.GraveRecipients.Find(key);
        if (link is null) return NotFound();
        _db.GraveRecipients.Remove(link);
        _db.SaveChanges();
        return NoContent();
    }
}
