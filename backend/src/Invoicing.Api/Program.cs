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

// The Vue evaluation frontend (docs/deviations.md D-003) ships in the release
// image as well, on a port of its own: the image sets VueFrontend:Port and has
// Kestrel listen there next to the UI5 port. The port a request arrived on picks
// the frontend. The Host header would not do, since behind a port mapping it
// names the outside port, which the app cannot know. Both ports reach the same
// API, so neither frontend needs a proxy. Without the setting, or without the
// build, nothing changes: the dev stack and the tests only ever see UI5.
var vuePort = app.Configuration.GetValue<int?>("VueFrontend:Port");
var vueRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot-vue");
var vueFiles =
    vuePort is not null && Directory.Exists(vueRoot) ? new PhysicalFileProvider(vueRoot) : null;
bool IsVueRequest(HttpContext context) =>
    vueFiles is not null && context.Connection.LocalPort == vuePort;

app.UseWhen(
    context => !IsVueRequest(context),
    ui5 =>
    {
        ui5.UseDefaultFiles();
        ui5.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = ui5ContentTypes });
    }
);

if (vueFiles is not null)
{
    app.UseWhen(
        IsVueRequest,
        vue =>
        {
            // Vue Router uses HTML5 history, so /invoices/42 is a page rather than
            // a file, and a reload or a bookmark asks the server for it. A request
            // that matched no endpoint and names no file gets index.html, and the
            // router takes it from there. /odata stays out of it, so a wrong API
            // URL still answers 404 instead of a page.
            vue.Use(
                (context, next) =>
                {
                    var request = context.Request;
                    if (
                        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                        && context.GetEndpoint() is null
                        && !Path.HasExtension(request.Path.Value)
                        && !request.Path.StartsWithSegments("/odata")
                    )
                    {
                        request.Path = "/index.html";
                    }
                    return next(context);
                }
            );
            vue.UseStaticFiles(new StaticFileOptions { FileProvider = vueFiles });
        }
    );
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program>
namespace Burkard.Invoicing.Api
{
    public partial class Program { }
}
