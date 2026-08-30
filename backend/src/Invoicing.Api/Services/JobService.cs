using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class JobService
{
    private readonly AppDbContext _db;
    private readonly JobQueue _queue;

    public JobService(AppDbContext db, JobQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<Job> EnqueueAsync(
        string type,
        string? payloadJson,
        int totalCount,
        CancellationToken ct
    )
    {
        var job = new Job
        {
            Type = type,
            Status = JobStatus.Queued,
            PayloadJson = payloadJson,
            TotalCount = totalCount,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        _queue.Enqueue(job.Id);
        return job;
    }

    public bool HasActive(string type) =>
        _db.Jobs.Any(j =>
            j.Type == type && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
        );
}
