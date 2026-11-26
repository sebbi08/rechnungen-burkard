using System;
using System.IO;
using System.Linq;
using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
    // a directory of its own per host: the restore staging folder and the
    // safety copies of a restore sit beside the database, and hosts of test
    // classes running in parallel must not see each other's
    private readonly string _directory = Directory
        .CreateDirectory(Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}"))
        .FullName;

    /// <summary>The moment the app's clock stands at: 2026-10-06, in H2 2026, so
    /// a term starting in 2026 has already begun. Set it in an initializer
    /// (<c>new ApiTestHost { Now = ... }</c>) before the host is first used; the
    /// class stays constructible as an xunit fixture.</summary>
    public DateTime Now { get; init; } = new(2026, 10, 6, 12, 0, 0);

    /// <summary>The same moment in UTC, for tests about the hours after midnight
    /// in which the two days differ. Defaults to <see cref="Now"/>.</summary>
    public DateTime? UtcNow { get; init; }

    public string DbFilePath => Path.Combine(_directory, "invoicing.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // the app's configuration goes too: AddDbContext accumulates them,
            // and the app's would add its interceptor a second time
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddAppDbContext($"Data Source={DbFilePath}");
            services.AddSingleton<IJobHandler, TestJobHandler>();
            services.AddSingleton<IJobHandler, UnrecordableJobHandler>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now, UtcNow));
            ConfigureTestServices?.Invoke(services);
        });
    }

    /// <summary>Registrations applied after the app's and the host's own, e.g.
    /// <c>s =&gt; s.AddSingleton(typeof(AuditGuardMode), AuditGuardMode.Record)</c>.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    /// <summary>Starts the app, which migrates the database, and puts
    /// <see cref="TestCemetery"/> in it: every grave needs a cemetery, and most
    /// tests are about something else.</summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Cemeteries.Add(new Cemetery { Id = TestCemetery.Id, Name = TestCemetery.Name });
        db.SaveChanges();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
    }
}
