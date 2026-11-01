using System.Reflection;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.OData;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Api.Services.Import;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder
    .Services.AddControllers()
    .AddOData(o =>
    {
        // Without this, the no-$-prefix alias for the built-in $search option claims our
        // custom "search" query option (InvoicesController.Get reads it from Request.Query),
        // and OData's own query-option parser then 400s on an empty "search=" before our
        // action code ever runs. The same goes for the per-column options (number,
        // recipient, date, total on Invoices; number, name, address, email on
        // Recipients), which are read from Request.Query too.
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
builder.Services.AddScoped<GraveCorrectionCheck>();
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

// Serve the frontend's build from wwwroot (combined release image). Vue Router
// uses HTML5 history, so /invoices/42 is a page rather than a file, and a
// reload or a bookmark asks the server for it. A request that matched no
// endpoint and names no file gets index.html, and the router takes it from
// there. /odata stays out of it, so a wrong API URL still answers 404 instead
// of a page. Without a build -- the dev stack, the integration tests -- there
// is nothing to serve and nothing changes.
if (!string.IsNullOrEmpty(app.Environment.WebRootPath))
{
    app.Use(
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
    // index.html names the build's hashed files. Left to heuristic caching, a
    // browser could keep an old one after a release and ask for files the new
    // image no longer has: a blank page until a hard reload. So the page is
    // revalidated every time, and the hashed files, which never change under
    // their name, are kept for good.
    app.UseStaticFiles(
        new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        }
    );
}

// The container's health check asks this, so it reads the database rather
// than only proving the process answers; and it says which release runs, which
// is otherwise nowhere to be read.
var version =
    typeof(Program)
        .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0]
    ?? "0.0.0";
app.MapGet(
    "/healthz",
    async (AppDbContext db, CancellationToken ct) =>
    {
        try
        {
            await db.GlobalSettings.AnyAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(
                new { status = "unavailable", version },
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        return Results.Ok(new { status = "ok", version });
    }
);
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program>
namespace Burkard.Invoicing.Api
{
    public partial class Program { }
}
