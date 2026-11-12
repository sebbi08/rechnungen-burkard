using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services.Backup;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Jobs;

public sealed class JobRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobQueue _queue;
    private readonly ILogger<JobRunner> _logger;
    private readonly DatabaseGate _gate;

    public JobRunner(
        IServiceScopeFactory scopeFactory,
        JobQueue queue,
        ILogger<JobRunner> logger,
        DatabaseGate gate
    )
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
        _gate = gate;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RecoverStuckJobs();
        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                // through the same gate as the requests: a restore refuses to
                // start while a job is queued or running, and this keeps a job
                // from starting while the database file is being swapped
                while (!_gate.TryEnter())
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed unexpectedly", jobId);
                await RecordFailureAsync(jobId, stoppingToken);
            }
            finally
            {
                _gate.Exit();
            }
        }
    }

    /// <summary>Marks a job Failed from a scope of its own, for when the job's
    /// own outcome could not be written. Left Running, it would refuse every
    /// new job of its type (JobService.HasActive) until the next start
    /// requeued it.</summary>
    private async Task RecordFailureAsync(long jobId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.Jobs.FindAsync(new object[] { jobId }, stoppingToken);
            if (job is null || job.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                return;
            }

            job.Status = JobStatus.Failed;
            job.Error = "The job's outcome could not be recorded.";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} could not be marked failed either", jobId);
        }
    }

    private void RecoverStuckJobs()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stuck = db.Jobs.Where(j => j.Status == JobStatus.Running).ToList();
        foreach (var job in stuck)
        {
            job.Status = JobStatus.Queued;
            job.StartedAt = null;
        }

        db.SaveChanges();
        var queuedIds = db.Jobs.Where(j => j.Status == JobStatus.Queued).Select(j => j.Id).ToList();
        foreach (var id in queuedIds)
        {
            _queue.Enqueue(id);
        }
    }

    private async Task ProcessAsync(long jobId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.Jobs.FindAsync(new object[] { jobId }, stoppingToken);
        if (job is null || job.Status != JobStatus.Queued)
        {
            return;
        }

        var handler = scope
            .ServiceProvider.GetServices<IJobHandler>()
            .FirstOrDefault(h => h.Type == job.Type);
        if (handler is null)
        {
            job.Status = JobStatus.Failed;
            job.Error = $"No handler registered for job type '{job.Type}'.";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        var progress = new DbProgressWriter(db, job);
        try
        {
            await handler.ExecuteAsync(job, progress, stoppingToken);
            job.Status = JobStatus.Succeeded;
            job.ProgressPercent = 100;
            job.ProcessedCount = job.TotalCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
        }

        job.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
    }

    private sealed class DbProgressWriter : IProgressWriter
    {
        private readonly AppDbContext _db;
        private readonly Job _job;
        private int _lastPercent = -1;

        public DbProgressWriter(AppDbContext db, Job job)
        {
            _db = db;
            _job = job;
        }

        public async Task ReportAsync(int processed, int total, CancellationToken ct)
        {
            var percent = total <= 0 ? 100 : (int)(processed * 100L / total);
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            _job.ProgressPercent = percent;
            _job.ProcessedCount = processed;
            await _db.SaveChangesAsync(ct);
        }
    }
}
