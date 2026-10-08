using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class HealthTests
{
    [Fact]
    public async Task Healthz_returns_ok_and_the_running_version()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        // which release is running is otherwise nowhere to be read
        Assert.Matches(@"^\d+\.\d+\.\d+", json.RootElement.GetProperty("version").GetString());
    }

    /// <summary>The container's health check asks /healthz, so it has to fail
    /// when the app cannot do its work: it used to answer ok without looking
    /// at the database at all.</summary>
    [Fact]
    public async Task Healthz_fails_when_the_database_cannot_be_read()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);

        // the next connection opens an empty file: no tables, nothing to read
        SqliteConnection.ClearAllPools();
        File.Delete(host.DbFilePath);

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
