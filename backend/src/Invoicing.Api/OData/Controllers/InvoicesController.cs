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
    /// calculation needs — each invoice is stamped with its three totals, and
    /// what is returned is an in-memory queryable rather than one EF can still
    /// translate. [EnableQuery] runs afterwards exactly as before: $filter,
    /// $orderby, $expand and paging still apply, now through LINQ to Objects
    /// instead of SQL, so every query option the frontend actually uses against
    /// stored columns keeps working. The trade-off is that a request now loads
    /// every invoice's lines rather than only the page's, which the search
    /// branch below already did for a subset of columns; it is accepted here too
    /// rather than hand-rolling $filter/$orderby application to keep only the
    /// matching page in memory.</summary>
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

        var invoices = query.Include(i => i.Lines).AsEnumerable().ToList();
        AttachDocuments(invoices);
        return invoices.Select(WithTotals).AsQueryable();
    }

    /// <summary>Fills each invoice's Documents from a lightweight projection
    /// rather than an EF Include. Document.Content is the full generated PDF —
    /// EdmModelBuilder ignores it so a $expand=Documents response never carries
    /// it, but that only keeps it out of the JSON; an Include still reads the
    /// BLOB column out of SQLite for every document of every invoice on the
    /// page. Selecting only the columns $expand=Documents actually serialises
    /// (Id, InvoiceId, Kind, FileName, GeneratedAt) and building bare Document
    /// instances from them keeps Content out of the query entirely, while
    /// leaving what a caller can observe unchanged. Do not replace this with
    /// `.Include(i => i.Documents)` — that reintroduces the BLOB read.</summary>
    private void AttachDocuments(List<Invoice> invoices)
    {
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var documentsByInvoice = _db
            .Documents.AsNoTracking()
            .Where(d => invoiceIds.Contains(d.InvoiceId))
            .Select(d => new
            {
                d.Id,
                d.InvoiceId,
                d.Kind,
                d.FileName,
                d.GeneratedAt,
            })
            .ToList()
            .ToLookup(d => d.InvoiceId);

        foreach (var invoice in invoices)
        {
            invoice.Documents = documentsByInvoice[invoice.Id]
                .Select(d => new Document
                {
                    Id = d.Id,
                    InvoiceId = d.InvoiceId,
                    Kind = d.Kind,
                    FileName = d.FileName,
                    GeneratedAt = d.GeneratedAt,
                })
                .ToList();
        }
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
