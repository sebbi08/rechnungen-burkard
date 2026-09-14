using System;
using System.IO;
using System.Linq;
using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile;
    private readonly bool _seedingEndpoints;

    public string DbFilePath => _dbFile;

    /// <summary>A host that accepts seeding writes, like the E2E stack — which
    /// is what a test host is for. Parameterless on purpose: xUnit resolves a
    /// class fixture through this constructor and ignores default values.</summary>
    public ApiTestHost()
        : this(seedingEndpoints: true) { }

    /// <summary>A host offering what a deployment offers: the grave link
    /// collections refuse writes. A factory rather than a second constructor —
    /// xUnit resolves a class fixture only through a type's single public
    /// one.</summary>
    public static ApiTestHost WithoutSeedingEndpoints() => new(seedingEndpoints: false);

    private ApiTestHost(bool seedingEndpoints)
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db");
        _seedingEndpoints = seedingEndpoints;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            SeedingOnlyAttribute.ConfigurationKey,
            _seedingEndpoints ? "true" : "false"
        );
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
            services.AddSingleton<IJobHandler, TestJobHandler>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbFile))
            File.Delete(_dbFile);
    }
}
