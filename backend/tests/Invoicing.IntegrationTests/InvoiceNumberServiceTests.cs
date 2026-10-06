using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceNumberServiceTests
{
    [Fact]
    public async Task Allocate_uses_local_year_when_utc_is_still_previous_year()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"invoice-number-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbFile}")
                .Options;
            using var db = new AppDbContext(options);
            db.Database.Migrate();

            var clock = new FixedClock(
                new DateTime(2026, 1, 1, 0, 15, 0),
                new DateTime(2025, 12, 31, 23, 15, 0)
            );
            Assert.Equal(2025, clock.GetUtcNow().Year); // real UTC is still in the old year

            var service = new InvoiceNumberService(db, clock);
            var (number, year, sequence) = await service.AllocateAsync();

            Assert.Equal(2026, year);
            Assert.Equal(1, sequence);
            Assert.Equal("2026-0001", number);
        }
        finally
        {
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }
}
