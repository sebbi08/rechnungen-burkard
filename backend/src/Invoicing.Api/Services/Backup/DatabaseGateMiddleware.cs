namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>Puts every request that can reach the database through the
/// <see cref="DatabaseGate"/>, and answers 503 while a restore has it closed.
/// The restore request itself goes around it: it is the one closing it.
/// Every <c>/odata</c> response also carries the <see cref="DataGeneration"/>, and a
/// write that names an older one is refused: its page shows data a restore has
/// replaced.</summary>
public sealed class DatabaseGateMiddleware
{
    public const string GenerationHeader = "X-Data-Generation";
    private const string RestorePath = "/odata/RestoreBackup";

    private readonly RequestDelegate _next;
    private readonly DatabaseGate _gate;
    private readonly DataGeneration _generation;

    public DatabaseGateMiddleware(
        RequestDelegate next,
        DatabaseGate gate,
        DataGeneration generation
    )
    {
        _next = next;
        _gate = gate;
        _generation = generation;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/odata", StringComparison.OrdinalIgnoreCase))
        {
            // read when the response starts, so the restore's own answer
            // already carries the new generation
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[GenerationHeader] = _generation.Current.ToString();
                return Task.CompletedTask;
            });
        }

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
            if (Stale(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error = new
                        {
                            code = "DataReplaced",
                            message = "The data was replaced by a restore; reload the page.",
                        },
                    }
                );
                return;
            }

            await _next(context);
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>A write from a page that names another generation than the current
    /// one. No header, no claim: scripts and tests are not pages.</summary>
    private bool Stale(HttpRequest request) =>
        request.Path.StartsWithSegments("/odata", StringComparison.OrdinalIgnoreCase)
        && !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method)
        && request.Headers.TryGetValue(GenerationHeader, out var sent)
        && sent != _generation.Current.ToString();

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
