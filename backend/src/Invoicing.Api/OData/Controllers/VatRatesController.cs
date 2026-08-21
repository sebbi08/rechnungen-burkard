using Burkard.Invoicing.Api.Persistence;
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

    public IActionResult Post([FromBody] VatRate rate)
    {
        if (rate.Rate <= 0)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidVatRate", message = "The VAT rate must be positive." }
            });
        }

        if (_db.VatRates.Any(v => v.Rate == rate.Rate))
        {
            return BadRequest(new
            {
                error = new { code = "VatRateExists", message = "This VAT rate is already configured." }
            });
        }

        _db.VatRates.Add(rate);
        _db.SaveChanges();
        return Created(rate);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var rate = _db.VatRates.Find(key);
        if (rate is null) return NotFound();
        _db.VatRates.Remove(rate);
        _db.SaveChanges();
        return NoContent();
    }
}
