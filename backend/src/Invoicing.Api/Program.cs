using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddOData(o =>
    o.Select().Filter().OrderBy().Count().SetMaxTop(200)
     .AddRouteComponents("odata", EdmModelBuilder.Build()));

var connectionString = builder.Configuration.GetConnectionString("Invoicing")
    ?? "Data Source=invoicing.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    DatabaseSeeder.Seed(db);
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program>
namespace Burkard.Invoicing.Api
{
    public partial class Program
    {
    }
}
