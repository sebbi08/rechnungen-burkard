using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceNumberService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public InvoiceNumberService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Computes the next number for the current local year. Call it inside the
    /// transaction that inserts the Invoice row: SQLite opens that transaction
    /// with BEGIN IMMEDIATE, so no other connection can insert between this read
    /// and the insert. Outside one, concurrent callers read the same number and
    /// all but one fail on the unique (Year, Sequence) index.
    /// </summary>
    public async Task<(string Number, int Year, int Sequence)> AllocateAsync(
        CancellationToken ct = default
    )
    {
        var year = _clock.GetLocalNow().Year;
        var maxSequence =
            await _db.Invoices.Where(i => i.Year == year).MaxAsync(i => (int?)i.Sequence, ct) ?? 0;
        var sequence = maxSequence + 1;
        return (InvoiceNumberFormatter.ForOriginal(year, sequence), year, sequence);
    }
}
