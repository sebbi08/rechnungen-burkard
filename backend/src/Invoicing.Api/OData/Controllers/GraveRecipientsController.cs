using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Read-only: a grave's recipients are fixed once the grave has an
/// invoice and change only through SaveGrave, since the even split across them is baked into every invoice
/// generated for it. A link written on its own would change that set.</summary>
public class GraveRecipientsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveRecipientsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveRecipient> Get() => _db.GraveRecipients.AsNoTracking();
}
