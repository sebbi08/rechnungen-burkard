using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>The stored documents of a half-year's grave invoices, as the bulk
/// download hands them out and the print joins them: one place, so the ZIP and
/// the printout cannot disagree.</summary>
public static class GraveInvoiceDocuments
{
    public static IQueryable<Document> OfHalfYear(AppDbContext db, int year, int half) =>
        db
            .Documents.Where(d =>
                d.Kind == DocumentKind.Normal
                && d.Invoice != null
                // a superseded revision's documents were replaced; they must not
                // go out next to the revision that replaced them
                && d.Invoice.IsLatestRevision
                // a deleted grave's invoices are no longer listed, so they are
                // not handed out either
                && (
                    d.Invoice.GraveId == null
                    || !db.Graves.Any(g => g.Id == d.Invoice.GraveId && g.DeletedAt != null)
                )
                && d.Invoice.PeriodYear == year
                && d.Invoice.PeriodHalf == half
            )
            .OrderBy(d => d.FileName);
}
