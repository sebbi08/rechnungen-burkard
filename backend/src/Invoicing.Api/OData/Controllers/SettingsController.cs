using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class SettingsController : ODataController
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db) => _db = db;

    public ActionResult<GlobalSettings> Get()
    {
        var settings =
            _db.GlobalSettings.FirstOrDefault()
            ?? throw new InvalidOperationException("GlobalSettings not seeded.");
        return Ok(settings);
    }

    public IActionResult Patch([FromBody] Delta<GlobalSettings>? delta)
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

        var settings =
            _db.GlobalSettings.FirstOrDefault()
            ?? throw new InvalidOperationException("GlobalSettings not seeded.");

        if (
            delta.TryGetPropertyValue(nameof(GlobalSettings.StampWord), out var stamp)
            && string.IsNullOrWhiteSpace(stamp as string)
        )
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "StampWordRequired",
                        message = "The stamp word must not be empty.",
                    },
                }
            );
        }

        delta.Patch(settings);
        _db.SaveChanges();
        return Ok(settings);
    }
}
