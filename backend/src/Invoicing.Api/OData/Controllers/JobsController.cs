using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class JobsController : ODataController
{
    private readonly AppDbContext _db;

    public JobsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Job> Get() => _db.Jobs.AsNoTracking();

    public ActionResult<Job> Get([FromRoute] long key)
    {
        var job = _db.Jobs.Find(key);
        return job is null ? NotFound() : Ok(job);
    }
}
