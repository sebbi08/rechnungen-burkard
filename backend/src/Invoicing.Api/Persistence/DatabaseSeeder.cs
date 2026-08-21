using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Persistence;

public static class DatabaseSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.GlobalSettings.Any())
        {
            db.GlobalSettings.Add(new GlobalSettings());
            db.SaveChanges();
        }
    }
}
