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
            return Rejected(new BackupRejectedException("JobAlreadyRunning"));
        }

        var fileName = $"rechnungen-sicherung-{_clock.GetLocalNow():yyyy-MM-dd-HHmm}.zip";
        // buffered rather than streamed: the status code is still ours to set if
        // writing the archive fails
        var buffer = new MemoryStream();
        await _backup.WriteBackupAsync(buffer, ct);
        buffer.Position = 0;
        return File(buffer, "application/zip", fileName);
    }

    /// <summary>Checks an uploaded backup and holds it under a token until it is
    /// restored or discarded.</summary>
    [HttpPost("UploadBackup")]
    [RequestSizeLimit(2_147_483_648)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_147_483_648)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (_jobs.HasAnyActive())
        {
            return Rejected(new BackupRejectedException("JobAlreadyRunning"));
        }

        // read the upload off the form directly: OData's input formatters do not
        // model-bind multipart bodies
        var file = Request.HasFormContentType ? Request.Form.Files.GetFile("file") : null;
        if (file is null)
        {
            return Rejected(new BackupRejectedException("ArchiveRequired"));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var staged = await _backup.StageAsync(stream, ct);
            var s = staged.Summary;
            return Ok(
                new
                {
                    token = staged.Token,
                    summary = new
                    {
                        createdAt = s.CreatedAtUtc,
                        appVersion = s.AppVersion,
                        graves = s.Graves,
                        recipients = s.Recipients,
                        invoices = s.Invoices,
                        newerInvoicesHere = s.NewerInvoicesHere,
                    },
                }
            );
        }
        catch (BackupRejectedException e)
        {
            return Rejected(e);
        }
    }

    public sealed record DiscardRequest(string? Token);

    [HttpPost("DiscardBackup")]
    public IActionResult Discard([FromBody] DiscardRequest request)
    {
        _backup.Discard(request.Token ?? "");
        return NoContent();
    }

    public sealed record RestoreRequest(string? Token);

    /// <summary>Replaces all data with the backup staged under the token. Other
    /// requests are answered 503 meanwhile (DatabaseGateMiddleware).</summary>
    [HttpPost("RestoreBackup")]
    public async Task<IActionResult> Restore(
        [FromBody] RestoreRequest request,
        CancellationToken ct
    )
    {
        try
        {
            await _backup.RestoreAsync(request.Token ?? "", ct);
            return NoContent();
        }
        catch (BackupRejectedException e)
        {
            return Rejected(e);
        }
        catch (RestoreFailedException)
        {
            // logged where it happened, with what was put back
            return Error("RestoreFailed");
        }
    }

    private IActionResult Rejected(BackupRejectedException e) => Error(e.Code);

    // the one place an error code becomes a status and an error body
    private IActionResult Error(string code)
    {
        var status = code switch
        {
            "JobAlreadyRunning" => StatusCodes.Status409Conflict,
            "BackupTokenUnknown" => StatusCodes.Status404NotFound,
            "RestoreFailed" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new { error = new { code, message = Message(code) } });
    }

    private static string Message(string code) =>
        code switch
        {
            "JobAlreadyRunning" => "A job is running; try again when it has finished.",
            "ArchiveRequired" => "Upload the backup as the multipart field 'file'.",
            "BackupInvalid" => "The file is not a backup of this application.",
            "BackupDamaged" => "The backup's database is damaged.",
            "BackupFromNewerVersion" => "The backup was written by a newer version.",
            "BackupTokenUnknown" => "The backup is no longer staged; upload it again.",
            "RestoreFailed" => "Restoring the backup failed; the server log says what happened.",
            _ => code,
        };
}
