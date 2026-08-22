using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceNumberService
{
    private readonly AppDbContext _db;

    public InvoiceNumberService(AppDbContext db) => _db = db;

    /// <summary>
    /// Computes the next number for the current year. The caller inserts the
    /// Invoice row inside its own transaction; the unique (Year, Sequence)
    /// index makes concurrent allocations fail loudly (retry in caller).
    /// </summary>
    public async Task<(string Number, int Year, int Sequence)> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var maxSequence = await _db.Invoices
            .Where(i => i.Year == year)
            .MaxAsync(i => (int?)i.Sequence, ct) ?? 0;
        var sequence = maxSequence + 1;
        return (InvoiceNumberFormatter.ForOriginal(year, sequence), year, sequence);
    }
}
