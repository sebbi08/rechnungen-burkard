using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddOData(o =>
    o.Select().Filter().OrderBy().Count().SetMaxTop(200)
     .AddRouteComponents("odata", EdmModelBuilder.Build()));

var connectionString = builder.Configuration.GetConnectionString("Invoicing")
    ?? "Data Source=invoicing.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<InvoiceNumberService>();
builder.Services.AddScoped<DocumentDataBuilder>();
builder.Services.AddSingleton<IDocumentGenerator, QuestPdfDocumentGenerator>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<GraveInvoiceGenerator>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<IJobHandler, GraveInvoicesJobHandler>();
builder.Services.AddHostedService<JobRunner>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    DatabaseSeeder.Seed(db);
}

// Serve the UI5 build from wwwroot (combined release image).
app.UseDefaultFiles();
app.UseStaticFiles();

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
