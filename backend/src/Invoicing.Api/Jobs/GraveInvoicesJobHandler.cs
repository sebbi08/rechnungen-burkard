using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Burkard.Invoicing.Api.Jobs;

public sealed record GraveInvoicesPayload(int Year, int Half);

public sealed class GraveInvoicesJobHandler : IJobHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GraveInvoicesJobHandler(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string Type => "GraveInvoices";

    public async Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<GraveInvoicesPayload>(job.PayloadJson ?? "{}")
            ?? throw new InvalidOperationException("missing payload");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<GraveInvoiceGenerator>();

        var invoiced = db.GraveInvoiceRuns
            .Where(r => r.Year == payload.Year && r.Half == payload.Half)
            .Select(r => r.GraveId)
            .ToHashSet();

        var graves = await db.Graves
            .OrderBy(g => g.GraveNumber)
            .ToListAsync(ct);

        var todo = graves.Where(g => !invoiced.Contains(g.Id)).ToList();
        var skipped = new List<string>();
        var processed = 0;

        foreach (var grave in todo)
        {
            ct.ThrowIfCancellationRequested();
            var invoices = await generator.GenerateForGraveAsync(grave, payload.Year, payload.Half, ct);
            if (invoices is null)
            {
                skipped.Add(grave.GraveNumber);
            }

            processed++;
            await progress.ReportAsync(processed, todo.Count, ct);
        }

        job.ResultJson = JsonSerializer.Serialize(new
        {
            InvoicedGraves = todo.Count - skipped.Count,
            SkippedGraves = skipped,
        });
    }
}
