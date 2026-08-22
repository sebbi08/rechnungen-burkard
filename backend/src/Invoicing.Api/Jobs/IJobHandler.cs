using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Jobs;

public interface IProgressWriter
{
    Task ReportAsync(int processed, int total, CancellationToken ct);
}

public interface IJobHandler
{
    string Type { get; }
    Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct);
}
