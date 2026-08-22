using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class InvoiceItemsController : ODataController
{
    private readonly AppDbContext _db;

    public InvoiceItemsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<InvoiceItem> Get() => _db.InvoiceItems.AsNoTracking();

    public ActionResult<InvoiceItem> Get([FromRoute] long key)
    {
        var item = _db.InvoiceItems.Find(key);
        return item is null ? NotFound() : Ok(item);
    }

    public IActionResult Post([FromBody] InvoiceItem? item)
    {
        if (item is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        item.VatRate = VatRateNormalizer.Normalize(item.VatRate);

        var error = Validate(item);
        if (error is not null) return BadRequest(error);

        if (_db.InvoiceItems.Any(i => i.Number == item.Number && i.Id != item.Id))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNumberExists", message = "This item number already exists." }
            });
        }

        _db.InvoiceItems.Add(item);
        _db.SaveChanges();
        return Created(item);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Microsoft.AspNetCore.OData.Deltas.Delta<InvoiceItem>? delta)
    {
        if (delta is null)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidRequest", message = "The request body is required." }
            });
        }

        var item = _db.InvoiceItems.Find(key);
        if (item is null) return NotFound();

        delta.Patch(item);
        item.VatRate = VatRateNormalizer.Normalize(item.VatRate);

        var error = Validate(item);
        if (error is not null) return BadRequest(error);

        if (_db.InvoiceItems.Any(i => i.Number == item.Number && i.Id != item.Id))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNumberExists", message = "This item number already exists." }
            });
        }

        _db.SaveChanges();
        return Updated(item);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var item = _db.InvoiceItems.Find(key);
        if (item is null) return NotFound();
        _db.InvoiceItems.Remove(item);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Restrict FK: the item is still linked to a grave
            return BadRequest(new
            {
                error = new { code = "ItemInUse", message = "This item is linked to a grave and cannot be deleted." }
            });
        }

        return NoContent();
    }

    private object? Validate(InvoiceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Number))
        {
            return new { error = new { code = "ItemNumberRequired", message = "The item number is required." } };
        }

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            return new { error = new { code = "ItemDescriptionRequired", message = "The description is required." } };
        }

        if (!_db.VatRates.Any(v => v.Rate == item.VatRate))
        {
            return new { error = new { code = "VatRateNotConfigured", message = "The VAT rate is not configured." } };
        }

        return null;
    }
}
