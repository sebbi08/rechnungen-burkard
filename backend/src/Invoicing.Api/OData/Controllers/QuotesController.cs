using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

/// <summary>The quote set, shaped like <see cref="InvoicesController"/>. A quote
/// PDF is served by <see cref="QuoteDownloadController"/> by attribute route;
/// the document's bytes are never read here.</summary>
public class QuotesController : ODataController
{
    private readonly AppDbContext _db;
    private readonly DocumentDataBuilder _documentData;

    public QuotesController(AppDbContext db, DocumentDataBuilder documentData)
    {
        _db = db;
        _documentData = documentData;
    }

    /// <summary>Every quote version, narrowed by the column options 'number',
    /// 'recipient', 'date' and 'total' exactly as the invoice list is (the number
    /// slot holds the quote number), and stamped with NetTotal, VatTotal,
    /// GrossTotal and — once converted — the number of the invoice it became.
    /// The totals are computed in memory for the reason given on
    /// <see cref="InvoicesController.Get()"/>, so [EnableQuery] runs over an
    /// in-memory queryable. No Document is included: the PDF's BLOB stays in
    /// the database.</summary>
    [EnableQuery]
    public IQueryable<Quote> Get()
    {
        var filter = new InvoiceSearch.Filter(
            Request.Query["number"].ToString(),
            Request.Query["recipient"].ToString(),
            Request.Query["date"].ToString(),
            Request.Query["total"].ToString()
        );
        IQueryable<Quote> query = _db.Quotes.AsNoTracking();
        if (!filter.IsEmpty)
        {
            var candidates = _db
                .Quotes.AsNoTracking()
                .Select(q => new
                {
                    q.Id,
                    q.QuoteNumber,
                    q.RecipientName,
                    q.RecipientAddress,
                    q.GeneratedAt,
                    Lines = q
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
                            c.QuoteNumber,
                            c.RecipientName,
                            c.RecipientAddress,
                            c.GeneratedAt,
                            InvoiceSearch.GrossTotal(
                                c.Lines.Select(l => (l.Count, l.UnitPrice, l.VatRate))
                            )
                        ),
                        filter
                    )
                )
                .Select(c => c.Id)
                .ToHashSet();
            query = query.Where(q => matched.Contains(q.Id));
        }

        var quotes = query.Include(q => q.Lines).AsEnumerable().ToList();
        var numbers = InvoiceNumbers(quotes);
        foreach (var quote in quotes)
        {
            Stamp(quote, numbers);
        }

        return quotes.AsQueryable();
    }

    public ActionResult<Quote> Get([FromRoute] long key)
    {
        var quote = _db
            .Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .SingleOrDefault(q => q.Id == key);
        if (quote is null)
        {
            return NotFound();
        }

        Stamp(quote, InvoiceNumbers(new List<Quote> { quote }));
        return Ok(quote);
    }

    [EnableQuery]
    public IQueryable<QuoteLine> GetLines(long key) =>
        _db.QuoteLines.AsNoTracking().Where(l => l.QuoteId == key);

    /// <summary>The original number of each invoice the given quotes became,
    /// by invoice id (a quote's InvoiceId is the original's, so a later
    /// revision of the invoice does not change what the list names).</summary>
    private Dictionary<long, string> InvoiceNumbers(List<Quote> quotes)
    {
        var ids = quotes
            .Where(q => q.InvoiceId != null)
            .Select(q => q.InvoiceId!.Value)
            .Distinct()
            .ToList();
        return ids.Count == 0
            ? new Dictionary<long, string>()
            : _db
                .Invoices.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.InvoiceNumber })
                .ToDictionary(i => i.Id, i => i.InvoiceNumber);
    }

    private void Stamp(Quote quote, Dictionary<long, string> invoiceNumbers)
    {
        var data = _documentData.Build(quote);
        quote.NetTotal = data.NetTotal;
        quote.VatTotal = data.TotalVat;
        quote.GrossTotal = data.GrossTotal;
        quote.InvoiceNumber =
            quote.InvoiceId is { } id && invoiceNumbers.TryGetValue(id, out var number)
                ? number
                : null;
    }
}
