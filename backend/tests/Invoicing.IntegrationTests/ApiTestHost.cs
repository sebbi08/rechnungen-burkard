using System;
using System.IO;
using System.Linq;
using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(
        Path.GetTempPath(),
        $"invoicing-test-{Guid.NewGuid():N}.db"
    );

    /// <summary>The moment the app's clock stands at: 2026-10-06, in H2 2026, so
    /// a term starting in 2026 has already begun. Set it in an initializer
    /// (<c>new ApiTestHost { Now = ... }</c>) before the host is first used; the
    /// class stays constructible as an xunit fixture.</summary>
    public DateTime Now { get; init; } = new(2026, 10, 6, 12, 0, 0);

    /// <summary>The same moment in UTC, for tests about the hours after midnight
    /// in which the two days differ. Defaults to <see cref="Now"/>.</summary>
    public DateTime? UtcNow { get; init; }

    public string DbFilePath => _dbFile;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
            services.AddSingleton<IJobHandler, TestJobHandler>();
            services.AddSingleton<IJobHandler, UnrecordableJobHandler>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now, UtcNow));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbFile))
            File.Delete(_dbFile);
    }
}
