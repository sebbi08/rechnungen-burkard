using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Burkard.Invoicing.Api.OData;

/// <summary>Hides an action unless the instance is one that exists to be seeded.
///
/// The grave link collections can be written to directly, which puts a grave
/// together without going through SaveGrave — and so without the check that its
/// prices divide evenly across its recipients. Such a grave saves, then never
/// bills: generation skips it. Nothing in the application uses these routes; the
/// E2E suite does, to build its fixtures faster than the dialog can.
///
/// So they are offered only where that is the point. `Invoicing:SeedingEndpoints`
/// is set in docker-compose.e2e.yml and in the workflow's `docker run`, nowhere
/// else — the dev stack and a release deployment answer 404, as if the route had
/// never been mapped. 404 rather than 403: a route that is not there says less
/// about what the instance can do.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SeedingOnlyAttribute : ActionFilterAttribute
{
    public const string ConfigurationKey = "Invoicing:SeedingEndpoints";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var configuration =
            context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>(ConfigurationKey))
        {
            context.Result = new NotFoundResult();
        }
    }
}
