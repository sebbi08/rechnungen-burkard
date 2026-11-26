using System.Net;
using Burkard.Invoicing.Api.Audit;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A Verlauf entry's time is a UTC instant and is served as one,
/// whatever zone the server runs in. SQLite hands a stored DateTime back without
/// a kind, and OData serialises such a value with the server's own offset: in
/// the image, which runs in Europe/Berlin, 13:32 UTC came out as
/// 13:32+01:00.</summary>
public class AuditTimeTests
{
    private static readonly DateTime Stored = new(2026, 11, 26, 13, 32, 0, DateTimeKind.Utc);

    /// <summary>The server zone of the image, pinned here so the test does not
    /// depend on the zone of the machine it runs on (the SDK container runs in
    /// UTC, where the fault does not show).</summary>
    private static ApiTestHost BerlinHost() =>
        new()
        {
            ConfigureTestServices = s =>
                s.Configure<ODataOptions>(o =>
                    o.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")
                ),
        };

    private static async Task SeedAsync(ApiTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEntries.Add(
            new AuditEntry
            {
                OccurredAt = Stored,
                Action = AuditActions.GraveEdited,
                GraveId = 7,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_served_time_is_the_stored_instant()
    {
        using var host = BerlinHost();
        await SeedAsync(host);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/odata/AuditEntries?$filter=GraveId eq 7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        var entry = Assert.Single(json.RootElement.GetProperty("value").EnumerateArray());
        var served = DateTimeOffset.Parse(entry.GetProperty("OccurredAt").GetString()!);
        Assert.Equal(Stored, served.UtcDateTime);
    }

    [Fact]
    public async Task A_read_entry_carries_its_time_as_UTC()
    {
        using var host = new ApiTestHost();
        await SeedAsync(host);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = await db.AuditEntries.AsNoTracking().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, entry.OccurredAt.Kind);
        Assert.Equal(Stored, entry.OccurredAt);
    }

    [Fact]
    public void The_model_matches_the_migrations()
    {
        using var host = new ApiTestHost();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
