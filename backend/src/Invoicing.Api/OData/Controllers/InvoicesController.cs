using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class InvoicesController : ODataController
{
    private readonly AppDbContext _db;

    public InvoicesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Invoice> Get() => _db.Invoices.AsNoTracking();

    public ActionResult<Invoice> Get([FromRoute] long key)
    {
        var invoice = _db.Invoices.Find(key);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [EnableQuery]
    public IQueryable<Document> GetDocuments(long key) =>
        _db.Documents.AsNoTracking().Where(d => d.InvoiceId == key);

    [EnableQuery]
    public IQueryable<InvoiceLine> GetLines(long key) =>
        _db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == key);
}
