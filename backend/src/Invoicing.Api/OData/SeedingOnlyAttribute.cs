using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Burkard.Invoicing.Api.OData;

/// <summary>Hides an action unless the instance is one that exists to be seeded.
///
/// The grave link collections can be written to directly, and POST /odata/Graves
/// takes nested links along with the grave. Either puts a grave together without
/// going through SaveGrave — and so without the check that its prices divide
/// evenly across its recipients. Such a grave saves, then never bills:
/// generation skips it. Nothing in the application uses these routes; the E2E
/// suite does, to build its fixtures faster than the dialog can.
///
/// So they are offered only where that is the point. `Invoicing:SeedingEndpoints`
/// is set in docker-compose.e2e.yml, in the workflow's `docker run` and by the
/// integration test host — nowhere else. It is read once, at startup, into
/// <see cref="SeedingEndpoints"/>, so a value that is not a boolean stops the
/// host rather than failing every seeding write after it. The dev stack and a
/// release deployment answer 404, as if the route had never been mapped: a
/// resource filter answers before the body is read or the controller is built.
/// 404 rather than 403: a route that is not there says less about what the
/// instance can do.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SeedingOnlyAttribute : Attribute, IResourceFilter
{
    public const string ConfigurationKey = "Invoicing:SeedingEndpoints";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var seeding = context.HttpContext.RequestServices.GetRequiredService<SeedingEndpoints>();
        if (!seeding.Offered)
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}

/// <summary>Whether this instance offers the seeding routes: the flag, read once
/// at startup.</summary>
public sealed record SeedingEndpoints(bool Offered);
