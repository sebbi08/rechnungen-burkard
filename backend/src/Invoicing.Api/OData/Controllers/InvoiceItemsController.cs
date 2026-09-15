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

        item.VatRate = VatRateNormalizer.Normalize(item.VatRate);

        var error = Validate(item);
        if (error is not null)
            return BadRequest(error);

        if (_db.InvoiceItems.Any(i => i.Number == item.Number && i.Id != item.Id))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "ItemNumberExists",
                        message = "This item number already exists.",
                    },
                }
            );
        }

        _db.InvoiceItems.Add(item);
        _db.SaveChanges();
        return Created(item);
    }

    public IActionResult Patch(
        [FromRoute] long key,
        [FromBody] Microsoft.AspNetCore.OData.Deltas.Delta<InvoiceItem>? delta
    )
    {
        if (delta is null)
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

        var item = _db.InvoiceItems.Find(key);
        if (item is null)
            return NotFound();

        var previousRate = item.VatRate;
        var previousDefault = item.DefaultPrice;
        delta.Patch(item);
        item.VatRate = VatRateNormalizer.Normalize(item.VatRate);

        var error = Validate(item);
        if (error is not null)
            return BadRequest(error);

        if (_db.InvoiceItems.Any(i => i.Number == item.Number && i.Id != item.Id))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "ItemNumberExists",
                        message = "This item number already exists.",
                    },
                }
            );
        }

        // The rate is read from the item at generation and never copied onto a
        // link, so changing it would move every linked grave's split at once.
        // Fixed, therefore, once a grave bills the item.
        var linked = _db.GraveItems.Any(l => l.ItemId == item.Id);
        if (linked && item.VatRate != previousRate)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "VatRateImmutable",
                        message = "The VAT rate of an item that graves bill cannot be changed.",
                    },
                }
            );
        }

        // A changed default applies to graves linked from now on. A grave that
        // was billing the old default keeps it, written onto the link as its
        // own price, so nothing already set up changes what it bills. A link
        // that already carries the old default as its own price needs nothing.
        if (previousDefault is { } oldDefault && item.DefaultPrice != oldDefault)
        {
            var onDefault = _db.GraveItems.Where(l => l.ItemId == item.Id && l.UnitPrice == null);
            foreach (var link in onDefault)
            {
                link.UnitPrice = oldDefault;
            }
        }

        _db.SaveChanges();
        return Updated(item);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var item = _db.InvoiceItems.Find(key);
        if (item is null)
            return NotFound();
        _db.InvoiceItems.Remove(item);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Restrict FK: the item is still linked to a grave
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "ItemInUse",
                        message = "This item is linked to a grave and cannot be deleted.",
                    },
                }
            );
        }

        return NoContent();
    }

    private object? Validate(InvoiceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Number))
        {
            return new
            {
                error = new
                {
                    code = "ItemNumberRequired",
                    message = "The item number is required.",
                },
            };
        }

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            return new
            {
                error = new
                {
                    code = "ItemDescriptionRequired",
                    message = "The description is required.",
                },
            };
        }

        if (!_db.VatRates.Any(v => v.Rate == item.VatRate))
        {
            return new
            {
                error = new
                {
                    code = "VatRateNotConfigured",
                    message = "The VAT rate is not configured.",
                },
            };
        }

        // prices are cents: a third decimal would be judged rounded by the
        // grave split check and billed unrounded by generation
        if (item.DefaultPrice is { } price && !GraveBilling.IsWholeCents(price))
        {
            return new
            {
                error = new
                {
                    code = "InvalidPrice",
                    message = "The price has more than two decimals.",
                },
            };
        }

        return null;
    }
}
