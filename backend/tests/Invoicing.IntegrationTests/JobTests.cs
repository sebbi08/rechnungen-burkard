using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class TestJobHandler : IJobHandler
{
    public string Type => "Test";
    public int Calls;

    public async Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct)
    {
        Calls++;
        await progress.ReportAsync(0, 10, ct);
        await progress.ReportAsync(5, 10, ct);
        await progress.ReportAsync(10, 10, ct);
    }
}

public class JobTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public JobTests(ApiTestHost host) => _host = host;

    private async Task<Job> WaitForTerminalAsync(long jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("job did not finish");
    }

    [Fact]
    public async Task Enqueued_job_runs_to_success_and_updates_progress()
    {
        using var scope = _host.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var handler = (TestJobHandler)
            _host.Services.GetServices<IJobHandler>().Single(h => h is TestJobHandler);
        var callsBefore = handler.Calls;
        var job = await jobs.EnqueueAsync("Test", null, 10, CancellationToken.None);

        var finished = await WaitForTerminalAsync(job.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(JobStatus.Succeeded, finished.Status);
        Assert.Equal(100, finished.ProgressPercent);
        Assert.Equal(10, finished.ProcessedCount);

        Assert.Equal(callsBefore + 1, handler.Calls);
    }

    [Fact]
    public async Task Job_can_be_polled_via_odata()
    {
        using var scope = _host.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = await jobs.EnqueueAsync("Test", null, 10, CancellationToken.None);
        await WaitForTerminalAsync(job.Id, TimeSpan.FromSeconds(15));

        using var client = _host.CreateClient();
        var response = await client.GetAsync($"/odata/Jobs({job.Id})");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("Succeeded", json.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Job_left_running_is_requeued_on_startup()
    {
        var recoveryHost = new ApiTestHost();
        try
        {
            // seed a stuck job directly before first app start
            using (
                var setup = new ServiceCollection()
                    .AddDbContext<AppDbContext>(o =>
                        o.UseSqlite($"Data Source={recoveryHost.DbFilePath}")
                    )
                    .BuildServiceProvider()
            )
            {
                var db = setup.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
                db.Jobs.Add(
                    new Job
                    {
                        Type = "Test",
                        Status = JobStatus.Running,
                        TotalCount = 10,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            }

            using (var client = recoveryHost.CreateClient())
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                Job? finished = null;
                while (DateTime.UtcNow < deadline)
                {
                    using var scope = recoveryHost.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    finished = await db.Jobs.OrderByDescending(j => j.Id).FirstAsync();
                    if (finished.Status is JobStatus.Succeeded or JobStatus.Failed)
                        break;
                    await Task.Delay(50);
                }

                Assert.NotNull(finished);
                Assert.Equal(JobStatus.Succeeded, finished!.Status);
            }
        }
        finally
        {
            recoveryHost.Dispose();
        }
    }
}
