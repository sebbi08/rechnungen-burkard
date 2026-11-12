using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

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
        // built whole in a temporary file before the answer starts, so the status
        // code is still ours to set if building fails; the file is deleted when
        // the response has sent it and closes the stream
        Stream backup;
        try
        {
            backup = await _backup.CreateBackupAsync(ct);
        }
        catch (BackupRejectedException e)
        {
            return Rejected(e);
        }
        return File(backup, "application/zip", fileName);
    }

    /// <summary>Checks an uploaded backup and holds it under a token until it is
    /// restored or discarded. The multipart body is read section by section and
    /// the file streamed straight into the staging folder: <c>Request.Form</c>
    /// would buffer it in the container's temporary folder, outside the data
    /// volume, and block a thread while doing so.</summary>
    [HttpPost("UploadBackup")]
    [RequestSizeLimit(2_147_483_648)]
    [DisableFormValueModelBinding]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (_jobs.HasAnyActive())
        {
            return Rejected(new BackupRejectedException("JobAlreadyRunning"));
        }

        if (
            !MediaTypeHeaderValue.TryParse(Request.ContentType, out var type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary) is not { Length: > 0 } boundary
        )
        {
            return Rejected(new BackupRejectedException("ArchiveRequired"));
        }

        var reader = new MultipartReader(boundary.Value!, Request.Body);
        while (await NextSectionAsync(reader, ct) is { } section)
        {
            if (!IsFileField(section))
            {
                continue;
            }

            try
            {
                var staged = await _backup.StageAsync(section.Body, ct);
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

        return Rejected(new BackupRejectedException("ArchiveRequired"));
    }

    /// <summary>The next section, or null at the end -- also of a body that is
    /// not well-formed multipart, which then simply has no file in it.</summary>
    private static async Task<MultipartSection?> NextSectionAsync(
        MultipartReader reader,
        CancellationToken ct
    )
    {
        try
        {
            return await reader.ReadNextSectionAsync(ct);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The form field <c>file</c>, sent as a file, as the import takes it.</summary>
    private static bool IsFileField(MultipartSection section) =>
        ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
        && disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
        && HeaderUtilities.RemoveQuotes(disposition.Name).Equals("file", StringComparison.Ordinal)
        && (disposition.FileName.HasValue || disposition.FileNameStar.HasValue);

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
        catch (RestoreFailedException e)
        {
            // logged where it happened, with what was put back
            return Error(e.RolledBack ? "RestoreFailed" : "RestoreRollbackFailed");
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
            "RestoreRollbackFailed" => StatusCodes.Status500InternalServerError,
            "RestoreInProgress" => StatusCodes.Status503ServiceUnavailable,
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
            "RestoreRollbackFailed" =>
                "Restoring the backup failed and the replaced database could not be put back; "
                    + "repair the instance by hand as the README describes.",
            "RestoreInProgress" => "A backup is being restored; try again shortly.",
            _ => code,
        };

    /// <summary>Keeps model binding off the form. MVC's form value providers read
    /// the whole form, files included, before any action with a parameter runs,
    /// which would leave the action nothing to stream.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var factories = context.ValueProviderFactories;
            factories.RemoveType<FormValueProviderFactory>();
            factories.RemoveType<FormFileValueProviderFactory>();
            factories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }
}
