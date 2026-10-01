using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class VatRatesController : ODataController
{
    private readonly AppDbContext _db;

    public VatRatesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<VatRate> Get() => _db.VatRates.AsNoTracking();

    public ActionResult<VatRate> Get([FromRoute] long key)
    {
        var rate = _db.VatRates.Find(key);
        return rate is null ? NotFound() : Ok(rate);
    }

    public IActionResult Post([FromBody] VatRate? rate)
    {
        if (rate is null)
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

        rate.Rate = VatRateNormalizer.Normalize(rate.Rate);

        // a percentage of the price: at 100 or more it is a typo, not a tax
        if (rate.Rate <= 0 || rate.Rate >= 100)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "InvalidVatRate",
                        message = "The VAT rate must be above 0 and below 100.",
                    },
                }
            );
        }

        if (_db.VatRates.Any(v => v.Rate == rate.Rate))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "VatRateExists",
                        message = "This VAT rate is already configured.",
                    },
                }
            );
        }

        _db.VatRates.Add(rate);
        _db.SaveChanges();
        return Created(rate);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var rate = _db.VatRates.Find(key);
        if (rate is null)
            return NotFound();
        // an item keeps its rate as a number; with the rate gone it could be
        // neither saved as it is (VatRateNotConfigured) nor, on a grave, changed
        // (VatRateImmutable). Invoice lines carry their own copy and are unaffected.
        if (_db.InvoiceItems.Any(i => i.VatRate == rate.Rate))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "VatRateInUse",
                        message = "Items still use this VAT rate.",
                    },
                }
            );
        }
        _db.VatRates.Remove(rate);
        _db.SaveChanges();
        return NoContent();
    }
}
