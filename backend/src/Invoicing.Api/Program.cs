using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Import;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddOData(o =>
{
    // Without this, the no-$-prefix alias for the built-in $search option claims our
    // custom "search" query option (InvoicesController.Get reads it from Request.Query),
    // and OData's own query-option parser then 400s on an empty "search=" before our
    // action code ever runs.
    o.EnableNoDollarQueryOptions = false;
    // Expand() was missing until the invoice detail page's readList became the
    // first caller to pass $expand (Lines, Documents): without it every
    // navigation property — collection or single-valued — is rejected with
    // "The property 'X' cannot be used in the $expand query option.", which is
    // not what D-002 promises for reads through the OData v4 model.
    o.Select().Filter().OrderBy().Count().Expand().SetMaxTop(200)
     .AddRouteComponents("odata", EdmModelBuilder.Build());
});

var connectionString = builder.Configuration.GetConnectionString("Invoicing")
    ?? "Data Source=invoicing.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<InvoiceNumberService>();
builder.Services.AddScoped<DocumentDataBuilder>();
builder.Services.AddSingleton<IDocumentGenerator, QuestPdfDocumentGenerator>();
builder.Services.AddScoped<InvoiceDocumentFactory>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<GraveInvoiceGenerator>();
builder.Services.AddScoped<GraveInvoiceRevisionService>();
builder.Services.AddScoped<GraveService>();
builder.Services.AddScoped<RecipientNumberService>();
builder.Services.AddScoped<ImportValidator>();
builder.Services.AddScoped<ImportService>();
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
