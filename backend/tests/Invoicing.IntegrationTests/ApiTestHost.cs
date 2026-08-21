using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile;

    public ApiTestHost()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbFile)) File.Delete(_dbFile);
    }
}
