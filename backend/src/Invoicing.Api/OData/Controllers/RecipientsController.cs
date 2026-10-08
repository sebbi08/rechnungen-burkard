using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
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
    private readonly RecipientNumberService _numbers;

    public RecipientsController(AppDbContext db, RecipientNumberService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    [EnableQuery]
    public IQueryable<Recipient> Get() => _db.Recipients.AsNoTracking();

    public ActionResult<Recipient> Get([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        return recipient is null ? NotFound() : Ok(recipient);
    }

    public IActionResult Post([FromBody] Recipient? recipient)
    {
        if (recipient is null)
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

        recipient.Id = 0; // see IdReadOnly
        var error = Validate(recipient);
        if (error is not null)
            return BadRequest(error);

        // the recipient number identifies the row in the import mapping file; a
        // client that does not care (the quick-create dialog, the UI form) gets
        // the next free one assigned
        var assignNumber = string.IsNullOrWhiteSpace(recipient.Number);
        if (!assignNumber && _db.Recipients.Any(r => r.Number == recipient.Number))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "RecipientNumberExists",
                        message = "This recipient number already exists.",
                    },
                }
            );
        }

        // SQLite opens the transaction with BEGIN IMMEDIATE, so it holds the
        // write lock from before the next free number is read until the row is
        // in: two creates at once take turns rather than read the same number.
        // (They used to race and retry, and eight at once could run out of
        // retries.)
        using var tx = _db.Database.BeginTransaction();
        if (assignNumber)
        {
            recipient.Number = _numbers.Next();
        }

        try
        {
            _db.Recipients.Add(recipient);
            _db.SaveChanges();
            tx.Commit();
            return Created(recipient);
        }
        catch (DbUpdateException)
        {
            // an explicit number another create took after the check above
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "RecipientNumberExists",
                        message = "This recipient number already exists.",
                    },
                }
            );
        }
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Delta<Recipient>? delta)
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

        var recipient = _db.Recipients.Find(key);
        if (recipient is null)
            return NotFound();

        delta.Patch(recipient);
        var error = Validate(recipient);
        if (error is not null)
            return BadRequest(error);

        if (string.IsNullOrWhiteSpace(recipient.Number))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "RecipientNumberRequired",
                        message = "The recipient number is required.",
                    },
                }
            );
        }

        if (_db.Recipients.Any(r => r.Number == recipient.Number && r.Id != recipient.Id))
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "RecipientNumberExists",
                        message = "This recipient number already exists.",
                    },
                }
            );
        }

        _db.SaveChanges();
        return Updated(recipient);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        if (recipient is null)
            return NotFound();
        _db.Recipients.Remove(recipient);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Restrict FK: the recipient is still linked to a grave
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "RecipientInUse",
                        message = "This recipient is linked to a grave and cannot be deleted.",
                    },
                }
            );
        }

        return NoContent();
    }

    private static object? Validate(Recipient r)
    {
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            return new
            {
                error = new { code = "RecipientNameRequired", message = "The name is required." },
            };
        }

        if (string.IsNullOrWhiteSpace(r.Address))
        {
            return new
            {
                error = new
                {
                    code = "RecipientAddressRequired",
                    message = "The address is required.",
                },
            };
        }

        return null;
    }
}
