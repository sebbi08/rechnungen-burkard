namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>Puts every request that can reach the database through the
/// <see cref="DatabaseGate"/>, and answers 503 while a restore has it closed.
/// The restore request itself goes around it: it is the one closing it.</summary>
public sealed class DatabaseGateMiddleware
{
    private const string RestorePath = "/odata/RestoreBackup";

    private readonly RequestDelegate _next;
    private readonly DatabaseGate _gate;

    public DatabaseGateMiddleware(RequestDelegate next, DatabaseGate gate)
    {
        _next = next;
        _gate = gate;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Guarded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!_gate.TryEnter())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            await context.Response.WriteAsJsonAsync(
                new
                {
                    error = new
                    {
                        code = "RestoreInProgress",
                        message = "A backup is being restored; try again shortly.",
                    },
                }
            );
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            _gate.Exit();
        }
    }

    private static bool Guarded(PathString path)
    {
        if (
            path.StartsWithSegments(RestorePath, StringComparison.OrdinalIgnoreCase, out var rest)
            && (!rest.HasValue || rest == "/")
        )
        {
            return false;
        }

        return path.StartsWithSegments("/odata", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase);
    }
}
