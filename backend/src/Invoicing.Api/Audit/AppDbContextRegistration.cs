using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Burkard.Invoicing.Api.Audit;

public static class AppDbContextRegistration
{
    /// <summary>The database context with the audit guard in it, and the scope
    /// the guard reads. The app and the test host both register it here, so the
    /// tests run the guard the app runs. <see cref="AuditGuardMode"/> is the
    /// caller's to register, since it depends on the environment.</summary>
    public static IServiceCollection AddAppDbContext(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.TryAddScoped<AuditScope>();
        services.TryAddScoped<AuditGuardInterceptor>();
        services.AddDbContext<AppDbContext>(
            (sp, o) =>
                o.UseSqlite(connectionString)
                    .AddInterceptors(sp.GetRequiredService<AuditGuardInterceptor>())
        );
        return services;
    }
}
