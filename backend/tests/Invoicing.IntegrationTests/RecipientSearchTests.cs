using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RecipientSearchTests
{
    private static async Task SeedAsync(ApiTestHost host, params Recipient[] recipients)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Recipients.AddRange(recipients);
        await db.SaveChangesAsync();
    }

    private static async Task<string[]> NumbersAsync(ApiTestHost host, string query)
    {
        using var client = host.CreateClient();
        var response = await client.GetAsync("/odata/Recipients?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return json
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(r => r.GetProperty("Number").GetString()!)
            .OrderBy(x => x)
            .ToArray();
    }

    [Fact]
    public async Task Each_column_searches_its_own_field()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            new Recipient
            {
                Number = "1001",
                Name = "Hans Berger",
                Address = "Ahornweg 1",
                Email = "berger@example.org",
            },
            new Recipient
            {
                Number = "1002",
                Name = "Ahorn Lang",
                Address = "Birkenallee 2",
                Email = "lang@1001.example",
            }
        );
        Assert.Equal(new[] { "1001" }, await NumbersAsync(host, "name=berger"));
        Assert.Equal(new[] { "1002" }, await NumbersAsync(host, "name=ahorn"));
        Assert.Equal(new[] { "1001" }, await NumbersAsync(host, "number=1001"));
        Assert.Equal(new[] { "1001" }, await NumbersAsync(host, "address=ahornweg"));
        Assert.Equal(new[] { "1002" }, await NumbersAsync(host, "email=1001"));
    }

    [Fact]
    public async Task Umlauts_fold_case()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            new Recipient
            {
                Number = "1001",
                Name = "MÜLLER Hans",
                Address = "x",
            },
            new Recipient
            {
                Number = "1002",
                Name = "Lang",
                Address = "y",
            }
        );
        Assert.Equal(new[] { "1001" }, await NumbersAsync(host, "name=m%C3%BCller"));
    }

    [Fact]
    public async Task Paging_and_count_apply_to_the_matches()
    {
        using var host = new ApiTestHost();
        await SeedAsync(
            host,
            new Recipient
            {
                Number = "1001",
                Name = "Bauer 1",
                Address = "x",
            },
            new Recipient
            {
                Number = "1002",
                Name = "Bauer 2",
                Address = "x",
            },
            new Recipient
            {
                Number = "1003",
                Name = "Bauer 3",
                Address = "x",
            },
            new Recipient
            {
                Number = "1004",
                Name = "Lang",
                Address = "x",
            }
        );
        using var client = host.CreateClient();
        var response = await client.GetAsync("/odata/Recipients?name=bauer&$top=2&$count=true");
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(2, json.RootElement.GetProperty("value").GetArrayLength());
        Assert.Equal(3, json.RootElement.GetProperty("@odata.count").GetInt32());
    }
}
