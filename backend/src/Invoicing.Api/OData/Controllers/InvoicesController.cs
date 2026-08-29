using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
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

    /// <summary>The invoice set, optionally narrowed by the 'search' custom query
    /// option. Returning an IQueryable keeps $filter, $orderby and paging working
    /// on top of the narrowed set. The term is read from the query string rather
    /// than taken as a parameter so the OData routing convention still recognises
    /// this as the entity set's Get.</summary>
    [EnableQuery]
    public IQueryable<Invoice> Get()
    {
        var term = Request.Query["search"].ToString();
        if (string.IsNullOrWhiteSpace(term))
        {
            return _db.Invoices.AsNoTracking();
        }

        var candidates = _db.Invoices.AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.RecipientName,
                i.RecipientAddress,
                i.GeneratedAt,
                Lines = i.Lines.Select(l => new { l.Count, l.UnitPrice, l.VatRate }).ToList(),
            })
            .ToList();

        var matched = candidates
            .Where(c => InvoiceSearch.Matches(
                new InvoiceSearch.Row(c.Id, c.InvoiceNumber, c.RecipientName, c.RecipientAddress, c.GeneratedAt,
                    InvoiceSearch.GrossTotal(c.Lines.Select(l => (l.Count, l.UnitPrice, l.VatRate)))),
                term))
            .Select(c => c.Id)
            .ToHashSet();

        return _db.Invoices.AsNoTracking().Where(i => matched.Contains(i.Id));
    }

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
