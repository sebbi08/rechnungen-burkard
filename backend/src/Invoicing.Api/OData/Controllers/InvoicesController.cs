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
    private readonly DocumentDataBuilder _documentData;

    public InvoicesController(AppDbContext db, DocumentDataBuilder documentData)
    {
        _db = db;
        _documentData = documentData;
    }

    /// <summary>The invoice set, optionally narrowed by the 'search' custom query
    /// option. The term is read from the query string rather than taken as a
    /// parameter so the OData routing convention still recognises this as the
    /// entity set's Get.
    ///
    /// NetTotal, VatTotal and GrossTotal are computed here rather than projected
    /// inside the IQueryable: DocumentDataBuilder groups lines by VAT rate,
    /// rounds each group to the cent and — for a grave invoice — divides by
    /// RecipientCount, none of which EF Core can translate to SQL. So, unlike the
    /// old version of this method, the narrowed set (the whole table, or the
    /// search's matching ids) is loaded into memory here — with the lines the
    /// calculation needs, and the documents the frontend's $expand asks for —
    /// each invoice is stamped with its three totals, and what is returned is an
    /// in-memory queryable rather than one EF can still translate. [EnableQuery]
    /// runs afterwards exactly as before: $filter, $orderby, $expand and paging
    /// still apply, now through LINQ to Objects instead of SQL, so every query
    /// option the frontend actually uses against stored columns keeps working.
    /// The trade-off is that a request now loads every invoice's lines and
    /// documents rather than only the page's, which the search branch below
    /// already did for a subset of columns; it is accepted here too rather than
    /// hand-rolling $filter/$orderby application to keep only the matching page
    /// in memory.</summary>
    [EnableQuery]
    public IQueryable<Invoice> Get()
    {
        var term = Request.Query["search"].ToString();
        IQueryable<Invoice> query;
        if (string.IsNullOrWhiteSpace(term))
        {
            query = _db.Invoices.AsNoTracking();
        }
        else
        {
            var candidates = _db
                .Invoices.AsNoTracking()
                .Select(i => new
                {
                    i.Id,
                    i.InvoiceNumber,
                    i.RecipientName,
                    i.RecipientAddress,
                    i.GeneratedAt,
                    Lines = i
                        .Lines.Select(l => new
                        {
                            l.Count,
                            l.UnitPrice,
                            l.VatRate,
                        })
                        .ToList(),
                })
                .ToList();

            var matched = candidates
                .Where(c =>
                    InvoiceSearch.Matches(
                        new InvoiceSearch.Row(
                            c.Id,
                            c.InvoiceNumber,
                            c.RecipientName,
                            c.RecipientAddress,
                            c.GeneratedAt,
                            InvoiceSearch.GrossTotal(
                                c.Lines.Select(l => (l.Count, l.UnitPrice, l.VatRate))
                            )
                        ),
                        term
                    )
                )
                .Select(c => c.Id)
                .ToHashSet();

            query = _db.Invoices.AsNoTracking().Where(i => matched.Contains(i.Id));
        }

        return query
            .Include(i => i.Lines)
            .Include(i => i.Documents)
            .AsEnumerable()
            .Select(WithTotals)
            .AsQueryable();
    }

    /// <summary>Stamps NetTotal, VatTotal and GrossTotal on an invoice already
    /// loaded with its lines: the recipient's share when it has one (a grave
    /// invoice), otherwise the invoice's own whole total.</summary>
    private Invoice WithTotals(Invoice invoice)
    {
        var data = _documentData.Build(invoice);
        invoice.NetTotal = data.Share?.NetTotal ?? data.NetTotal;
        invoice.VatTotal = data.Share?.VatTotal ?? data.TotalVat;
        invoice.GrossTotal = data.Share?.GrossTotal ?? data.GrossTotal;
        return invoice;
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
