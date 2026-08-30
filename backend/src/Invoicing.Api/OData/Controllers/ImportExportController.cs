using Burkard.Invoicing.Api.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class ImportExportController : ODataController
{
    private const long MaxArchiveBytes = 32 * 1024 * 1024;

    private readonly ImportService _import;
    private readonly ILogger<ImportExportController> _logger;

    public ImportExportController(ImportService import, ILogger<ImportExportController> logger)
    {
        _import = import;
        _logger = logger;
    }

    [HttpGet("ImportTemplate")]
    public IActionResult Template() =>
        File(ImportService.BuildTemplate(), "application/zip", "import-vorlage.zip");

    /// <summary>Validates the uploaded archive and, only when it is free of
    /// problems, writes all of it. A rejected archive answers with the full
    /// report so every problem can be fixed in one pass (FR-059).</summary>
    [HttpPost("ImportArchive")]
    [RequestSizeLimit(MaxArchiveBytes)]
    public async Task<IActionResult> Import()
    {
        // read the upload off the form directly: OData's input formatters do not
        // model-bind multipart bodies
        IFormFile? file = null;
        try
        {
            if (Request.HasFormContentType)
            {
                file = Request.Form.Files.GetFile("file");
            }
        }
        catch (InvalidDataException)
        {
            // a malformed multipart body is a bad request, not a server fault
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = "ArchiveRequired",
                        message = "An archive file is required.",
                    },
                }
            );
        }

        try
        {
            // ZipArchive needs to seek, which the request body stream cannot
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream())
            {
                await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            }

            buffer.Position = 0;

            var result = await _import.ImportAsync(buffer, HttpContext.RequestAborted);
            if (result.Summary is null)
            {
                return BadRequest(
                    new
                    {
                        error = new
                        {
                            code = "ImportInvalid",
                            message = "The archive was rejected.",
                        },
                        problems = result.Problems,
                    }
                );
            }

            return Ok(new { summary = result.Summary });
        }
        catch (Exception ex)
        {
            // Letting the exception escape would cause the test host to rethrow it on
            // the client instead of producing a 500 response; mirror production behavior.
            _logger.LogError(ex, "ImportArchive failed");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = new { code = "InternalError", message = "An internal error occurred." },
                }
            );
        }
    }
}
