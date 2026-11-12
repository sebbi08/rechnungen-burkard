using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class BackupController : ODataController
{
    private readonly BackupService _backup;
    private readonly JobService _jobs;
    private readonly IClock _clock;

    public BackupController(BackupService backup, JobService jobs, IClock clock)
    {
        _backup = backup;
        _jobs = jobs;
        _clock = clock;
    }

    /// <summary>The whole database as a ZIP. Refused while any job runs: a run
    /// writes in several steps, and a copy taken in between is half of it.</summary>
    [HttpGet("DownloadBackup")]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        if (_jobs.HasAnyActive())
        {
            return Conflict(
                new
                {
                    error = new
                    {
                        code = "JobAlreadyRunning",
                        message = "A job is running; try again when it has finished.",
                    },
                }
            );
        }

        var fileName = $"rechnungen-sicherung-{_clock.GetLocalNow():yyyy-MM-dd-HHmm}.zip";
        // buffered rather than streamed: the status code is still ours to set if
        // writing the archive fails
        var buffer = new MemoryStream();
        await _backup.WriteBackupAsync(buffer, ct);
        buffer.Position = 0;
        return File(buffer, "application/zip", fileName);
    }
}
