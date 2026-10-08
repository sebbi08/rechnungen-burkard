using System.Text.Json;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Jobs;

public sealed record GraveInvoicesPayload(int Year, int Half);

public sealed record GraveSkip(string Number, string Reason);

public sealed class GraveInvoicesJobHandler : IJobHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GraveInvoicesJobHandler(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public string Type => "GraveInvoices";

    public async Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct)
    {
        var payload =
            JsonSerializer.Deserialize<GraveInvoicesPayload>(job.PayloadJson ?? "{}")
            ?? throw new InvalidOperationException("missing payload");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<GraveInvoiceGenerator>();

        // a grave outside its term for this half-year, or with nothing to bill
        // in it, is not "skipped", it is simply not due
        var (todo, _) = await GraveBilling.OpenForHalfYearAsync(db, payload.Year, payload.Half, ct);
        job.TotalCount = todo.Count;
        var skipped = new List<GraveSkip>();
        var processed = 0;

        foreach (var grave in todo)
        {
            ct.ThrowIfCancellationRequested();
            var result = await generator.GenerateForGraveAsync(
                grave,
                payload.Year,
                payload.Half,
                ct
            );
            if (result.SkipReason is not null)
            {
                skipped.Add(new GraveSkip(grave.GraveNumber, result.SkipReason));
            }

            processed++;
            await progress.ReportAsync(processed, todo.Count, ct);
        }

        job.ResultJson = JsonSerializer.Serialize(
            new { InvoicedGraves = todo.Count - skipped.Count, SkippedGraves = skipped }
        );
    }
}
