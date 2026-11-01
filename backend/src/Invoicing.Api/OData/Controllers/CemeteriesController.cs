using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class CemeteriesController : ODataController
{
    private readonly AppDbContext _db;

    public CemeteriesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Cemetery> Get() => _db.Cemeteries.AsNoTracking();

    public ActionResult<Cemetery> Get([FromRoute] long key)
    {
        var cemetery = _db.Cemeteries.Find(key);
        return cemetery is null ? NotFound() : Ok(cemetery);
    }

    public IActionResult Post([FromBody] Cemetery? cemetery)
    {
        if (cemetery is null)
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

        cemetery.Id = 0; // see IdReadOnly
        cemetery.Name = (cemetery.Name ?? "").Trim();
        var error = Validate(cemetery);
        if (error is not null)
            return BadRequest(error);

        _db.Cemeteries.Add(cemetery);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // the unique index: the same name, created after the check above
            return BadRequest(NameExists());
        }

        return Created(cemetery);
    }

    // Graves refer to the cemetery rather than carry its name, so a new name
    // reaches every one of them at once.
    public IActionResult Patch([FromRoute] long key, [FromBody] Delta<Cemetery>? delta)
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

        if (IdReadOnly.Changes(delta))
            return BadRequest(IdReadOnly.Error());

        var cemetery = _db.Cemeteries.Find(key);
        if (cemetery is null)
            return NotFound();

        delta.Patch(cemetery);
        cemetery.Name = (cemetery.Name ?? "").Trim();
        var error = Validate(cemetery);
        if (error is not null)
            return BadRequest(error);

        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            return BadRequest(NameExists());
        }

        return Updated(cemetery);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var cemetery = _db.Cemeteries.Find(key);
        if (cemetery is null)
            return NotFound();
        if (_db.Graves.Any(g => g.CemeteryId == key))
            return BadRequest(InUse());

        _db.Cemeteries.Remove(cemetery);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Restrict FK: a grave was saved into it after the check above
            return BadRequest(InUse());
        }

        return NoContent();
    }

    private object? Validate(Cemetery cemetery)
    {
        if (cemetery.Name.Length == 0)
        {
            return new
            {
                error = new
                {
                    code = "CemeteryNameRequired",
                    message = "The cemetery name is required.",
                },
            };
        }

        // Compared in memory, like SearchGraves: SQLite's NOCASE and lower()
        // fold ASCII only, and "Südfriedhof" and "SÜDFRIEDHOF" are one name.
        var taken = _db
            .Cemeteries.Where(c => c.Id != cemetery.Id)
            .Select(c => c.Name)
            .AsEnumerable()
            .Any(name => string.Equals(name, cemetery.Name, StringComparison.OrdinalIgnoreCase));
        return taken ? NameExists() : null;
    }

    private static object NameExists() =>
        new
        {
            error = new
            {
                code = "CemeteryNameExists",
                message = "A cemetery with this name already exists.",
            },
        };

    private static object InUse() =>
        new
        {
            error = new
            {
                code = "CemeteryInUse",
                message = "Graves still lie in this cemetery; it cannot be deleted.",
            },
        };
}
