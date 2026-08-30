using System.Net;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Starts a grave generation and waits for the job to finish. Generation
/// runs as a background job, so a test that asserts on its invoices has to wait
/// for it rather than for the POST.</summary>
public static class GraveGenerationHelper
{
    public static async Task RunAsync(HttpClient client, int year, int half)
    {
        var start = await client.PostAsync(
            "/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year, half })
        );
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/odata/Jobs({jobId})");
            response.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(response);
            var status = json.RootElement.GetProperty("Status").GetString();
            Assert.NotEqual("Failed", status);
            if (status == "Succeeded")
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Grave generation for {year}-{half} did not finish in time.");
    }
}
