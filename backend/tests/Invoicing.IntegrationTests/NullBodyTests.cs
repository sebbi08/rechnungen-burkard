using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class NullBodyTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public NullBodyTests(ApiTestHost host) => _host = host;

    private static StringContent NullBody() => new("null", Encoding.UTF8, "application/json");

    private async Task AssertInvalidRequestAsync(string url, HttpContent content)
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidRequest",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public Task Post_vat_rate_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/VatRates", NullBody());

    [Fact]
    public Task Post_item_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/InvoiceItems", NullBody());

    [Fact]
    public Task Post_recipient_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/Recipients", NullBody());

    [Fact]
    public Task Post_grave_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/Graves", NullBody());

    [Fact]
    public Task Post_grave_item_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/GraveItems", NullBody());

    [Fact]
    public Task Post_grave_recipient_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/GraveRecipients", NullBody());

    [Fact]
    public Task Post_bulk_download_with_null_body_is_rejected() =>
        AssertInvalidRequestAsync("/odata/BulkDownloadDocuments", NullBody());

    [Fact]
    public async Task Create_invoice_with_null_body_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/CreateInvoice", NullBody());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidRequest",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_revision_with_null_body_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/CreateRevision", NullBody());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidRequest",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_revision_without_lines_is_rejected_with_NoLines()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateRevision",
            ODataJson.Body(new { invoiceId = 1, recipientId = 1 })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NoLines",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Generate_grave_invoices_with_null_body_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/GenerateGraveInvoices", NullBody());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidRequest",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Patch_settings_with_null_body_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PatchAsync("/odata/Settings", NullBody());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "InvalidRequest",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task Create_invoice_without_lines_is_rejected_with_NoLines()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(new { recipientId = 1 })
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "NoLines",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
}
