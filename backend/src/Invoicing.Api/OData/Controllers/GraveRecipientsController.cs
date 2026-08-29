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

    /// <summary>Once a grave has been invoiced, its recipient set is frozen: the
    /// even split across them is baked into the invoices already generated. Links
    /// on a grave that was never invoiced stay writable.</summary>
    private bool WasInvoiced(long graveId) => _db.GraveInvoiceRuns.Any(r => r.GraveId == graveId);

    private static ObjectResult Immutable() => new(new
    {
        error = new { code = "GraveRecipientsImmutable", message = "A grave's recipients cannot be changed." }
    })
    { StatusCode = StatusCodes.Status400BadRequest };

    public IActionResult Post([FromBody] GraveRecipient? link)
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

        if (!_db.Recipients.Any(r => r.Id == link.RecipientId))
        {
            return BadRequest(new
            {
                error = new { code = "RecipientNotFound", message = "The recipient does not exist." }
            });
        }

        if (_db.GraveRecipients.Any(l => l.GraveId == link.GraveId && l.RecipientId == link.RecipientId))
        {
            return BadRequest(new
            {
                error = new { code = "GraveRecipientLinkExists", message = "This recipient is already linked to the grave." }
            });
        }

        if (WasInvoiced(link.GraveId))
        {
            return Immutable();
        }

        _db.GraveRecipients.Add(link);
        _db.SaveChanges();
        return Created(link);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var link = _db.GraveRecipients.Find(key);
        if (link is null) return NotFound();
        if (WasInvoiced(link.GraveId))
        {
            return Immutable();
        }

        _db.GraveRecipients.Remove(link);
        _db.SaveChanges();
        return NoContent();
    }
}
