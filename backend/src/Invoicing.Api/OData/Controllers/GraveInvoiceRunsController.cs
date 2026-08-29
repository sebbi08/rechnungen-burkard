using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>Read-only: runs are written by generation alone. The grave detail
/// page reads them to show which half-years a grave was invoiced for.</summary>
public class GraveInvoiceRunsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoiceRunsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveInvoiceRun> Get() => _db.GraveInvoiceRuns.AsNoTracking();
}
