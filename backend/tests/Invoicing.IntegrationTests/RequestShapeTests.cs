using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>Requests the UI never sends, but a client may: each used to end in
/// an unhandled 500, or in a header pointing nowhere.</summary>
public class RequestShapeTests
{
    private static async Task<long> CreateItemAsync(HttpClient client, string number)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var created = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = number,
                    Description = "Grabpflege",
                    VatRate = 7.0m,
                    DefaultPrice = 10.00m,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ODataJson.ReadAsync(created);
        return json.RootElement.GetProperty("Id").GetInt64();
    }

    /// <summary>The database assigns a new row's id; one in the body is not
    /// the client's to choose, and an existing one collided with its row.</summary>
    [Fact]
    public async Task A_create_ignores_an_id_in_its_body()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var existing = await CreateItemAsync(client, "1001");

        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Id = existing,
                    Number = "1002",
                    Description = "Gießdienst",
                    VatRate = 7.0m,
                    DefaultPrice = 5.00m,
                }
            )
        );
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new
                {
                    Id = 1,
                    Name = "Anna Beispiel",
                    Address = "Weg 1",
                }
            )
        );
        var second = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(
                new
                {
                    Id = 1,
                    Name = "Bert Beispiel",
                    Address = "Weg 2",
                }
            )
        );

        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        using var itemJson = await ODataJson.ReadAsync(item);
        Assert.NotEqual(existing, itemJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal(HttpStatusCode.Created, recipient.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    /// <summary>A row's id is its identity; a PATCH that would change it is
    /// refused rather than failing in the database.</summary>
    [Theory]
    [InlineData("InvoiceItems")]
    [InlineData("Recipients")]
    public async Task A_patch_cannot_change_a_rows_id(string set)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await CreateItemAsync(client, "1001");
        var recipient = await client.PostAsync(
            "/odata/Recipients",
            ODataJson.Body(new { Name = "Anna Beispiel", Address = "Weg 1" })
        );
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        var id =
            set == "InvoiceItems" ? itemId : recipientJson.RootElement.GetProperty("Id").GetInt64();

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{set}({id})")
        {
            Content = ODataJson.Body(new { Id = id + 100 }),
        };
        var response = await client.SendAsync(patch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            "IdReadOnly",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Theory]
    [InlineData("ValidateGraveBilling")]
    [InlineData("PreviewGraveBilling")]
    public async Task A_billing_check_without_lines_is_refused(string action)
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            $"/odata/{action}",
            new StringContent(
                """{"recipients":1,"lines":null}""",
                System.Text.Encoding.UTF8,
                "application/json"
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The Location of a created invoice used to be relative to the
    /// action's own URL, so it resolved to /odata/odata/Invoices(1).</summary>
    [Fact]
    public async Task A_created_invoice_is_located_where_it_can_be_read()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var itemId = await CreateItemAsync(client, "1001");
        long recipientId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipient = new Recipient
            {
                Number = "E-0001",
                Name = "Anna Beispiel",
                Address = "Weg 1",
            };
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            recipientId = recipient.Id;
        }

        var created = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var location = new System.Uri(
            created.RequestMessage!.RequestUri!,
            created.Headers.Location!
        );
        var read = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
