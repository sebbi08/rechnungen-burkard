using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Import;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Read once: a value that is not a boolean stops the host here rather than
// failing every seeding write later.
var seedingEndpoints = builder.Configuration.GetValue<bool>(SeedingOnlyAttribute.ConfigurationKey);
builder.Services.AddSingleton(new SeedingEndpoints(seedingEndpoints));
builder
    .Services.AddControllers()
    .AddOData(o =>
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
        o.Select()
            .Filter()
            .OrderBy()
            .Count()
            .Expand()
            .SetMaxTop(200)
            .AddRouteComponents("odata", EdmModelBuilder.Build());
    });

var connectionString =
    builder.Configuration.GetConnectionString("Invoicing") ?? "Data Source=invoicing.db";
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

// Serve the UI5 build from wwwroot (combined release image). Two of the static
// file middleware's defaults do not fit that build, and both fail silently: a
// resource it will not serve comes back as a bare 404, and UI5 carries on
// without it. A missing message bundle costs the framework every text it
// renders itself, so buttons and value states show the raw message key.
var ui5ContentTypes = new FileExtensionContentTypeProvider();
foreach (
    var extension in new[] { ".properties", ".library", ".supportrc", ".theme", ".theming", ".hpb" }
)
{
    // The middleware answers only for extensions it can name a content type
    // for, and knows none of the runtime's own. application/octet-stream is what
    // `ui5 serve` returns for them in the dev stack; UI5 reads each one as text
    // or as bytes and never looks at the type.
    ui5ContentTypes.Mappings[extension] = "application/octet-stream";
}

if (!string.IsNullOrEmpty(app.Environment.WebRootPath))
{
    // The library and theme metadata is named ".library", ".theming" and so on,
    // and the default file provider hides dot-prefixed names. None is the only
    // filter that lets them through: on Unix a leading dot is what makes a file
    // hidden, so excluding hidden files excludes exactly these again. wwwroot
    // holds nothing but the UI5 build, and every dot-prefixed name in it is
    // OpenUI5 metadata.
    app.Environment.WebRootFileProvider = new PhysicalFileProvider(
        app.Environment.WebRootPath,
        ExclusionFilters.None
    );
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = ui5ContentTypes });

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program>
namespace Burkard.Invoicing.Api
{
    public partial class Program { }
}
