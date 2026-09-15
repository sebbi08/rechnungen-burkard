using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Read-only: a grave's links are written with the grave, through
/// SaveGrave, which is where the checks live. A link written on its own would
/// put a grave together past the check that its prices divide across its
/// recipients.</summary>
public class GraveItemsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveItemsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveItem> Get() => _db.GraveItems.AsNoTracking();
}
