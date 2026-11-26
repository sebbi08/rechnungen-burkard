using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>The Verlauf of graves and invoice chains, read by
/// <c>$filter=GraveId eq …</c> or <c>InvoiceRootId eq …</c>, newest first and
/// paged by the client. Read-only: entries are written by audit actions
/// alone.</summary>
public class AuditEntriesController : ODataController
{
    private readonly AppDbContext _db;

    public AuditEntriesController(AppDbContext db) => _db = db;

    [EnableQuery(MaxTop = 100)]
    public IQueryable<AuditEntry> Get() => _db.AuditEntries.AsNoTracking();
}
