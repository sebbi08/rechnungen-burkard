using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class RecipientsController : ODataController
{
    private readonly AppDbContext _db;

    public RecipientsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Recipient> Get() => _db.Recipients.AsNoTracking();

    public ActionResult<Recipient> Get([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        return recipient is null ? NotFound() : Ok(recipient);
    }

    public IActionResult Post([FromBody] Recipient recipient)
    {
        var error = Validate(recipient);
        if (error is not null) return BadRequest(error);

        _db.Recipients.Add(recipient);
        _db.SaveChanges();
        return Created(recipient);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Delta<Recipient> delta)
    {
        var recipient = _db.Recipients.Find(key);
        if (recipient is null) return NotFound();

        delta.Patch(recipient);
        var error = Validate(recipient);
        if (error is not null) return BadRequest(error);

        _db.SaveChanges();
        return Updated(recipient);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        if (recipient is null) return NotFound();
        _db.Recipients.Remove(recipient);
        _db.SaveChanges();
        return NoContent();
    }

    private static object? Validate(Recipient r)
    {
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            return new { error = new { code = "RecipientNameRequired", message = "The name is required." } };
        }

        if (string.IsNullOrWhiteSpace(r.Address))
        {
            return new { error = new { code = "RecipientAddressRequired", message = "The address is required." } };
        }

        return null;
    }
}
