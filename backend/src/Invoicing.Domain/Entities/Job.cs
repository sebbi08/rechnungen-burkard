namespace Burkard.Invoicing.Domain.Entities;

public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class Job
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int ProgressPercent { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
