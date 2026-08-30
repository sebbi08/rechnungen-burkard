using System.Linq;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class SeedTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public SeedTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Startup_migrates_schema_and_seeds_default_settings()
    {
        using var client = _host.CreateClient(); // triggers app startup incl. migrate + seed
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Single(db.GlobalSettings);
        Assert.Equal("Kopie", db.GlobalSettings.Single().StampWord);
        Assert.Equal("", db.GlobalSettings.Single().EndingText);

        // all tables exist (smoke insert/read on one of them)
        db.VatRates.Add(new VatRate { Rate = 7m });
        await db.SaveChangesAsync();
        Assert.Contains(db.VatRates, v => v.Rate == 7m);
    }
}
