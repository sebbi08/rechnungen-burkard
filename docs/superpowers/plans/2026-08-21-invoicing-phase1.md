# Invoicing App Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 invoicing web application (Stories 1–9): OpenUI5 frontend + ASP.NET Core OData v4 backend with SQLite, QuestPDF document generation, and half-year grave invoicing.

**Architecture:** Monorepo with `backend/` (ASP.NET Core 10, EF Core + SQLite, OData v4 API, QuestPDF documents, in-process job runner) and `frontend/` (OpenUI5 TypeScript app bound to the OData V4 model). One combined release image; docker-compose provides two dev services.

**Tech Stack:** .NET 10 LTS, ASP.NET Core OData 8.x, EF Core + SQLite, QuestPDF, xUnit + WebApplicationFactory + PdfPig, OpenUI5 (TypeScript, OData V4 model), Playwright, Docker, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-21-invoicing-app-phase1-design.md` (design doc, argued from `spec.md`). The plan argues from the spec — executors read both.

## Global Constraints

- English codebase: code, identifiers, comments, commit messages, docs in English (Principle VII).
- German-only UI via i18n resource bundles; no hard-coded user-visible strings. Backend document texts come from a German `.resx`; API errors use machine-readable codes.
- .NET 10 LTS, OData v4 under `/odata` via Microsoft.AspNetCore.OData 8.x. OpenUI5 only — never SAPUI5.
- SQLite persistence; exactly ONE EF Core migration per released version. During development, amend the existing migration (delete + recreate) instead of adding more; release 1.0.0 ships with exactly one.
- Monetary values: `decimal` columns; all rounding math in integer cents (half-up per VAT group).
- Testing priority: integration + E2E; unit tests only for pure business logic.
- Dev runs in Docker; releases are GitHub Releases + GHCR container images; version bumps automatically (semver).
- Long-running tasks MUST show a blocking progress UI (Story 13 / Principle VIII).
- QuestPDF Community license (`QuestPDF.Settings.License = LicenseType.Community` set once at startup).
- Invoice numbering: `2026-0001`, revisions `2026-0001-R1`, `-R2`, …; year = generation year.

## File Structure

```
backend/
├── Invoicing.sln
├── src/Invoicing.Domain/            # POCO entities + pure services (no EF, no web)
│   ├── Invoicing.Domain.csproj
│   ├── Entities/                    # VatRate, GlobalSettings, InvoiceItem, Recipient,
│   │                                # Grave, GraveItem, GraveRecipient, Invoice, InvoiceLine,
│   │                                # Document, DocumentKind, GraveInvoiceRun, Job, JobStatus
│   ├── InvoiceNumberFormatter.cs    # pure number formatting
│   └── ShareCalculator.cs           # pure cent-splitting
├── src/Invoicing.Documents/         # QuestPDF composers (deterministic, no web)
│   ├── Invoicing.Documents.csproj
│   ├── InvoiceDocumentData.cs       # layout input model
│   ├── InvoiceDocument.cs           # IDocument implementation
│   ├── DocumentService.cs           # GenerateNormal / GenerateCopy → byte[]
│   └── Resources/Documents.resx     # German strings for generated PDFs
├── src/Invoicing.Api/               # ASP.NET Core web app (single combined image serves UI)
│   ├── Invoicing.Api.csproj
│   ├── Program.cs
│   ├── Persistence/AppDbContext.cs
│   ├── Persistence/DatabaseSeeder.cs
│   ├── OData/EdmModelBuilder.cs
│   ├── OData/Controllers/           # SettingsController, VatRatesController, InvoiceItemsController,
│   │                                # RecipientsController, GravesController, GraveItemsController,
│   │                                # GraveRecipientsController, InvoicesController, JobsController,
│   │                                # InvoiceActionsController, DocumentDownloadController,
│   │                                # BulkDownloadController, GraveInvoicingStatusController
│   ├── Services/InvoiceService.cs   # CreateAsync / CreateRevisionAsync
│   ├── Services/InvoiceNumberService.cs
│   ├── Services/DocumentDataBuilder.cs
│   ├── Services/ValidationException.cs
│   └── Jobs/JobQueue.cs, JobRunner.cs, IJobHandler.cs, GraveInvoicesJobHandler.cs
├── tests/Invoicing.IntegrationTests/  # xUnit + WebApplicationFactory against SQLite temp DB
│   ├── ApiTestHost.cs
│   ├── HealthTests.cs, SettingsTests.cs, VatRateTests.cs, InvoiceItemTests.cs,
│   ├── RecipientTests.cs, GraveTests.cs, DocumentTests.cs (service-level via DI),
│   ├── CreateInvoiceTests.cs, RevisionTests.cs, JobTests.cs, GraveGenerationTests.cs,
│   └── BulkDownloadTests.cs
├── tests/Invoicing.DocumentTests/     # xUnit + PdfPig on generated PDF bytes
│   └── InvoiceDocumentTests.cs, CopyStampTests.cs, DeterminismTests.cs
└── dev.Dockerfile
frontend/
├── package.json, ui5.yaml, tsconfig.json
├── src/
│   ├── index.html, manifest.json, Component.ts
│   ├── model/models.ts, model/api.ts        # OData V4 model factory + action fetch helpers
│   ├── controller/App.controller.ts, Settings.controller.ts, Items.controller.ts,
│   │   Recipients.controller.ts, Invoices.controller.ts, Graves.controller.ts
│   ├── view/App.view.xml, Settings.view.xml, Items.view.xml, Recipients.view.xml,
│   │   Invoices.view.xml, Graves.view.xml
│   ├── view/fragments/NewInvoice.fragment.xml, EditInvoice.fragment.xml,
│   │   GenerateGraveInvoices.fragment.xml
│   └── i18n/i18n.properties              # German
e2e/
├── package.json, playwright.config.ts
└── tests/app.spec.ts
docker-compose.yml
Dockerfile                        # combined release image (Task 17)
.github/workflows/ci.yml
.github/workflows/release.yml
```

---

### Task 1: Repository scaffolding, backend projects, dev containers

**Files:**

- Create: `backend/Invoicing.sln`, `backend/src/Invoicing.Domain/Invoicing.Domain.csproj`, `backend/src/Invoicing.Documents/Invoicing.Documents.csproj`, `backend/src/Invoicing.Api/Invoicing.Api.csproj`, `backend/tests/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj`, `backend/tests/Invoicing.DocumentTests/Invoicing.DocumentTests.csproj`, `backend/dev.Dockerfile`, `docker-compose.yml`, `backend/src/Invoicing.Api/Program.cs`, `backend/tests/Invoicing.IntegrationTests/ApiTestHost.cs`, `backend/tests/Invoicing.IntegrationTests/HealthTests.cs`, `backend/.gitignore`
- Create: `.gitignore` (root)

**Interfaces:**

- Produces: `Burkard.Invoicing.Api.Program` (partial, public for `WebApplicationFactory`), endpoint `GET /healthz`, test host class `ApiTestHost : WebApplicationFactory<Program>`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/HealthTests.cs`:

```csharp
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class HealthTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public HealthTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Healthz_returns_ok()
    {
        using var client = _host.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\"", body);
    }
}
```

Create `backend/tests/Invoicing.IntegrationTests/ApiTestHost.cs`:

```csharp
using Burkard.Invoicing.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q` (from repo root)
Expected: FAIL — project does not exist yet ("project could not be restored" or missing `Program`).

- [ ] **Step 3: Create the project files**

`backend/src/Invoicing.Api/Invoicing.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Burkard.Invoicing.Api</RootNamespace>
  </PropertyGroup>
</Project>
```

`backend/src/Invoicing.Domain/Invoicing.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Burkard.Invoicing.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

`backend/src/Invoicing.Documents/Invoicing.Documents.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Burkard.Invoicing.Documents</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Invoicing.Domain/Invoicing.Domain.csproj" />
    <PackageReference Include="QuestPDF" Version="2025.*" />
  </ItemGroup>
</Project>
```

`backend/tests/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Burkard.Invoicing.IntegrationTests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Invoicing.Api/Invoicing.Api.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>
</Project>
```

`backend/tests/Invoicing.DocumentTests/Invoicing.DocumentTests.csproj` (same package pattern, plus references to `Invoicing.Documents` and `Invoicing.Domain`; additionally `<PackageReference Include="PdfPig" Version="0.1.*" />`).

`backend/src/Invoicing.Api/Program.cs`:

```csharp
using Burkard.Invoicing.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program>
public partial class Program
{
}
```

`backend/dev.Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src/backend
COPY backend/ ./
WORKDIR /src/backend/src/Invoicing.Api
CMD ["dotnet", "watch", "run", "--urls", "http://0.0.0.0:8080"]
```

`docker-compose.yml`:

```yaml
services:
  backend:
    build:
      context: .
      dockerfile: backend/dev.Dockerfile
    ports:
      - "8080:8080"
    volumes:
      - ./backend:/src/backend
      - invoicing-data:/data
    environment:
      - ConnectionStrings__Invoicing=Data Source=/data/invoicing.db
      - ASPNETCORE_ENVIRONMENT=Development
volumes:
  invoicing-data:
```

Root `.gitignore` (applies to both `backend/` and `frontend/` builds):

```
bin/
obj/
node_modules/
dist/
*.db
.playwright/
test-results/
```

Create the solution: run `dotnet new sln -n Invoicing -o backend` then `dotnet sln backend/Invoicing.sln add` for each csproj (Api, Domain, Documents, IntegrationTests, DocumentTests).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS (1 test).

- [ ] **Step 5: Verify dev container boots**

Run: `docker compose up --build -d` then `curl -s http://localhost:8080/healthz`
Expected: `{"status":"ok"}`. Then `docker compose down`.

- [ ] **Step 6: Commit**

```bash
git add .gitignore backend docker-compose.yml
git commit -m "chore: scaffold backend solution and dev containers"
```

---

### Task 2: Domain entities, DbContext, single initial migration, settings seed

**Files:**

- Create: `backend/src/Invoicing.Domain/Entities/*.cs` (all entities below), `backend/src/Invoicing.Api/Persistence/AppDbContext.cs`, `backend/src/Invoicing.Api/Persistence/DatabaseSeeder.cs`, `backend/tests/Invoicing.IntegrationTests/SeedTests.cs`
- Modify: `backend/src/Invoicing.Api/Invoicing.Api.csproj` (add EF Core packages + Domain reference), `backend/src/Invoicing.Api/Program.cs` (register DbContext, migrate + seed at startup), `backend/tests/Invoicing.IntegrationTests/ApiTestHost.cs` (SQLite temp DB + migrate on startup)

**Interfaces:**

- Produces: entities `VatRate`, `GlobalSettings`, `InvoiceItem`, `Recipient`, `Grave`, `GraveItem`, `GraveRecipient`, `Invoice`, `InvoiceLine`, `Document` (+enum `DocumentKind { Normal = 0, Copy = 1 }`), `GraveInvoiceRun`, `Job` (+enum `JobStatus { Queued = 0, Running = 1, Succeeded = 2, Failed = 3 }`), all with `long Id`; `AppDbContext` DbSets; unique indexes: `InvoiceItem.Number`, `VatRate.Rate`, `(GraveInvoiceRun.GraveId, Year, Half)`, `(Invoice.Year, Sequence)`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/Invoicing.IntegrationTests/SeedTests.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class SeedTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public SeedTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Startup_migrates_schema_and_seeds_default_settings()
    {
        using var client = _host.CreateClient(); // triggers app startup incl. migrate + seed
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Single(db.GlobalSettings);
        Assert.Equal("Kopie", db.GlobalSettings.Single().StampWord);
        Assert.Equal("", db.GlobalSettings.Single().EndingText);

        // all tables exist (smoke insert/read on one of them)
        db.VatRates.Add(new VatRate { Rate = 7m });
        await db.SaveChangesAsync();
        Assert.Contains(db.VatRates, v => v.Rate == 7m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter SeedTests -v q`
Expected: FAIL (compile error — `AppDbContext`, entities do not exist).

- [ ] **Step 3: Create entities**

Create the POCOs under `backend/src/Invoicing.Domain/Entities/` (namespace `Burkard.Invoicing.Domain.Entities`), one file per aggregate where sensible:

`VatRate.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class VatRate
{
    public long Id { get; set; }
    public decimal Rate { get; set; }
}
```

`GlobalSettings.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class GlobalSettings
{
    public long Id { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "Kopie";
}
```

`InvoiceItem.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class InvoiceItem
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal VatRate { get; set; }
    public decimal? DefaultPrice { get; set; }
}
```

`Recipient.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class Recipient
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}
```

`Grave.cs` + children:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class Grave
{
    public long Id { get; set; }
    public string GraveNumber { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly CreatedAt { get; set; }
    public List<GraveItem> Items { get; set; } = new();
    public List<GraveRecipient> Recipients { get; set; } = new();
}

public sealed class GraveItem
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long ItemId { get; set; }
    public int Count { get; set; }
    public InvoiceItem Item { get; set; } = null!;
}

public sealed class GraveRecipient
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public long RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
}
```

`Invoice.cs` + `InvoiceLine.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class Invoice
{
    public long Id { get; set; }
    public long RootId { get; set; }          // = Id for originals; shared by revisions
    public int RevisionNumber { get; set; }   // 0 = original
    public int Year { get; set; }             // numbering year = generation year
    public int Sequence { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public long? GraveId { get; set; }
    public int? PeriodYear { get; set; }      // invoiced half-year period (grave invoices)
    public int? PeriodHalf { get; set; }
    public decimal? SharePercent { get; set; } // grave split share, e.g. 50 or 33.3333
    public long RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public DateTime GeneratedAt { get; set; } // UTC
    public List<InvoiceLine> Lines { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
}

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long? ItemId { get; set; }
    public string ItemNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Count { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public int LineNumber { get; set; }
}
```

`Document.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public enum DocumentKind
{
    Normal = 0,
    Copy = 1,
}

public sealed class Document
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public DocumentKind Kind { get; set; }
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime GeneratedAt { get; set; }
}
```

`GraveInvoiceRun.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public sealed class GraveInvoiceRun
{
    public long Id { get; set; }
    public long GraveId { get; set; }
    public int Year { get; set; }
    public int Half { get; set; }   // 1 = Jan–Jun, 2 = Jul–Dec
    public long? JobId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

`Job.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class Job
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int ProgressPercent { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
```

- [ ] **Step 4: DbContext, seeding, startup wiring**

`backend/src/Invoicing.Api/Persistence/AppDbContext.cs`:

```csharp
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<VatRate> VatRates => Set<VatRate>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Grave> Graves => Set<Grave>();
    public DbSet<GraveItem> GraveItems => Set<GraveItem>();
    public DbSet<GraveRecipient> GraveRecipients => Set<GraveRecipient>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<GraveInvoiceRun> GraveInvoiceRuns => Set<GraveInvoiceRun>();
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VatRate>().HasIndex(v => v.Rate).IsUnique();
        mb.Entity<InvoiceItem>().HasIndex(i => i.Number).IsUnique();
        mb.Entity<Invoice>().HasIndex(i => new { i.Year, i.Sequence }).IsUnique();
        mb.Entity<Invoice>().HasIndex(i => i.RootId);
        mb.Entity<InvoiceLine>().HasIndex(l => l.InvoiceId);
        mb.Entity<Document>().HasIndex(d => d.InvoiceId);
        mb.Entity<GraveInvoiceRun>().HasIndex(r => new { r.GraveId, r.Year, r.Half }).IsUnique();
        mb.Entity<Document>().Property(d => d.Kind).HasConversion<int>();
        mb.Entity<Job>().Property(j => j.Status).HasConversion<int>();
        // cascade children when their parent goes
        mb.Entity<Grave>().HasMany(g => g.Items).WithOne().HasForeignKey(i => i.GraveId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Grave>().HasMany(g => g.Recipients).WithOne().HasForeignKey(r => r.GraveId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>().HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Invoice>().HasMany(i => i.Documents).WithOne(d => d.Invoice).HasForeignKey(d => d.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`backend/src/Invoicing.Api/Persistence/DatabaseSeeder.cs`:

```csharp
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
```

`Program.cs` becomes:

```csharp
using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

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

public partial class Program
{
}
```

Add to `Invoicing.Api.csproj`:

```xml
<ItemGroup>
    <ProjectReference Include="../Invoicing.Domain/Invoicing.Domain.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets
    >runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
```

Update `ApiTestHost.cs` to use an isolated temp SQLite file:

```csharp
using Burkard.Invoicing.Api;
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;

namespace Burkard.Invoicing.IntegrationTests;

public class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile;

    public ApiTestHost()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbFile)) File.Delete(_dbFile);
    }
}
```

- [ ] **Step 5: Create the single migration and run tests**

Run:

```bash
cd backend
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/Invoicing.Api --startup-project src/Invoicing.Api
dotnet test tests/Invoicing.IntegrationTests -v q
```

Expected: PASS (SeedTests). If you later change any entity/configuration, run `dotnet ef migrations remove --force` and re-add `InitialCreate` — never a second migration in this release.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Invoicing.Domain backend/src/Invoicing.Api backend/tests/Invoicing.IntegrationTests backend/tests/Invoicing.DocumentTests
git commit -m "feat: add domain model and initial EF Core migration"
```

---

### Task 3: OData foundation — EDM model, Settings and VAT rates API

**Files:**

- Create: `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs`, `backend/src/Invoicing.Api/OData/Controllers/SettingsController.cs`, `backend/src/Invoicing.Api/OData/Controllers/VatRatesController.cs`
- Modify: `backend/src/Invoicing.Api/Invoicing.Api.csproj` (add `Microsoft.AspNetCore.OData`), `backend/src/Invoicing.Api/Program.cs` (AddOData + route prefix), `backend/tests/Invoicing.IntegrationTests/ODataJson.cs` (helper), create `SettingsTests.cs`, `VatRateTests.cs`

**Interfaces:**

- Produces: singleton `/odata/Settings` (GET/PATCH, validates non-empty `StampWord`), entity set `/odata/VatRates` (CRUD, validates `Rate > 0`, duplicates rejected 400), error body shape `{"error":{"code":"...","message":"..."}}`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/ODataJson.cs`:

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Burkard.Invoicing.IntegrationTests;

public static class ODataJson
{
    public static StringContent Body(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }
}
```

`SettingsTests.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class SettingsTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public SettingsTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Get_settings_returns_seeded_defaults()
    {
        using var client = _host.CreateClient();
        var response = await client.GetAsync("/odata/Settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("Kopie", json.RootElement.GetProperty("StampWord").GetString());
    }

    [Fact]
    public async Task Patch_empty_stamp_word_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PatchAsync("/odata/Settings", ODataJson.Body(new { StampWord = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("StampWordRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_ending_text_preserves_line_breaks()
    {
        using var client = _host.CreateClient();
        const string text = "Line one\nLine two";
        var response = await client.PatchAsync("/odata/Settings", ODataJson.Body(new { EndingText = text }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = await client.GetAsync("/odata/Settings");
        using var json = await ODataJson.ReadAsync(get);
        Assert.Equal(text, json.RootElement.GetProperty("EndingText").GetString());
    }
}
```

`VatRateTests.cs`:

```csharp
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class VatRateTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public VatRateTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Post_vat_rate_creates_and_duplicate_is_rejected()
    {
        using var client = _host.CreateClient();
        var created = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("VatRateExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_negative_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = -1m }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter "SettingsTests|VatRateTests" -v q`
Expected: FAIL — 404, `/odata/Settings` does not exist.

- [ ] **Step 3: Register OData and implement controllers**

Add to `Invoicing.Api.csproj`: `<PackageReference Include="Microsoft.AspNetCore.OData" Version="8.*" />`.

`OData/EdmModelBuilder.cs`:

```csharp
using Burkard.Invoicing.Domain.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Burkard.Invoicing.Api.OData;

public static class EdmModelBuilder
{
    public static IEdmModel Build()
    {
        var b = new ODataConventionModelBuilder();
        b.EntitySet<VatRate>("VatRates");
        b.EntitySet<InvoiceItem>("InvoiceItems");
        b.EntitySet<Recipient>("Recipients");
        b.EntitySet<Grave>("Graves");
        b.EntitySet<GraveItem>("GraveItems");
        b.EntitySet<GraveRecipient>("GraveRecipients");
        b.EntitySet<Invoice>("Invoices");
        b.EntitySet<Document>("Documents");
        b.EntitySet<Job>("Jobs");
        b.Singleton<GlobalSettings>("Settings");
        return b.GetEdmModel();
    }
}
```

`Program.cs` — replace `builder.Services.AddControllers();` with:

```csharp
builder.Services.AddControllers().AddOData(o =>
    o.Select().Filter().OrderBy().Count().SetMaxTop(200)
     .AddRouteComponents("odata", EdmModelBuilder.Build()));
```

`OData/Controllers/SettingsController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class SettingsController : ODataController
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db) => _db = db;

    public ActionResult<GlobalSettings> Get()
    {
        var settings = _db.GlobalSettings.FirstOrDefault()
            ?? throw new InvalidOperationException("GlobalSettings not seeded.");
        return Ok(settings);
    }

    public IActionResult Patch([FromBody] Delta<GlobalSettings> delta)
    {
        var settings = _db.GlobalSettings.FirstOrDefault()
            ?? throw new InvalidOperationException("GlobalSettings not seeded.");

        if (delta.TryGetPropertyValue(nameof(GlobalSettings.StampWord), out var stamp)
            && string.IsNullOrWhiteSpace(stamp as string))
        {
            return BadRequest(new
            {
                error = new { code = "StampWordRequired", message = "The stamp word must not be empty." }
            });
        }

        delta.Patch(settings);
        _db.SaveChanges();
        return Updated(settings);
    }
}
```

`OData/Controllers/VatRatesController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class VatRatesController : ODataController
{
    private readonly AppDbContext _db;

    public VatRatesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<VatRate> Get() => _db.VatRates.AsNoTracking();

    public ActionResult<VatRate> Get([FromRoute] long key)
    {
        var rate = _db.VatRates.Find(key);
        return rate is null ? NotFound() : Ok(rate);
    }

    public IActionResult Post([FromBody] VatRate rate)
    {
        if (rate.Rate <= 0)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidVatRate", message = "The VAT rate must be positive." }
            });
        }

        if (_db.VatRates.Any(v => v.Rate == rate.Rate))
        {
            return BadRequest(new
            {
                error = new { code = "VatRateExists", message = "This VAT rate is already configured." }
            });
        }

        _db.VatRates.Add(rate);
        _db.SaveChanges();
        return Created(rate);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var rate = _db.VatRates.Find(key);
        if (rate is null) return NotFound();
        _db.VatRates.Remove(rate);
        _db.SaveChanges();
        return NoContent();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS (all tests incl. earlier ones).

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: OData foundation with settings and VAT rate endpoints"
```

---

### Task 4: Invoice items and recipients API

**Files:**

- Create: `backend/src/Invoicing.Api/OData/Controllers/InvoiceItemsController.cs`, `backend/src/Invoicing.Api/OData/Controllers/RecipientsController.cs`, `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs`, `backend/tests/Invoicing.IntegrationTests/RecipientTests.cs`

**Interfaces:**

- Produces: `/odata/InvoiceItems` CRUD (validates non-empty `Number`, non-empty `Description`, `VatRate` must be a configured rate); `/odata/Recipients` CRUD (validates non-empty `Name` and `Address`).

- [ ] **Step 1: Write the failing tests**

`InvoiceItemTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceItemTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public InvoiceItemTests(ApiTestHost host) => _host = host;

    private async Task SeedVatRateAsync(HttpClient client) =>
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));

    [Fact]
    public async Task Create_item_without_default_price_stores_null_price()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);

        var response = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "1001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = (decimal?)null,
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("DefaultPrice").ValueKind);
    }

    [Fact]
    public async Task Create_item_with_unknown_vat_rate_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "1002",
            Description = "Unknown rate",
            VatRate = 99m,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("VatRateNotConfigured", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_item_with_duplicate_number_is_rejected()
    {
        using var client = _host.CreateClient();
        await SeedVatRateAsync(client);
        var first = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "2001",
            Description = "First",
            VatRate = 7.0m,
        }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "2001",
            Description = "Second",
            VatRate = 7.0m,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var json = await ODataJson.ReadAsync(duplicate);
        Assert.Equal("ItemNumberExists", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
```

`RecipientTests.cs`:

```csharp
using System.Net;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RecipientTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public RecipientTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Create_recipient_and_query_with_filter()
    {
        using var client = _host.CreateClient();
        var created = await client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Familie Mahr",
            Address = "Musterstraße 1\n12345 Musterstadt",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await client.GetAsync("/odata/Recipients?$filter=contains(Name,'Mahr')");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = await ODataJson.ReadAsync(list);
        var names = json.RootElement.GetProperty("value").EnumerateArray()
            .Select(e => e.GetProperty("Name").GetString()).ToArray();
        Assert.Contains("Familie Mahr", names);
    }

    [Fact]
    public async Task Create_recipient_without_address_is_rejected()
    {
        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/Recipients", ODataJson.Body(new { Name = "No Address" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("RecipientAddressRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter "InvoiceItemTests|RecipientTests" -v q`
Expected: FAIL — 404 on `/odata/InvoiceItems` and `/odata/Recipients`.

- [ ] **Step 3: Implement controllers**

`InvoiceItemsController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class InvoiceItemsController : ODataController
{
    private readonly AppDbContext _db;

    public InvoiceItemsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<InvoiceItem> Get() => _db.InvoiceItems.AsNoTracking();

    public ActionResult<InvoiceItem> Get([FromRoute] long key)
    {
        var item = _db.InvoiceItems.Find(key);
        return item is null ? NotFound() : Ok(item);
    }

    public IActionResult Post([FromBody] InvoiceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Number))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNumberRequired", message = "The item number is required." }
            });
        }

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            return BadRequest(new
            {
                error = new { code = "ItemDescriptionRequired", message = "The description is required." }
            });
        }

        if (!_db.VatRates.Any(v => v.Rate == item.VatRate))
        {
            return BadRequest(new
            {
                error = new { code = "VatRateNotConfigured", message = "The VAT rate is not configured." }
            });
        }

        if (_db.InvoiceItems.Any(i => i.Number == item.Number))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNumberExists", message = "This item number already exists." }
            });
        }

        _db.InvoiceItems.Add(item);
        _db.SaveChanges();
        return Created(item);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Microsoft.AspNetCore.OData.Deltas.Delta<InvoiceItem> delta)
    {
        var item = _db.InvoiceItems.Find(key);
        if (item is null) return NotFound();

        delta.Patch(item);

        if (string.IsNullOrWhiteSpace(item.Number)
            || !_db.VatRates.Any(v => v.Rate == item.VatRate)
            || _db.InvoiceItems.Any(i => i.Number == item.Number && i.Id != item.Id))
        {
            return BadRequest(new
            {
                error = new { code = "InvalidItem", message = "Number, description and VAT rate are required." }
            });
        }

        _db.SaveChanges();
        return Updated(item);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var item = _db.InvoiceItems.Find(key);
        if (item is null) return NotFound();
        _db.InvoiceItems.Remove(item);
        _db.SaveChanges();
        return NoContent();
    }
}
```

`RecipientsController.cs` (same shape, with validation):

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class RecipientsController : ODataController
{
    private readonly AppDbContext _db;

    public RecipientsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Recipient> Get() => _db.Recipients.AsNoTracking();

    public ActionResult<Recipient> Get([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        return recipient is null ? NotFound() : Ok(recipient);
    }

    public IActionResult Post([FromBody] Recipient recipient)
    {
        var error = Validate(recipient);
        if (error is not null) return BadRequest(error);

        _db.Recipients.Add(recipient);
        _db.SaveChanges();
        return Created(recipient);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Delta<Recipient> delta)
    {
        var recipient = _db.Recipients.Find(key);
        if (recipient is null) return NotFound();

        delta.Patch(recipient);
        var error = Validate(recipient);
        if (error is not null) return BadRequest(error);

        _db.SaveChanges();
        return Updated(recipient);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var recipient = _db.Recipients.Find(key);
        if (recipient is null) return NotFound();
        _db.Recipients.Remove(recipient);
        _db.SaveChanges();
        return NoContent();
    }

    private static object? Validate(Recipient r)
    {
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            return new { error = new { code = "RecipientNameRequired", message = "The name is required." } };
        }

        if (string.IsNullOrWhiteSpace(r.Address))
        {
            return new { error = new { code = "RecipientAddressRequired", message = "The address is required." } };
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: invoice item and recipient endpoints"
```

---

### Task 5: Graves API (with items, recipients, and search)

**Files:**

- Create: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, `backend/src/Invoicing.Api/OData/Controllers/GraveItemsController.cs`, `backend/src/Invoicing.Api/OData/Controllers/GraveRecipientsController.cs`, `backend/tests/Invoicing.IntegrationTests/GraveTests.cs`

**Interfaces:**

- Produces: `/odata/Graves` CRUD (validates non-empty `GraveNumber`), `/odata/GraveItems` and `/odata/GraveRecipients` CRUD for links (validate referenced grave exists; validate grave-item count > 0), and search endpoint `GET /odata/SearchGraves?term=...` returning JSON `{ "value": [ { "Id", "GraveNumber", "RecipientNames": [...] } ] }` matching grave number or any recipient name (case-insensitive).

- [ ] **Step 1: Write the failing test**

`GraveTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public GraveTests(ApiTestHost host) => _host = host;

    private async Task<(long ItemId, long RecipientId)> SeedAsync(HttpClient client)
    {
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 7.0m }));
        var item = await client.PostAsync("/odata/InvoiceItems", ODataJson.Body(new
        {
            Number = "3001",
            Description = "Grabpflege",
            VatRate = 7.0m,
            DefaultPrice = 120.00m,
        }));
        using var itemJson = await ODataJson.ReadAsync(item);
        var recipient = await client.PostAsync("/odata/Recipients", ODataJson.Body(new
        {
            Name = "Anna Beispiel",
            Address = "Hauptstraße 1\n99999 Musterdorf",
        }));
        using var recipientJson = await ODataJson.ReadAsync(recipient);
        return (itemJson.RootElement.GetProperty("Id").GetInt64(),
                recipientJson.RootElement.GetProperty("Id").GetInt64());
    }

    [Fact]
    public async Task Create_grave_with_item_and_recipient_and_search_by_number()
    {
        using var client = _host.CreateClient();
        var (itemId, recipientId) = await SeedAsync(client);

        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "A-001",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        Assert.Equal(HttpStatusCode.Created, grave.StatusCode);
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();
        Assert.NotEqual(default, graveJson.RootElement.GetProperty("CreatedAt").GetString());

        var linkItem = await client.PostAsync("/odata/GraveItems", ODataJson.Body(new
        {
            GraveId = graveId,
            ItemId = itemId,
            Count = 2,
        }));
        Assert.Equal(HttpStatusCode.Created, linkItem.StatusCode);

        var linkRecipient = await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new
        {
            GraveId = graveId,
            RecipientId = recipientId,
        }));
        Assert.Equal(HttpStatusCode.Created, linkRecipient.StatusCode);

        var search = await client.GetAsync("/odata/SearchGraves?term=A-001");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Single(searchJson.RootElement.GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task Search_graves_by_recipient_name_matches_case_insensitively()
    {
        using var client = _host.CreateClient();
        var (_, recipientId) = await SeedAsync(client);

        await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "B-002",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        // fetch grave id from listing
        var list = await client.GetAsync("/odata/Graves?$filter=GraveNumber eq 'B-002'");
        using var listJson = await ODataJson.ReadAsync(list);
        var graveId = listJson.RootElement.GetProperty("value")[0].GetProperty("Id").GetInt64();
        await client.PostAsync("/odata/GraveRecipients", ODataJson.Body(new { GraveId = graveId, RecipientId = recipientId }));

        var search = await client.GetAsync("/odata/SearchGraves?term=beispiel");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = await ODataJson.ReadAsync(search);
        Assert.Single(searchJson.RootElement.GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task Create_grave_item_with_zero_count_is_rejected()
    {
        using var client = _host.CreateClient();
        var (itemId, _) = await SeedAsync(client);
        var grave = await client.PostAsync("/odata/Graves", ODataJson.Body(new
        {
            GraveNumber = "C-003",
            StartDate = "2026-01-01",
            EndDate = "2040-12-31",
        }));
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();

        var link = await client.PostAsync("/odata/GraveItems", ODataJson.Body(new
        {
            GraveId = graveId,
            ItemId = itemId,
            Count = 0,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter GraveTests -v q`
Expected: FAIL — 404 on `/odata/Graves` and `/odata/SearchGraves`.

- [ ] **Step 3: Implement controllers**

`GravesController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GravesController : ODataController
{
    private readonly AppDbContext _db;

    public GravesController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Grave> Get() => _db.Graves.AsNoTracking();

    public ActionResult<Grave> Get([FromRoute] long key)
    {
        var grave = _db.Graves.Find(key);
        return grave is null ? NotFound() : Ok(grave);
    }

    public IActionResult Post([FromBody] Grave grave)
    {
        if (string.IsNullOrWhiteSpace(grave.GraveNumber))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNumberRequired", message = "The grave number is required." }
            });
        }

        grave.CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.Graves.Add(grave);
        _db.SaveChanges();
        return Created(grave);
    }

    public IActionResult Patch([FromRoute] long key, [FromBody] Microsoft.AspNetCore.OData.Deltas.Delta<Grave> delta)
    {
        var grave = _db.Graves.Find(key);
        if (grave is null) return NotFound();
        delta.Patch(grave);
        if (string.IsNullOrWhiteSpace(grave.GraveNumber))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNumberRequired", message = "The grave number is required." }
            });
        }

        _db.SaveChanges();
        return Updated(grave);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var grave = _db.Graves.Find(key);
        if (grave is null) return NotFound();
        _db.Graves.Remove(grave); // cascades to GraveItems/GraveRecipients
        _db.SaveChanges();
        return NoContent();
    }
}
```

`GraveItemsController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class GraveItemsController : ODataController
{
    private readonly AppDbContext _db;

    public GraveItemsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<GraveItem> Get() => _db.GraveItems.AsNoTracking();

    public IActionResult Post([FromBody] GraveItem link)
    {
        if (!_db.Graves.Any(g => g.Id == link.GraveId))
        {
            return BadRequest(new
            {
                error = new { code = "GraveNotFound", message = "The grave does not exist." }
            });
        }

        if (!_db.InvoiceItems.Any(i => i.Id == link.ItemId))
        {
            return BadRequest(new
            {
                error = new { code = "ItemNotFound", message = "The item does not exist." }
            });
        }

        if (link.Count <= 0)
        {
            return BadRequest(new
            {
                error = new { code = "InvalidCount", message = "The count must be positive." }
            });
        }

        _db.GraveItems.Add(link);
        _db.SaveChanges();
        return Created(link);
    }

    public IActionResult Delete([FromRoute] long key)
    {
        var link = _db.GraveItems.Find(key);
        if (link is null) return NotFound();
        _db.GraveItems.Remove(link);
        _db.SaveChanges();
        return NoContent();
    }
}
```

`GraveRecipientsController.cs` (same shape; validates grave and recipient exist; no count check).

`SearchGraves` endpoint — add to `GravesController` (attribute-routed, documented OData 8 pattern):

```csharp
using Microsoft.AspNetCore.OData.Routing.Attributes;

// inside GravesController
[ODataAttributeRouting]
[HttpGet("odata/SearchGraves")]
public async Task<IActionResult> Search([FromQuery] string term)
{
    if (string.IsNullOrWhiteSpace(term))
    {
        return Ok(new { value = Array.Empty<object>() });
    }

    var lowered = term.ToLowerInvariant();
    var graves = await _db.Graves
        .Where(g => g.GraveNumber.ToLower().Contains(lowered)
                    || g.Recipients.Any(r => r.Recipient.Name.ToLower().Contains(lowered)))
        .Select(g => new
        {
            g.Id,
            g.GraveNumber,
            RecipientNames = g.Recipients.Select(r => r.Recipient.Name).ToList(),
        })
        .ToListAsync();

    return Ok(new { value = graves });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: grave management endpoints with links and search"
```

---

### Task 6: Document generation (QuestPDF composers) and document tests

**Files:**

- Create: `backend/src/Invoicing.Documents/InvoiceDocumentData.cs`, `backend/src/Invoicing.Documents/InvoiceDocument.cs`, `backend/src/Invoicing.Documents/DocumentService.cs`, `backend/src/Invoicing.Documents/DocumentTexts.cs`, `backend/tests/Invoicing.DocumentTests/InvoiceDocumentTests.cs`, `backend/tests/Invoicing.DocumentTests/CopyStampTests.cs`, `backend/tests/Invoicing.DocumentTests/DeterminismTests.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (set QuestPDF license)

**Interfaces:**

- Produces: `DocumentService.GeneratePdf(InvoiceDocumentData data, bool isCopy) → byte[]` (deterministic); `InvoiceDocumentData` model with `InvoiceNumber`, `GeneratedAt` (UTC), `RecipientName`, `RecipientAddress`, `ShareText` (nullable), `EndingText`, `StampWord`, `Lines` (`LineData` with `Position`, `ItemNumber`, `Description`, `Count`, `UnitPrice`, `VatRate`, `LineTotal`, `VatAmount`), `VatSummaries` (per-rate `NetTotal`, `VatAmount`, `GrossTotal`), `NetTotal`, `TotalVat`, `GrossTotal`.

- [ ] **Step 1: Write the failing tests**

`backend/tests/Invoicing.DocumentTests/InvoiceDocumentTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class InvoiceDocumentTests
{
    private static InvoiceDocumentData SampleData() => new()
    {
        InvoiceNumber = "2026-0001",
        GeneratedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        RecipientName = "Familie Mahr",
        RecipientAddress = "Musterstraße 1\n12345 Musterstadt",
        EndingText = "Vielen Dank!\nBitte überweisen Sie den Betrag.",
        StampWord = "Kopie",
        Lines = new[]
        {
            new InvoiceDocumentData.LineData(1, "1001", "Grabpflege", 2, 120.00m, 7.0m, 240.00m, 16.80m),
        },
        VatSummaries = new[] { new InvoiceDocumentData.VatSummary(7.0m, 240.00m, 16.80m, 256.80m) },
        NetTotal = 240.00m,
        TotalVat = 16.80m,
        GrossTotal = 256.80m,
    };

    private static string ExtractText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void Normal_invoice_contains_number_lines_totals_and_ending_text()
    {
        var pdf = DocumentService.GeneratePdf(SampleData(), isCopy: false);
        var text = ExtractText(pdf);

        Assert.Contains("2026-0001", text);
        Assert.Contains("Familie Mahr", text);
        Assert.Contains("Musterstraße 1", text);
        Assert.Contains("Grabpflege", text);
        Assert.Contains("240,00", text);      // line total, German number format
        Assert.Contains("256,80", text);      // gross total
        Assert.Contains("Vielen Dank!", text);
        Assert.Contains("Bitte überweisen Sie den Betrag.", text);
        Assert.DoesNotContain("Kopie", text); // normal has no stamp
    }

    [Fact]
    public void Share_text_is_printed_above_the_lines()
    {
        var data = SampleData();
        data.ShareText = "Diese Rechnung enthält 50 % des Gesamtbetrags.";
        var pdf = DocumentService.GeneratePdf(data, isCopy: false);
        var text = ExtractText(pdf);

        Assert.Contains("Diese Rechnung enthält 50 % des Gesamtbetrags.", text);
        Assert.True(text.IndexOf("50 % des Gesamtbetrags", StringComparison.Ordinal)
                    < text.IndexOf("Grabpflege", StringComparison.Ordinal));
    }
}
```

`CopyStampTests.cs`:

```csharp
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class CopyStampTests
{
    // reuse a multi-page sample: many lines force > 1 page
    private static InvoiceDocumentData MultiPageData() => new()
    {
        InvoiceNumber = "2026-0002",
        GeneratedAt = new System.DateTime(2026, 2, 1, 0, 0, 0, System.DateTimeKind.Utc),
        RecipientName = "Anna Beispiel",
        RecipientAddress = "Hauptstraße 1\n99999 Musterdorf",
        EndingText = "",
        StampWord = "Kopie",
        Lines = Enumerable.Range(1, 80).Select(i => new InvoiceDocumentData.LineData(
            i, $"I-{i}", $"Leistung {i}", 1, 10.00m, 19.0m, 10.00m, 1.90m)).ToArray(),
        VatSummaries = new[] { new InvoiceDocumentData.VatSummary(19.0m, 800.00m, 152.00m, 952.00m) },
        NetTotal = 800.00m,
        TotalVat = 152.00m,
        GrossTotal = 952.00m,
    };

    [Fact]
    public void Copy_stamp_word_appears_on_every_page()
    {
        var pdf = DocumentService.GeneratePdf(MultiPageData(), isCopy: true);
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);

        var pages = document.GetPages().ToList();
        Assert.True(pages.Count > 1, "sample should produce multiple pages");
        foreach (var page in pages)
        {
            Assert.Contains("Kopie", page.Text);
        }
    }

    [Fact]
    public void Very_long_stamp_word_still_generates_and_appears_on_every_page()
    {
        var data = MultiPageData();
        data.StampWord = new string('K', 80);

        var pdf = DocumentService.GeneratePdf(data, isCopy: true);
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);

        foreach (var page in document.GetPages())
        {
            Assert.Contains('K', page.Text);
        }
    }
}
```

`DeterminismTests.cs`:

```csharp
using System;
using Burkard.Invoicing.Documents;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

public class DeterminismTests
{
    [Fact]
    public void Same_input_produces_byte_identical_pdf()
    {
        var data = new InvoiceDocumentData
        {
            InvoiceNumber = "2026-0003",
            GeneratedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            RecipientName = "Test",
            RecipientAddress = "Testweg 1",
            EndingText = "Danke",
            StampWord = "Kopie",
            Lines = new[] { new InvoiceDocumentData.LineData(1, "A", "Leistung", 1, 5.00m, 7.0m, 5.00m, 0.35m) },
            VatSummaries = new[] { new InvoiceDocumentData.VatSummary(7.0m, 5.00m, 0.35m, 5.35m) },
            NetTotal = 5.00m,
            TotalVat = 0.35m,
            GrossTotal = 5.35m,
        };

        var first = DocumentService.GeneratePdf(data, isCopy: true);
        var second = DocumentService.GeneratePdf(data, isCopy: true);

        Assert.Equal(first, second);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.DocumentTests -v q`
Expected: FAIL (compile error — `DocumentService` does not exist).

- [ ] **Step 3: Implement the composers**

`DocumentTexts.cs` (the single place for German document strings — the generated PDF is a German document; UI languages do not affect it):

```csharp
using System.Globalization;

namespace Burkard.Invoicing.Documents;

public static class DocumentTexts
{
    public static string ShareText(decimal percent) =>
        string.Create(CultureInfo.GetCultureInfo("de-DE"),
            $"Diese Rechnung enthält {percent:0.##} % des Gesamtbetrags.");

    public static string InvoiceTitle(string invoiceNumber) => $"Rechnung {invoiceNumber}";
    public static string ColumnDescription => "Bezeichnung";
    public static string ColumnCount => "Anzahl";
    public static string ColumnUnitPrice => "Einzelpreis";
    public static string ColumnVat => "MwSt.";
    public static string ColumnTotal => "Summe";
    public static string NetForRate(decimal rate, decimal amount) => $"Netto {rate:0.#} %: {amount:N2}";
    public static string VatForRate(decimal rate, decimal amount) => $"MwSt. {rate:0.#} %: {amount:N2}";
    public static string NetTotal(decimal amount) => $"Netto gesamt: {amount:N2}";
    public static string VatTotal(decimal amount) => $"MwSt. gesamt: {amount:N2}";
    public static string GrossTotal(decimal amount) => $"Gesamtbetrag: {amount:N2}";
    public static string PageFooterPageOf => "Seite";
}
```

`InvoiceDocumentData.cs`:

```csharp
namespace Burkard.Invoicing.Documents;

public sealed class InvoiceDocumentData
{
    public string InvoiceNumber { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? ShareText { get; set; }
    public string EndingText { get; set; } = "";
    public string StampWord { get; set; } = "";
    public IReadOnlyList<LineData> Lines { get; set; } = Array.Empty<LineData>();
    public IReadOnlyList<VatSummary> VatSummaries { get; set; } = Array.Empty<VatSummary>();
    public decimal NetTotal { get; set; }
    public decimal TotalVat { get; set; }
    public decimal GrossTotal { get; set; }

    public sealed record LineData(int Position, string ItemNumber, string Description,
        int Count, decimal UnitPrice, decimal VatRate, decimal LineTotal, decimal VatAmount);

    public sealed record VatSummary(decimal Rate, decimal NetTotal, decimal VatAmount, decimal GrossTotal);
}
```

`InvoiceDocument.cs` (uses invariant `ToString("N2")` for amounts — German formatting):

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Burkard.Invoicing.Documents;

internal sealed class InvoiceDocument : IDocument
{
    private readonly InvoiceDocumentData _data;
    private readonly bool _isCopy;

    public InvoiceDocument(InvoiceDocumentData data, bool isCopy)
    {
        _data = data;
        _isCopy = isCopy;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Invoice {_data.InvoiceNumber}",
        Author = "Burkard Invoicing",
        Creator = "Burkard Invoicing",
        Producer = "QuestPDF",
        CreationDate = _data.GeneratedAt,
        ModifiedDate = _data.GeneratedAt,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            if (_isCopy)
            {
                // scale the stamp down for very long words so it always fits
                var stampFontSize = Math.Clamp(48f * 20 / Math.Max(_data.StampWord.Length, 1), 24f, 72f);
                page.Foreground()
                    .Rotate(45)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(_data.StampWord)
                    .FontSize(stampFontSize)
                    .FontColor(Colors.Grey.Lighten2.WithAlpha(160));
            }

            page.Header().Column(column =>
            {
                column.Item().Text(DocumentTexts.InvoiceTitle(_data.InvoiceNumber)).FontSize(18).SemiBold();
                column.Item().Text(_data.GeneratedAt.ToString("dd.MM.yyyy"));
                column.Item().PaddingTop(10).Text(_data.RecipientName).SemiBold();
                column.Item().Text(_data.RecipientAddress);
            });

            page.Content().Column(column =>
            {
                if (!string.IsNullOrEmpty(_data.ShareText))
                {
                    column.Item().PaddingBottom(8).Text(_data.ShareText).SemiBold();
                }

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(80);
                        cols.ConstantColumn(50);
                        cols.ConstantColumn(80);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text(DocumentTexts.ColumnDescription);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnCount);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnUnitPrice);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnVat);
                        header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnTotal);
                    });

                    foreach (var line in _data.Lines)
                    {
                        table.Cell().Text(line.Description);
                        table.Cell().AlignRight().Text(line.Count.ToString());
                        table.Cell().AlignRight().Text(Money(line.UnitPrice));
                        table.Cell().AlignRight().Text($"{line.VatRate.ToString("0.#")} %");
                        table.Cell().AlignRight().Text(Money(line.LineTotal));
                    }
                });

                column.Item().PaddingTop(10).AlignRight().Column(summary =>
                {
                    foreach (var vat in _data.VatSummaries)
                    {
                        summary.Item().Text(DocumentTexts.NetForRate(vat.Rate, vat.NetTotal));
                        summary.Item().Text(DocumentTexts.VatForRate(vat.Rate, vat.VatAmount));
                    }

                    summary.Item().PaddingTop(6).Text(DocumentTexts.NetTotal(_data.NetTotal)).SemiBold();
                    summary.Item().Text(DocumentTexts.VatTotal(_data.TotalVat)).SemiBold();
                    summary.Item().Text(DocumentTexts.GrossTotal(_data.GrossTotal)).SemiBold().FontSize(13);
                });

                if (!string.IsNullOrEmpty(_data.EndingText))
                {
                    column.Item().PaddingTop(16).Text(_data.EndingText);
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(DocumentTexts.PageFooterPageOf + " ");
                text.CurrentPageNumber();
                text.Span(" von ");
                text.TotalPages();
            });
        });
    }

    private static string Money(decimal value) => value.ToString("N2");

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);
}
```

`DocumentService.cs`:

```csharp
using QuestPDF.Fluent;

namespace Burkard.Invoicing.Documents;

public static class DocumentService
{
    public static byte[] GeneratePdf(InvoiceDocumentData data, bool isCopy)
    {
        var document = new InvoiceDocument(data, isCopy);
        return document.GeneratePdf();
    }
}
```

Note: all German strings live in `DocumentTexts` (single source, no resx machinery needed for one language). The PDF is a German document; its labels do not change with the UI language.

In `Program.cs` (Api), before building the app:

```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

Add `<ProjectReference Include="../../src/Invoicing.Documents/Invoicing.Documents.csproj" />` to `Invoicing.Api.csproj`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.DocumentTests -v q`
Expected: PASS. If the determinism test fails with differing bytes, the metadata is not the cause — check for nondeterministic defaults (do NOT add timestamps or GUIDs to the layout); with the fixed `CreationDate`/`ModifiedDate` this must hold.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Documents backend/src/Invoicing.Api backend/tests/Invoicing.DocumentTests
git commit -m "feat: deterministic QuestPDF invoice and copy generation"
```

---

### Task 7: Invoice number service (pure formatting + allocation)

**Files:**

- Create: `backend/src/Invoicing.Domain/InvoiceNumberFormatter.cs`, `backend/src/Invoicing.Api/Services/InvoiceNumberService.cs`, `backend/tests/Invoicing.DocumentTests/../..` — put unit tests in `backend/tests/Invoicing.IntegrationTests/InvoiceNumberFormatterTests.cs` (pure logic only; no host needed)

**Interfaces:**

- Produces: `InvoiceNumberFormatter.ForOriginal(int year, int sequence) → "2026-0001"`; `InvoiceNumberFormatter.ForRevision(string baseNumber, int revisionNumber) → "2026-0001-R1"`; `InvoiceNumberFormatter.StripRevision(string number) → "2026-0001"`; `InvoiceNumberService.AllocateAsync(CancellationToken) → (string Number, int Year, int Sequence)` — reads max sequence per year, caller inserts the invoice inside its transaction; unique index guards races.

- [ ] **Step 1: Write the failing unit tests**

`backend/tests/Invoicing.IntegrationTests/InvoiceNumberFormatterTests.cs`:

```csharp
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class InvoiceNumberFormatterTests
{
    [Fact]
    public void Original_number_is_year_dash_sequence_padded_to_4()
    {
        Assert.Equal("2026-0001", InvoiceNumberFormatter.ForOriginal(2026, 1));
        Assert.Equal("2026-0042", InvoiceNumberFormatter.ForOriginal(2026, 42));
    }

    [Fact]
    public void Revision_number_appends_R_suffix()
    {
        Assert.Equal("2026-0001-R1", InvoiceNumberFormatter.ForRevision("2026-0001", 1));
        Assert.Equal("2026-0001-R2", InvoiceNumberFormatter.ForRevision("2026-0001", 2));
    }

    [Fact]
    public void Strip_revision_removes_suffix()
    {
        Assert.Equal("2026-0001", InvoiceNumberFormatter.StripRevision("2026-0001-R2"));
        Assert.Equal("2026-0001", InvoiceNumberFormatter.StripRevision("2026-0001"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter InvoiceNumberFormatterTests -v q`
Expected: FAIL (compile error — type missing).

- [ ] **Step 3: Implement**

`backend/src/Invoicing.Domain/InvoiceNumberFormatter.cs`:

```csharp
using System.Globalization;

namespace Burkard.Invoicing.Domain;

public static class InvoiceNumberFormatter
{
    private const string RevisionMarker = "-R";

    public static string ForOriginal(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}-{sequence:D4}");

    public static string ForRevision(string baseNumber, int revisionNumber) =>
        $"{baseNumber}{RevisionMarker}{revisionNumber}";

    public static string StripRevision(string number)
    {
        var index = number.LastIndexOf(RevisionMarker, StringComparison.Ordinal);
        return index >= 0 ? number[..index] : number;
    }
}
```

`backend/src/Invoicing.Api/Services/InvoiceNumberService.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceNumberService
{
    private readonly AppDbContext _db;

    public InvoiceNumberService(AppDbContext db) => _db = db;

    /// <summary>
    /// Computes the next number for the current year. The caller inserts the
    /// Invoice row inside its own transaction; the unique (Year, Sequence)
    /// index makes concurrent allocations fail loudly (retry in caller).
    /// </summary>
    public async Task<(string Number, int Year, int Sequence)> AllocateAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var maxSequence = await _db.Invoices
            .Where(i => i.Year == year)
            .MaxAsync(i => (int?)i.Sequence, ct) ?? 0;
        var sequence = maxSequence + 1;
        return (InvoiceNumberFormatter.ForOriginal(year, sequence), year, sequence);
    }
}
```

Register in `Program.cs`: `builder.Services.AddScoped<InvoiceNumberService>();` (and later `InvoiceService`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter InvoiceNumberFormatterTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: invoice number formatting and allocation service"
```

---

### Task 8: CreateInvoice action — snapshots, atomic document pair

**Files:**

- Create: `backend/src/Invoicing.Api/Services/ValidationException.cs`, `backend/src/Invoicing.Api/Services/InvoiceService.cs`, `backend/src/Invoicing.Api/Services/DocumentDataBuilder.cs`, `backend/src/Invoicing.Api/Services/DocumentGenerator.cs`, `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs`, `backend/tests/Invoicing.IntegrationTests/CreateInvoiceTests.cs`
- Modify: `backend/src/Invoicing.Api/OData/EdmModelBuilder.cs` (declare action `CreateInvoice`), `backend/src/Invoicing.Api/Program.cs` (register `InvoiceService`, `DocumentDataBuilder`, `IDocumentGenerator`)

**Interfaces:**

- Consumes: `AppDbContext`, `InvoiceNumberService`, `DocumentService.GeneratePdf`, `InvoiceNumberFormatter`.
- Produces: `IDocumentGenerator { byte[] Generate(InvoiceDocumentData data, bool isCopy); }` + default implementation `QuestPdfDocumentGenerator`; `InvoiceService.CreateAsync(long recipientId, IReadOnlyList<InvoiceLineInput> lines, CancellationToken) → Invoice` (validates, snapshots recipient/ending text/stamp word, allocates number, generates normal+copy in one transaction; throws `ValidationException(code, message)`); `InvoiceLineInput { ItemId, Count, UnitPrice? }`; endpoint `POST /odata/CreateInvoice` with body `{"recipientId": 1, "lines":[{"itemId":1,"count":2,"unitPrice":null}]}` → 201 with invoice JSON (or 400 with error code).

- [ ] **Step 1: Write the failing tests**

`CreateInvoiceTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class CreateInvoiceTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public CreateInvoiceTests(ApiTestHost host) => _host = host;

    private async Task<(long ItemId, long RecipientId)> SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem
        {
            Number = "4001",
            Description = "Grabpflege",
            VatRate = 7m,
            DefaultPrice = 120.00m,
        };
        var recipient = new Recipient
        {
            Name = "Familie Mahr",
            Address = "Musterstraße 1\n12345 Musterstadt",
        };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (item.Id, recipient.Id);
    }

    [Fact]
    public async Task Create_invoice_persists_snapshots_and_both_documents()
    {
        var (itemId, recipientId) = await SeedAsync();
        using var client = _host.CreateClient();

        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId,
            lines = new[]
            {
                new { itemId, count = 2, unitPrice = (decimal?)null },
            },
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = await ODataJson.ReadAsync(response);
        var number = json.RootElement.GetProperty("InvoiceNumber").GetString();
        Assert.Matches(new Regex(@"^\d{4}-\d{4}$"), number);
        var id = json.RootElement.GetProperty("Id").GetInt64();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices
            .Include(i => i.Lines).Include(i => i.Documents)
            .SingleAsync(i => i.Id == id);

        Assert.Equal("Familie Mahr", invoice.RecipientName);
        Assert.Contains("Musterstraße 1", invoice.RecipientAddress);
        Assert.Equal("Kopie", invoice.StampWord);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2, line.Count);
        Assert.Equal(120.00m, line.UnitPrice);
        Assert.Equal(7m, line.VatRate);
        Assert.Equal(2, invoice.Documents.Count);
        Assert.Contains(invoice.Documents, d => d.Kind == DocumentKind.Normal);
        Assert.Contains(invoice.Documents, d => d.Kind == DocumentKind.Copy);
        Assert.All(invoice.Documents, d => Assert.NotEmpty(d.Content));
    }

    [Fact]
    public async Task Create_invoice_with_item_without_price_and_no_unit_price_fails()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem { Number = "4002", Description = "No price", VatRate = 19m, DefaultPrice = null };
        var recipient = new Recipient { Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("PriceRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_invoice_with_recipient_without_address_fails()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 19m });
        var item = new InvoiceItem { Number = "4003", Description = "X", VatRate = 19m, DefaultPrice = 1m };
        var recipient = new Recipient { Name = "No address", Address = "" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)1m } },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("RecipientAddressRequired", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_creates_get_distinct_numbers()
    {
        var (itemId, recipientId) = await SeedAsync();
        using var client = _host.CreateClient();

        var payload = ODataJson.Body(new
        {
            recipientId,
            lines = new[] { new { itemId, count = 1, unitPrice = (decimal?)10m } },
        });

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () => await client.PostAsync("/odata/CreateInvoice", payload)));
        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var numbers = new System.Collections.Generic.List<string>();
        foreach (var r in responses)
        {
            using var json = await ODataJson.ReadAsync(r);
            numbers.Add(json.RootElement.GetProperty("InvoiceNumber").GetString());
        }

        Assert.Equal(5, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Failed_copy_generation_persists_no_half_generated_pair()
    {
        var host = new FailingDocsHost();
        try
        {
            long itemId;
            long recipientId;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.VatRates.Add(new VatRate { Rate = 7m });
                var item = new InvoiceItem { Number = "4004", Description = "X", VatRate = 7m, DefaultPrice = 1m };
                var recipient = new Recipient { Name = "X", Address = "Y" };
                db.InvoiceItems.Add(item);
                db.Recipients.Add(recipient);
                await db.SaveChangesAsync();
                itemId = item.Id;
                recipientId = recipient.Id;
            }

            using var client = host.CreateClient();
            var response = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId,
                lines = new[] { new { itemId, count = 1, unitPrice = (decimal?)1m } },
            }));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Invoices.ToListAsync());
            Assert.Empty(await db.Documents.ToListAsync());
        }
        finally
        {
            host.Dispose();
        }
    }

    private sealed class FailingDocumentGenerator : IDocumentGenerator
    {
        public byte[] Generate(InvoiceDocumentData data, bool isCopy)
        {
            if (isCopy)
            {
                throw new InvalidOperationException("copy generation failed");
            }

            return DocumentService.GeneratePdf(data, isCopy: false);
        }
    }

    private sealed class FailingDocsHost : ApiTestHost
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IDocumentGenerator));
                services.Remove(descriptor);
                services.AddSingleton<IDocumentGenerator, FailingDocumentGenerator>();
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter CreateInvoiceTests -v q`
Expected: FAIL — 404 on `/odata/CreateInvoice`.

- [ ] **Step 3: Implement the service and endpoint**

`ValidationException.cs`:

```csharp
namespace Burkard.Invoicing.Api.Services;

public sealed class ValidationException : Exception
{
    public string Code { get; }

    public ValidationException(string code, string message) : base(message)
    {
        Code = code;
    }
}
```

`DocumentGenerator.cs` (indirection over the static `DocumentService` so tests can inject failures):

```csharp
using Burkard.Invoicing.Documents;

namespace Burkard.Invoicing.Api.Services;

public interface IDocumentGenerator
{
    byte[] Generate(InvoiceDocumentData data, bool isCopy);
}

public sealed class QuestPdfDocumentGenerator : IDocumentGenerator
{
    public byte[] Generate(InvoiceDocumentData data, bool isCopy) =>
        DocumentService.GeneratePdf(data, isCopy);
}
```

`DocumentDataBuilder.cs` — builds layout data from an invoice, computing totals in integer cents:

```csharp
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;

namespace Burkard.Invoicing.Api.Services;

public sealed class DocumentDataBuilder
{
    public InvoiceDocumentData Build(Invoice invoice, string? shareText)
    {
        var lines = invoice.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l =>
            {
                var lineTotalCents = Math.Round(l.Count * l.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero);
                var lineTotal = lineTotalCents / 100m;
                var vatAmount = Math.Round(lineTotalCents * l.VatRate / 100m, 0, MidpointRounding.AwayFromZero) / 100m;
                return new InvoiceDocumentData.LineData(
                    l.LineNumber, l.ItemNumber, l.Description, l.Count,
                    l.UnitPrice, l.VatRate, lineTotal, vatAmount);
            })
            .ToArray();

        var summaries = lines
            .GroupBy(l => l.VatRate)
            .Select(g => new
            {
                Rate = g.Key,
                Net = g.Sum(l => Math.Round(l.LineTotal * 100m, 0, MidpointRounding.AwayFromZero)),
                Vat = g.Sum(l => Math.Round(l.VatAmount * 100m, 0, MidpointRounding.AwayFromZero)),
            })
            .OrderBy(g => g.Rate)
            .Select(g => new InvoiceDocumentData.VatSummary(g.Rate, g.Net / 100m, g.Vat / 100m, (g.Net + g.Vat) / 100m))
            .ToArray();

        var netTotalCents = summaries.Sum(s => Math.Round(s.NetTotal * 100m, 0, MidpointRounding.AwayFromZero));
        var vatTotalCents = summaries.Sum(s => Math.Round(s.VatAmount * 100m, 0, MidpointRounding.AwayFromZero));

        return new InvoiceDocumentData
        {
            InvoiceNumber = invoice.InvoiceNumber,
            GeneratedAt = DateTime.SpecifyKind(invoice.GeneratedAt, DateTimeKind.Utc),
            RecipientName = invoice.RecipientName,
            RecipientAddress = invoice.RecipientAddress,
            ShareText = shareText,
            EndingText = invoice.EndingText,
            StampWord = invoice.StampWord,
            Lines = lines,
            VatSummaries = summaries,
            NetTotal = netTotalCents / 100m,
            TotalVat = vatTotalCents / 100m,
            GrossTotal = (netTotalCents + vatTotalCents) / 100m,
        };
    }
}
```

`InvoiceService.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class InvoiceLineInput
{
    public long ItemId { get; set; }
    public int Count { get; set; }
    public decimal? UnitPrice { get; set; }
}

public sealed class InvoiceService
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly DocumentDataBuilder _dataBuilder;
    private readonly IDocumentGenerator _documents;

    public InvoiceService(AppDbContext db, InvoiceNumberService numbers,
        DocumentDataBuilder dataBuilder, IDocumentGenerator documents)
    {
        _db = db;
        _numbers = numbers;
        _dataBuilder = dataBuilder;
        _documents = documents;
    }

    public async Task<Invoice> CreateAsync(long recipientId, IReadOnlyList<InvoiceLineInput> lineInputs, CancellationToken ct)
    {
        var recipient = await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
            ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

        var lines = await BuildLinesAsync(lineInputs, ct);
        var settings = await _db.GlobalSettings.FirstAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(recipient, settings, lines, ct);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // unique (Year, Sequence) race — retry with a fresh number
            }
        }
    }

    private async Task<List<InvoiceLine>> BuildLinesAsync(IReadOnlyList<InvoiceLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
        {
            throw new ValidationException("NoLines", "At least one line is required.");
        }

        var itemIds = inputs.Select(l => l.ItemId).ToArray();
        var items = await _db.InvoiceItems
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var result = new List<InvoiceLine>(inputs.Count);
        foreach (var (input, index) in inputs.Select((l, i) => (l, i)))
        {
            if (!items.TryGetValue(input.ItemId, out var item))
            {
                throw new ValidationException($"ItemNotFound:{input.ItemId}", $"The item {input.ItemId} does not exist.");
            }

            if (input.Count <= 0)
            {
                throw new ValidationException("InvalidCount", "The count must be positive.");
            }

            var unitPrice = input.UnitPrice ?? item.DefaultPrice;
            if (unitPrice is null)
            {
                throw new ValidationException("PriceRequired", $"A price is required for item '{item.Number}'.");
            }

            result.Add(new InvoiceLine
            {
                ItemId = item.Id,
                ItemNumber = item.Number,
                Description = item.Description,
                Count = input.Count,
                UnitPrice = unitPrice.Value,
                VatRate = item.VatRate,
                LineNumber = index + 1,
            });
        }

        return result;
    }

    private async Task<Invoice> PersistAsync(Recipient recipient, GlobalSettings settings,
        List<InvoiceLine> lines, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            throw new ValidationException("RecipientAddressRequired", "The recipient address is required.");
        }

        var (number, year, sequence) = await _numbers.AllocateAsync(ct);
        var now = DateTime.UtcNow;

        var invoice = new Invoice
        {
            RevisionNumber = 0,
            Year = year,
            Sequence = sequence,
            InvoiceNumber = number,
            RecipientId = recipient.Id,
            RecipientName = recipient.Name,
            RecipientAddress = recipient.Address,
            EndingText = settings.EndingText,
            StampWord = settings.StampWord,
            GeneratedAt = now,
            Lines = lines,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        invoice.RootId = invoice.Id;

        var data = _dataBuilder.Build(invoice, shareText: null);
        invoice.Documents = new List<Document>
        {
            new()
            {
                Kind = DocumentKind.Normal,
                FileName = $"{invoice.InvoiceNumber}.pdf",
                Content = _documents.Generate(data, isCopy: false),
                GeneratedAt = now,
            },
            new()
            {
                Kind = DocumentKind.Copy,
                FileName = $"{invoice.InvoiceNumber}-Kopie.pdf",
                Content = _documents.Generate(data, isCopy: true),
                GeneratedAt = now,
            },
        };

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice;
    }
}
```

Note: both documents are generated and saved within one transaction — a failure on either persists neither (atomic pair).

`InvoiceActionsController.cs`:

```csharp
using Burkard.Invoicing.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Microsoft.AspNetCore.Mvc.Route("odata")]
public class InvoiceActionsController : ODataController
{
    private readonly InvoiceService _service;

    public InvoiceActionsController(InvoiceService service) => _service = service;

    [HttpPost("CreateInvoice")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var invoice = await _service.CreateAsync(request.RecipientId, request.Lines, HttpContext.RequestAborted);
            return Created($"odata/Invoices({invoice.Id})", invoice);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
        }
    }

    public sealed class CreateInvoiceRequest
    {
        public long RecipientId { get; set; }
        public List<InvoiceLineInput> Lines { get; set; } = new();
    }
}
```

Register services in `Program.cs`:

```csharp
builder.Services.AddScoped<InvoiceNumberService>();
builder.Services.AddScoped<DocumentDataBuilder>();
builder.Services.AddSingleton<IDocumentGenerator, QuestPdfDocumentGenerator>();
builder.Services.AddScoped<InvoiceService>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS (all four tests).

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: CreateInvoice action with snapshots and atomic document pair"
```

---

### Task 9: CreateRevision action and document download endpoint

**Files:**

- Create: `backend/tests/Invoicing.IntegrationTests/RevisionTests.cs`, `backend/tests/Invoicing.IntegrationTests/DocumentDownloadTests.cs`, `backend/src/Invoicing.Api/OData/Controllers/DocumentDownloadController.cs`
- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs` (add `CreateRevisionAsync`), `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs` (add `CreateRevision` action)

**Interfaces:**

- Produces: `InvoiceService.CreateRevisionAsync(long invoiceId, long recipientId, IReadOnlyList<InvoiceLineInput> lines, CancellationToken) → Invoice` — new row with same `RootId`, `RevisionNumber + 1`, number `{base}-R{n}` (via `InvoiceNumberFormatter.StripRevision`), re-snapshots recipient/settings, regenerates both documents, leaves original untouched; `POST /odata/CreateRevision` body `{"invoiceId":1,"recipientId":1,"lines":[...]}` → 201; `GET /odata/Documents({key})/$value` → PDF file.

- [ ] **Step 1: Write the failing tests**

`RevisionTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class RevisionTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public RevisionTests(ApiTestHost host) => _host = host;

    private async Task<(long InvoiceId, long ItemId, long RecipientId, byte[] NormalBytes)> SeedInvoiceAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "5001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        var recipient = new Recipient { Name = "Familie Mahr", Address = "Altstraße 1\n11111 Altstadt" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(created);
        var invoiceId = json.RootElement.GetProperty("Id").GetInt64();

        var normal = await db.Documents.SingleAsync(d => d.InvoiceId == invoiceId && d.Kind == DocumentKind.Normal);
        return (invoiceId, item.Id, recipient.Id, normal.Content);
    }

    [Fact]
    public async Task Revision_creates_new_row_keeps_original_and_resnapshots_recipient()
    {
        var (invoiceId, itemId, recipientId, originalBytes) = await SeedInvoiceAsync();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Recipients.FindAsync(recipientId);
        recipient!.Address = "Neustraße 2\n22222 Neustadt";
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/CreateRevision", ODataJson.Body(new
        {
            invoiceId,
            recipientId,
            lines = new[] { new { itemId, count = 2, unitPrice = (decimal?)null } },
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.EndsWith("-R1", json.RootElement.GetProperty("InvoiceNumber").GetString());

        var invoices = await db.Invoices.Where(i => i.Id == invoiceId || i.RootId == invoiceId)
            .Include(i => i.Lines).Include(i => i.Documents)
            .OrderBy(i => i.RevisionNumber)
            .ToListAsync();
        Assert.Equal(2, invoices.Count);

        var original = invoices[0];
        var revision = invoices[1];
        Assert.Equal(original.RootId, revision.RootId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("Altstraße 1\n11111 Altstadt", original.RecipientAddress);
        Assert.Equal("Neustraße 2\n22222 Neustadt", revision.RecipientAddress);
        Assert.Equal(1, original.Lines.Count);
        Assert.Equal(2, revision.Lines.Count);

        // original documents unchanged (byte-identical)
        var originalNormal = original.Documents.Single(d => d.Kind == DocumentKind.Normal);
        Assert.Equal(originalBytes, originalNormal.Content);
        Assert.Equal(2, revision.Documents.Count);
    }
}
```

`DocumentDownloadTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class DocumentDownloadTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public DocumentDownloadTests(ApiTestHost host) => _host = host;

    [Fact]
    public async Task Download_normal_document_returns_stored_pdf_bytes()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "5002", Description = "X", VatRate = 7m, DefaultPrice = 5m };
        var recipient = new Recipient { Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
        {
            recipientId = recipient.Id,
            lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
        }));
        created.EnsureSuccessStatusCode();

        var stored = await db.Documents.SingleAsync(d => d.Kind == DocumentKind.Normal);
        var response = await client.GetAsync($"/odata/Documents({stored.Id})/$value");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(stored.Content, bytes);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Contains(".pdf", disposition!.FileNameStar ?? disposition.FileName ?? "");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter "RevisionTests|DocumentDownloadTests" -v q`
Expected: FAIL — 404 on `/odata/CreateRevision` and `/odata/Documents(..)/$value`.

- [ ] **Step 3: Implement**

Add to `InvoiceService`:

```csharp
public async Task<Invoice> CreateRevisionAsync(long invoiceId, long recipientId,
    IReadOnlyList<InvoiceLineInput> lineInputs, CancellationToken ct)
{
    var original = await _db.Invoices
        .Include(i => i.Lines).Include(i => i.Documents)
        .SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
        ?? throw new ValidationException("InvoiceNotFound", "The invoice does not exist.");

    var recipient = await _db.Recipients.FindAsync(new object[] { recipientId }, ct)
        ?? throw new ValidationException("RecipientNotFound", "The recipient does not exist.");

    var lines = await BuildLinesAsync(lineInputs, ct);
    var settings = await _db.GlobalSettings.FirstAsync(ct);
    var now = DateTime.UtcNow;
    var baseNumber = InvoiceNumberFormatter.StripRevision(original.InvoiceNumber);

    var revision = new Invoice
    {
        RootId = original.RootId,
        RevisionNumber = original.RevisionNumber + 1,
        Year = original.Year,
        Sequence = original.Sequence,
        InvoiceNumber = InvoiceNumberFormatter.ForRevision(baseNumber, original.RevisionNumber + 1),
        GraveId = original.GraveId,
        PeriodYear = original.PeriodYear,
        PeriodHalf = original.PeriodHalf,
        SharePercent = original.SharePercent,
        RecipientId = recipient.Id,
        RecipientName = recipient.Name,
        RecipientAddress = recipient.Address,
        EndingText = settings.EndingText,
        StampWord = settings.StampWord,
        GeneratedAt = now,
        Lines = lines,
    };

    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    _db.Invoices.Add(revision);
    await _db.SaveChangesAsync(ct);

    var data = _dataBuilder.Build(revision, shareText: null);
revision.Documents = new List<Document>
    {
        new()
        {
            Kind = DocumentKind.Normal,
            FileName = $"{revision.InvoiceNumber}.pdf",
            Content = _documents.Generate(data, isCopy: false),
            GeneratedAt = now,
        },
        new()
        {
            Kind = DocumentKind.Copy,
            FileName = $"{revision.InvoiceNumber}-Kopie.pdf",
            Content = _documents.Generate(data, isCopy: true),
            GeneratedAt = now,
        },
    };

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return revision;
}
```

Add to `InvoiceActionsController`:

```csharp
[HttpPost("CreateRevision")]
public async Task<IActionResult> CreateRevision([FromBody] CreateRevisionRequest request)
{
    try
    {
        var invoice = await _service.CreateRevisionAsync(
            request.InvoiceId, request.RecipientId, request.Lines, HttpContext.RequestAborted);
        return Created($"odata/Invoices({invoice.Id})", invoice);
    }
    catch (ValidationException ex)
    {
        return BadRequest(new { error = new { code = ex.Code, message = ex.Message } });
    }
}

public sealed class CreateRevisionRequest
{
    public long InvoiceId { get; set; }
    public long RecipientId { get; set; }
    public List<InvoiceLineInput> Lines { get; set; } = new();
}
```

`DocumentDownloadController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class DocumentDownloadController : ODataController
{
    private readonly AppDbContext _db;

    public DocumentDownloadController(AppDbContext db) => _db = db;

    [HttpGet("Documents({key})/$value")]
    public IActionResult GetStream([FromRoute] long key)
    {
        var document = _db.Documents.Find(key);
        if (document is null)
        {
            return NotFound();
        }

        return File(document.Content, "application/pdf", document.FileName);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: invoice revisions and document download endpoint"
```

---

### Task 10: Job queue, runner, and progress endpoints

**Files:**

- Create: `backend/src/Invoicing.Api/Jobs/JobQueue.cs`, `backend/src/Invoicing.Api/Jobs/IJobHandler.cs`, `backend/src/Invoicing.Api/Jobs/JobRunner.cs`, `backend/src/Invoicing.Api/Services/JobService.cs`, `backend/src/Invoicing.Api/OData/Controllers/JobsController.cs`, `backend/tests/Invoicing.IntegrationTests/JobTests.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (register queue/runner/handler), `backend/tests/Invoicing.IntegrationTests/ApiTestHost.cs` (expose `DbFilePath`, register `TestJobHandler`)

**Interfaces:**

- Consumes: `Job`/`JobStatus` entities, `AppDbContext`.
- Produces: `JobQueue.Enqueue(long jobId)` / `ReadAllAsync(CancellationToken) → IAsyncEnumerable<long>`; `IJobHandler { string Type; Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct); }`; `IProgressWriter.ReportAsync(int processed, int total, CancellationToken)`; `JobService.EnqueueAsync(type, payloadJson, totalCount, ct) → Job` and `JobService.HasActive(type) → bool`; `GET /odata/Jobs` + `GET /odata/Jobs({key})` for polling; startup recovery resets `Running → Queued` and requeues `Queued`.

- [ ] **Step 1: Write the failing tests**

`JobTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class TestJobHandler : IJobHandler
{
    public string Type => "Test";
    public int Calls;

    public async Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct)
    {
        Calls++;
        await progress.ReportAsync(0, 10, ct);
        await progress.ReportAsync(5, 10, ct);
        await progress.ReportAsync(10, 10, ct);
    }
}

public class JobTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public JobTests(ApiTestHost host) => _host = host;

    private async Task<Job> WaitForTerminalAsync(long jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("job did not finish");
    }

    [Fact]
    public async Task Enqueued_job_runs_to_success_and_updates_progress()
    {
        using var scope = _host.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = await jobs.EnqueueAsync("Test", null, 10, CancellationToken.None);

        var finished = await WaitForTerminalAsync(job.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(JobStatus.Succeeded, finished.Status);
        Assert.Equal(100, finished.ProgressPercent);
        Assert.Equal(10, finished.ProcessedCount);

        var handler = (TestJobHandler)_host.Services.GetServices<IJobHandler>().Single(h => h is TestJobHandler);
        Assert.Equal(1, handler.Calls); // this enqueue is the only "Test" job in this fixture
    }

    [Fact]
    public async Task Job_can_be_polled_via_odata()
    {
        using var scope = _host.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = await jobs.EnqueueAsync("Test", null, 10, CancellationToken.None);
        await WaitForTerminalAsync(job.Id, TimeSpan.FromSeconds(15));

        using var client = _host.CreateClient();
        var response = await client.GetAsync($"/odata/Jobs({job.Id})");
        response.EnsureSuccessStatusCode();
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("Succeeded", json.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Job_left_running_is_requeued_on_startup()
    {
        var recoveryHost = new ApiTestHost();
        try
        {
            // seed a stuck job directly before first app start
            using (var setup = new ServiceCollection()
                       .AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={recoveryHost.DbFilePath}"))
                       .BuildServiceProvider())
            {
                var db = setup.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
                db.Jobs.Add(new Job { Type = "Test", Status = JobStatus.Running, TotalCount = 10, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            using (var client = recoveryHost.CreateClient())
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                Job? finished = null;
                while (DateTime.UtcNow < deadline)
                {
                    using var scope = recoveryHost.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    finished = await db.Jobs.OrderByDescending(j => j.Id).FirstAsync();
                    if (finished.Status is JobStatus.Succeeded or JobStatus.Failed) break;
                    await Task.Delay(50);
                }

                Assert.NotNull(finished);
                Assert.Equal(JobStatus.Succeeded, finished!.Status);
            }
        }
        finally
        {
            recoveryHost.Dispose();
        }
    }
}
```

Modify `ApiTestHost.cs`: make the db file path protected and register the test handler:

```csharp
public class ApiTestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile;

    public string DbFilePath => _dbFile;

    public ApiTestHost()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
            services.AddSingleton<IJobHandler, TestJobHandler>();
        });
    }
    // Dispose unchanged
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter JobTests -v q`
Expected: FAIL (compile errors — `JobQueue`, `JobService`, `IJobHandler`, `IProgressWriter` missing).

- [ ] **Step 3: Implement**

`Jobs/IJobHandler.cs`:

```csharp
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Jobs;

public interface IProgressWriter
{
    Task ReportAsync(int processed, int total, CancellationToken ct);
}

public interface IJobHandler
{
    string Type { get; }
    Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct);
}
```

`Jobs/JobQueue.cs`:

```csharp
using System.Threading.Channels;

namespace Burkard.Invoicing.Api.Jobs;

public sealed class JobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(long jobId) => _channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
```

`Services/JobService.cs`:

```csharp
using Burkard.Invoicing.Api.Jobs;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class JobService
{
    private readonly AppDbContext _db;
    private readonly JobQueue _queue;

    public JobService(AppDbContext db, JobQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<Job> EnqueueAsync(string type, string? payloadJson, int totalCount, CancellationToken ct)
    {
        var job = new Job
        {
            Type = type,
            Status = JobStatus.Queued,
            PayloadJson = payloadJson,
            TotalCount = totalCount,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        _queue.Enqueue(job.Id);
        return job;
    }

    public bool HasActive(string type) =>
        _db.Jobs.Any(j => j.Type == type && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running));
}
```

`Jobs/JobRunner.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Jobs;

public sealed class JobRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobQueue _queue;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(IServiceScopeFactory scopeFactory, JobQueue queue, ILogger<JobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RecoverStuckJobs();
        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed unexpectedly", jobId);
            }
        }
    }

    private void RecoverStuckJobs()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stuck = db.Jobs.Where(j => j.Status == JobStatus.Running).ToList();
        foreach (var job in stuck)
        {
            job.Status = JobStatus.Queued;
            job.StartedAt = null;
        }

        db.SaveChanges();
        var queuedIds = db.Jobs.Where(j => j.Status == JobStatus.Queued).Select(j => j.Id).ToList();
        foreach (var id in queuedIds)
        {
            _queue.Enqueue(id);
        }
    }

    private async Task ProcessAsync(long jobId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.Jobs.FindAsync(new object[] { jobId }, stoppingToken);
        if (job is null || job.Status != JobStatus.Queued)
        {
            return;
        }

        var handler = scope.ServiceProvider.GetServices<IJobHandler>()
            .FirstOrDefault(h => h.Type == job.Type);
        if (handler is null)
        {
            job.Status = JobStatus.Failed;
            job.Error = $"No handler registered for job type '{job.Type}'.";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        var progress = new DbProgressWriter(db, job);
        try
        {
            await handler.ExecuteAsync(job, progress, stoppingToken);
            job.Status = JobStatus.Succeeded;
            job.ProgressPercent = 100;
            job.ProcessedCount = job.TotalCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
        }

        job.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
    }

    private sealed class DbProgressWriter : IProgressWriter
    {
        private readonly AppDbContext _db;
        private readonly Job _job;
        private int _lastPercent = -1;

        public DbProgressWriter(AppDbContext db, Job job)
        {
            _db = db;
            _job = job;
        }

        public async Task ReportAsync(int processed, int total, CancellationToken ct)
        {
            var percent = total <= 0 ? 100 : (int)(processed * 100L / total);
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            _job.ProgressPercent = percent;
            _job.ProcessedCount = processed;
            await _db.SaveChangesAsync(ct);
        }
    }
}
```

`JobsController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

public class JobsController : ODataController
{
    private readonly AppDbContext _db;

    public JobsController(AppDbContext db) => _db = db;

    [EnableQuery]
    public IQueryable<Job> Get() => _db.Jobs.AsNoTracking();

    public ActionResult<Job> Get([FromRoute] long key)
    {
        var job = _db.Jobs.Find(key);
        return job is null ? NotFound() : Ok(job);
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddScoped<JobService>();
builder.Services.AddHostedService<JobRunner>();
```

`GraveInvoicesJobHandler` is registered in Task 11; for this task the runner simply has no handlers besides the test one.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS (all three JobTests; note the third test uses its own `ApiTestHost` instance).

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: persistent job queue with background runner and recovery"
```

---

### Task 11: Share calculation and half-year grave invoice generation

**Files:**

- Create: `backend/src/Invoicing.Domain/ShareCalculator.cs`, `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`, `backend/src/Invoicing.Api/Jobs/GraveInvoicesJobHandler.cs`, `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`, `backend/tests/Invoicing.IntegrationTests/ShareCalculatorTests.cs`, `backend/tests/Invoicing.IntegrationTests/GraveGenerationTests.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceActionsController.cs` (add `GenerateGraveInvoices` action), `backend/src/Invoicing.Api/Program.cs` (register `IJobHandler`), `backend/src/Invoicing.Documents/DocumentTexts.cs` (created in Task 6 — add `ShareText` if missing)

**Interfaces:**

- Consumes: `ShareCalculator`, `DocumentService`, `AppDbContext`, `JobService`.
- Produces: `ShareCalculator.SplitCents(long totalCents, int recipients) → long[]` (base + remainder distributed one cent at a time in order); `POST /odata/GenerateGraveInvoices` body `{"year":2026,"half":1}` → 202 + Job (or 409 `JobAlreadyRunning`, 400 `InvalidHalfYear`); `GET /odata/GraveInvoicingStatus?year=2026&half=1` → `{ "value": [ { "Id", "GraveNumber", "Invoiced" } ] }` ordered not-invoiced first; job handler type `"GraveInvoices"` that creates one invoice per recipient with distributed shares, `SharePercent = 100m/n`, `GraveInvoiceRun` rows, and `ResultJson` listing skipped graves.

- [ ] **Step 1: Write the failing unit tests**

`ShareCalculatorTests.cs`:

```csharp
using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class ShareCalculatorTests
{
    [Fact]
    public void Even_split_has_no_remainder()
    {
        Assert.Equal(new long[] { 500, 500 }, ShareCalculator.SplitCents(1000, 2));
    }

    [Fact]
    public void Remainder_is_distributed_one_cent_at_a_time_from_the_front()
    {
        Assert.Equal(new long[] { 334, 333, 333 }, ShareCalculator.SplitCents(1000, 3));
    }

    [Fact]
    public void Shares_always_sum_to_the_total()
    {
        var shares = ShareCalculator.SplitCents(1, 3);
        Assert.Equal(1, shares.Sum());
    }

    [Fact]
    public void Zero_recipients_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareCalculator.SplitCents(100, 0));
    }
}
```

(`SplitCents(1, 3)` returns `[1, 0, 0]` — the sum is 1.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter ShareCalculatorTests -v q`
Expected: FAIL (compile error — `ShareCalculator` missing).

- [ ] **Step 3: Implement `ShareCalculator` and generator internals**

`backend/src/Invoicing.Domain/ShareCalculator.cs`:

```csharp
namespace Burkard.Invoicing.Domain;

public static class ShareCalculator
{
    /// <summary>Distributes <paramref name="totalCents"/> evenly across
    /// <paramref name="recipients"/> recipients; the remainder is handed out
    /// one cent at a time to the first recipients so the sum always equals
    /// the total.</summary>
    public static long[] SplitCents(long totalCents, int recipients)
    {
        if (recipients <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipients));
        }

        var baseShare = totalCents / recipients;
        var remainder = (int)(totalCents % recipients);
        var shares = new long[recipients];
        for (var i = 0; i < recipients; i++)
        {
            shares[i] = baseShare + (i < remainder ? 1 : 0);
        }

        return shares;
    }
}
```

`backend/src/Invoicing.Documents/DocumentTexts.cs` (German texts for generated documents, central place — the "resource bundle" of the PDF layer):

```csharp
namespace Burkard.Invoicing.Documents;

public static class DocumentTexts
{
    public static string ShareText(decimal percent) =>
        string.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"),
            $"Diese Rechnung enthält {percent:0.##} % des Gesamtbetrags.");

    public static string InvoiceTitle(string invoiceNumber) => $"Rechnung {invoiceNumber}";

    public static string PageFooter => "Seite";

    public static string ColumnDescription => "Bezeichnung";
    public static string ColumnCount => "Anzahl";
    public static string ColumnUnitPrice => "Einzelpreis";
    public static string ColumnVat => "MwSt.";
    public static string ColumnTotal => "Summe";
    public static string NetForRate(decimal rate, decimal amount) =>
        $"Netto {rate:0.#} %: {amount:N2}";
    public static string VatForRate(decimal rate, decimal amount) =>
        $"MwSt. {rate:0.#} %: {amount:N2}";
    public static string NetTotal(decimal amount) => $"Netto gesamt: {amount:N2}";
    public static string VatTotal(decimal amount) => $"MwSt. gesamt: {amount:N2}";
    public static string GrossTotal(decimal amount) => $"Gesamtbetrag: {amount:N2}";
}
```

- [ ] **Step 4: Verify the document tests still pass after the composer refactor**

`DocumentTexts` was already used by the composer since Task 6 — run the document tests to confirm nothing regressed:

Run: `dotnet test backend/tests/Invoicing.DocumentTests -v q`
Expected: PASS.

- [ ] **Step 5: Write the failing generation tests**

`GraveGenerationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class GraveGenerationTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public GraveGenerationTests(ApiTestHost host) => _host = host;

    private async Task<(long GraveTwoRecipients, long GraveOneRecipient, long GraveNoRecipients)> SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "6001", Description = "Grabpflege", VatRate = 7m, DefaultPrice = 100m };
        db.InvoiceItems.Add(item);
        var r1 = new Recipient { Name = "Anna", Address = "A-Straße 1" };
        var r2 = new Recipient { Name = "Bert", Address = "B-Straße 2" };
        var r3 = new Recipient { Name = "Carla", Address = "C-Straße 3" };
        db.Recipients.AddRange(r1, r2, r3);
        await db.SaveChangesAsync();

        var g2 = new Grave
        {
            GraveNumber = "G-1",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var g1 = new Grave
        {
            GraveNumber = "G-2",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var g0 = new Grave
        {
            GraveNumber = "G-3",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2040, 12, 31),
            CreatedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Graves.AddRange(g2, g1, g0);
        await db.SaveChangesAsync();

        db.GraveItems.AddRange(
            new GraveItem { GraveId = g2.Id, ItemId = item.Id, Count = 2 },
            new GraveItem { GraveId = g1.Id, ItemId = item.Id, Count = 1 });
        db.GraveRecipients.AddRange(
            new GraveRecipient { GraveId = g2.Id, RecipientId = r1.Id },
            new GraveRecipient { GraveId = g2.Id, RecipientId = r2.Id },
            new GraveRecipient { GraveId = g1.Id, RecipientId = r3.Id });
        await db.SaveChangesAsync();

        return (g2.Id, g1.Id, g0.Id);
    }

    private async Task<JsonElement> PollJobAsync(HttpClient client, long jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/odata/Jobs({jobId})");
            response.EnsureSuccessStatusCode();
            using var json = await ODataJson.ReadAsync(response);
            var status = json.RootElement.GetProperty("Status").GetString();
            if (status is "Succeeded" or "Failed")
            {
                return json.RootElement.Clone();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("generation job did not finish");
    }

    [Fact]
    public async Task Generation_creates_one_invoice_per_recipient_with_split_shares()
    {
        var (grave2, grave1, _) = await SeedAsync();
        using var client = _host.CreateClient();

        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var startJson = await ODataJson.ReadAsync(start);
        var jobId = startJson.RootElement.GetProperty("Id").GetInt64();

        var job = await PollJobAsync(client, jobId);
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Documents)
            .Where(i => i.PeriodYear == 2026 && i.PeriodHalf == 1)
            .ToListAsync();

        // grave G-1: 2 recipients × share of 214.00 total (2 × 100 + 7% VAT = 214.00) → 107.00 each
        // grave G-2: 1 recipient → full 107.00
        Assert.Equal(3, invoices.Count);

        var g2Invoices = invoices.Where(i => i.GraveId == grave2).ToList();
        Assert.Equal(2, g2Invoices.Count);
        Assert.All(g2Invoices, i => Assert.Equal(50m, i.SharePercent));

        // split totals: 21400 cents / 2 = 10700 each → line gross 107.00
        foreach (var invoice in g2Invoices)
        {
            var grossCents = invoice.Lines.Sum(l => (long)Math.Round(
                Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m));
            Assert.Equal(10700, grossCents);
        }

        // shares sum to the grave total: 10700 + 10700 = 21400
        Assert.Equal(21400, g2Invoices.Sum(i => (long)Math.Round(
            i.Lines.Sum(l => Math.Round(l.Count * l.UnitPrice * 100m) * (100m + l.VatRate) / 100m))));

        Assert.Equal(2, await db.GraveInvoiceRuns.CountAsync(r => r.Year == 2026 && r.Half == 1));
        Assert.All(invoices, i =>
        {
            Assert.Equal(2, i.Documents.Count);
            Assert.All(i.Documents, d => Assert.NotEmpty(d.Content));
        });
    }

    [Fact]
    public async Task Second_generation_for_same_half_year_is_rejected()
    {
        var (grave2, grave1, graveNoRecipients) = await SeedAsync();
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == graveNoRecipients);
            db.Graves.Remove(grave); // G-3 can never be invoiced; remove it so ALL graves are covered
            await db.SaveChangesAsync();
        }

        using var client = _host.CreateClient();
        var first = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var firstJson = await ODataJson.ReadAsync(first);
        await PollJobAsync(client, firstJson.RootElement.GetProperty("Id").GetInt64());

        var second = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var secondJson = await ODataJson.ReadAsync(second);
        Assert.Equal("GravesAlreadyInvoiced", secondJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_generation_is_rejected_with_409()
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Add(new Job
            {
                Type = "GraveInvoices",
                Status = JobStatus.Running,
                TotalCount = 1,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = _host.CreateClient();
        var response = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 2 }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal("JobAlreadyRunning", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Status_endpoint_orders_not_invoiced_first_and_reports_skips()
    {
        var (_, _, graveNoRecipients) = await SeedAsync();
        using var client = _host.CreateClient();

        var statusBefore = await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1");
        using var before = await ODataJson.ReadAsync(statusBefore);
        var itemsBefore = before.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.All(itemsBefore, e => Assert.False(e.GetProperty("Invoiced").GetBoolean()));

        var start = await client.PostAsync("/odata/GenerateGraveInvoices",
            ODataJson.Body(new { year = 2026, half = 1 }));
        using var startJson = await ODataJson.ReadAsync(start);
        var job = await PollJobAsync(client, startJson.RootElement.GetProperty("Id").GetInt64());
        Assert.Equal("Succeeded", job.GetProperty("Status").GetString());

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.GraveInvoiceRuns.CountAsync()); // G-1 and G-2
        // grave without recipients is skipped and reported
        var storedJob = await db.Jobs.SingleAsync(j => j.Type == "GraveInvoices");
        Assert.Contains("G-3", storedJob.ResultJson);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter "ShareCalculatorTests|GraveGenerationTests" -v q`
Expected: FAIL — 404 on `/odata/GenerateGraveInvoices` (ShareCalculatorTests will fail at compile if Step 3 not yet done; implement ShareCalculator first, then these fail at HTTP level).

- [ ] **Step 7: Implement generator, handler, and endpoints**

`backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain;
using Burkard.Invoicing.Domain.Entities;
using Burkard.Invoicing.Documents;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.Services;

public sealed class GraveInvoiceGenerator
{
    private readonly AppDbContext _db;
    private readonly InvoiceNumberService _numbers;
    private readonly DocumentDataBuilder _dataBuilder;

    public GraveInvoiceGenerator(AppDbContext db, InvoiceNumberService numbers, DocumentDataBuilder dataBuilder)
    {
        _db = db;
        _numbers = numbers;
        _dataBuilder = dataBuilder;
    }

    /// <summary>Generates all invoices for one grave and half-year in a single
    /// transaction. Returns null when the grave must be skipped (no recipients,
    /// no items, or an item without default price), otherwise the created invoices.</summary>
    public async Task<List<Invoice>?> GenerateForGraveAsync(Grave grave, int year, int half, CancellationToken ct)
    {
        var recipients = await _db.GraveRecipients
            .Where(r => r.GraveId == grave.Id)
            .Select(r => r.Recipient)
            .ToListAsync(ct);

        var graveItems = await _db.GraveItems
            .Where(i => i.GraveId == grave.Id)
            .Include(i => i.Item)
            .ToListAsync(ct);

        if (recipients.Count == 0 || graveItems.Count == 0)
        {
            return null;
        }

        if (graveItems.Any(i => i.Item.DefaultPrice is null))
        {
            return null;
        }

        // total gross in cents: per line round(net cents * (100+rate)/100)
        static long TotalCents(GraveItem item)
        {
            var netCents = (long)Math.Round(item.Count * item.Item.DefaultPrice!.Value * 100m,
                0, MidpointRounding.AwayFromZero);
            return (long)Math.Round(netCents * (100m + item.Item.VatRate) / 100m,
                0, MidpointRounding.AwayFromZero);
        }

        long totalCents = 0;
        foreach (var item in graveItems)
        {
            totalCents += TotalCents(item);
        }

        var shares = ShareCalculator.SplitCents(totalCents, recipients.Count);
        var settings = await _db.GlobalSettings.FirstAsync(ct);
        var sharePercent = 100m / recipients.Count;
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var invoices = new List<Invoice>();
        for (var r = 0; r < recipients.Count; r++)
        {
            var (number, seqYear, sequence) = await _numbers.AllocateAsync(ct);
            var invoice = new Invoice
            {
                RevisionNumber = 0,
                Year = seqYear,
                Sequence = sequence,
                InvoiceNumber = number,
                GraveId = grave.Id,
                PeriodYear = year,
                PeriodHalf = half,
                SharePercent = sharePercent,
                RecipientId = recipients[r].Id,
                RecipientName = recipients[r].Name,
                RecipientAddress = recipients[r].Address,
                EndingText = settings.EndingText,
                StampWord = settings.StampWord,
                GeneratedAt = now,
            };

            invoice.Lines = DistributeShare(shares[r], graveItems);
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            invoice.RootId = invoice.Id;

            var data = _dataBuilder.Build(invoice, DocumentTexts.ShareText(sharePercent));
            invoice.Documents = new List<Document>
            {
                new()
                {
                    Kind = DocumentKind.Normal,
                    FileName = $"{invoice.InvoiceNumber}.pdf",
                    Content = DocumentService.GeneratePdf(data, isCopy: false),
                    GeneratedAt = now,
                },
                new()
                {
                    Kind = DocumentKind.Copy,
                    FileName = $"{invoice.InvoiceNumber}-Kopie.pdf",
                    Content = DocumentService.GeneratePdf(data, isCopy: true),
                    GeneratedAt = now,
                },
            };
            await _db.SaveChangesAsync(ct);
            invoices.Add(invoice);
        }

        _db.GraveInvoiceRuns.Add(new GraveInvoiceRun
        {
            GraveId = grave.Id,
            Year = year,
            Half = half,
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoices;
    }

    /// <summary>Distributes a share (in gross cents) across the grave's lines;
    /// the remainder is handed out one cent at a time to the first lines so the
    /// sum is exact. Each line's net is derived back from its gross cents.</summary>
    private static List<InvoiceLine> DistributeShare(long shareCents, List<GraveItem> graveItems)
    {
        var distributed = ShareCalculator.SplitCents(shareCents, graveItems.Count);

        var lines = new List<InvoiceLine>(graveItems.Count);
        for (var i = 0; i < graveItems.Count; i++)
        {
            var item = graveItems[i];
            var lineGross = distributed[i];
            var lineNet = (long)Math.Round(lineGross * 100m / (100m + item.Item.VatRate),
                0, MidpointRounding.AwayFromZero);
            var unitPrice = lineNet / 100m / item.Count;

            lines.Add(new InvoiceLine
            {
                ItemId = item.Item.Id,
                ItemNumber = item.Item.Number,
                Description = item.Item.Description,
                Count = item.Count,
                UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                VatRate = item.Item.VatRate,
                LineNumber = i + 1,
            });
        }

        return lines;
    }
}
```

Note: per-line unit prices are derived from the distributed gross so each recipient's invoice totals exactly their share; the share statement above the item list (from `DocumentTexts.ShareText`) explains the percentage.

`Jobs/GraveInvoicesJobHandler.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Api.Services;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Burkard.Invoicing.Api.Jobs;

public sealed record GraveInvoicesPayload(int Year, int Half);

public sealed class GraveInvoicesJobHandler : IJobHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GraveInvoicesJobHandler(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string Type => "GraveInvoices";

    public async Task ExecuteAsync(Job job, IProgressWriter progress, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<GraveInvoicesPayload>(job.PayloadJson ?? "{}")
            ?? throw new InvalidOperationException("missing payload");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<GraveInvoiceGenerator>();

        var invoiced = db.GraveInvoiceRuns
            .Where(r => r.Year == payload.Year && r.Half == payload.Half)
            .Select(r => r.GraveId)
            .ToHashSet();

        var graves = await db.Graves
            .OrderBy(g => g.GraveNumber)
            .ToListAsync(ct);

        var todo = graves.Where(g => !invoiced.Contains(g.Id)).ToList();
        var skipped = new List<string>();
        var processed = 0;

        foreach (var grave in todo)
        {
            ct.ThrowIfCancellationRequested();
            var invoices = await generator.GenerateForGraveAsync(grave, payload.Year, payload.Half, ct);
            if (invoices is null)
            {
                skipped.Add(grave.GraveNumber);
            }

            processed++;
            await progress.ReportAsync(processed, todo.Count, ct);
        }

        job.ResultJson = JsonSerializer.Serialize(new
        {
            InvoicedGraves = todo.Count - skipped.Count,
            SkippedGraves = skipped,
        });
    }
}
```

`InvoiceActionsController` — add the generation action (replace the existing constructor):

```csharp
private readonly InvoiceService _service;
private readonly JobService _jobs;
private readonly AppDbContext _db;

public InvoiceActionsController(InvoiceService service, JobService jobs, AppDbContext db)
{
    _service = service;
    _jobs = jobs;
    _db = db;
}

[HttpPost("GenerateGraveInvoices")]
public async Task<IActionResult> GenerateGraveInvoices([FromBody] GenerateGraveInvoicesRequest request)
{
    if (request.Year < 2000 || request.Year > 2100 || request.Half is not (1 or 2))
    {
        return BadRequest(new
        {
            error = new { code = "InvalidHalfYear", message = "The half-year is invalid." }
        });
    }

    var alreadyInvoicedCount = _db.GraveInvoiceRuns
        .Where(r => r.Year == request.Year && r.Half == request.Half)
        .Select(r => r.GraveId)
        .Distinct()
        .Count();
    if (alreadyInvoicedCount > 0 && alreadyInvoicedCount >= _db.Graves.Count())
    {
        return Conflict(new
        {
            error = new { code = "GravesAlreadyInvoiced", message = "Invoices for this half-year were already generated." }
        });
    }

    if (_jobs.HasActive("GraveInvoices"))
    {
        return Conflict(new
        {
            error = new { code = "JobAlreadyRunning", message = "A generation is already running." }
        });
    }

    var total = _db.Graves.Count();
    var job = await _jobs.EnqueueAsync("GraveInvoices",
        JsonSerializer.Serialize(new GraveInvoicesPayload(request.Year, request.Half)),
        total, HttpContext.RequestAborted);
    return Accepted(job);
}

public sealed class GenerateGraveInvoicesRequest
{
    public int Year { get; set; }
    public int Half { get; set; }
}
```

(Add the needed `using`s: `System.Text.Json`, `Burkard.Invoicing.Api.Jobs`, `Microsoft.EntityFrameworkCore`, `Burkard.Invoicing.Api.Persistence`.)

`GraveInvoicingStatusController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class GraveInvoicingStatusController : ODataController
{
    private readonly AppDbContext _db;

    public GraveInvoicingStatusController(AppDbContext db) => _db = db;

    [HttpGet("GraveInvoicingStatus")]
    public async Task<IActionResult> Status([FromQuery] int year, [FromQuery] int half)
    {
        var invoicedIds = _db.GraveInvoiceRuns
            .Where(r => r.Year == year && r.Half == half)
            .Select(r => r.GraveId)
            .ToHashSet();

        var graves = await _db.Graves
            .OrderBy(g => !invoicedIds.Contains(g.Id))
            .ThenBy(g => g.GraveNumber)
            .Select(g => new { g.Id, g.GraveNumber })
            .ToListAsync();

        var value = graves.Select(g => new
        {
            g.Id,
            g.GraveNumber,
            Invoiced = invoicedIds.Contains(g.Id),
        });

        return Ok(new { value });
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddScoped<GraveInvoiceGenerator>();
builder.Services.AddScoped<IJobHandler, GraveInvoicesJobHandler>();
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add backend
git commit -m "feat: half-year grave invoice generation with share splitting"
```

---

### Task 12: Bulk download (normal and copies separately)

**Files:**

- Create: `backend/src/Invoicing.Api/OData/Controllers/BulkDownloadController.cs`, `backend/tests/Invoicing.IntegrationTests/BulkDownloadTests.cs`

**Interfaces:**

- Consumes: `Document`/`DocumentKind`, `Document.Invoice` navigation (for half-year filter).
- Produces: `POST /odata/BulkDownloadDocuments` body `{"kind":"Normal"|"Copy","year":2026,"half":1}` (year/half optional) → `application/zip` containing exactly the documents of the requested kind, never mixing kinds.

- [ ] **Step 1: Write the failing tests**

`BulkDownloadTests.cs`:

```csharp
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class BulkDownloadTests : IClassFixture<ApiTestHost>
{
    private readonly ApiTestHost _host;

    public BulkDownloadTests(ApiTestHost host) => _host = host;

    private async Task SeedTwoInvoicesAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.VatRates.Add(new VatRate { Rate = 7m });
        var item = new InvoiceItem { Number = "7001", Description = "X", VatRate = 7m, DefaultPrice = 10m };
        var recipient = new Recipient { Name = "X", Address = "Y" };
        db.InvoiceItems.Add(item);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();

        using var client = _host.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            var created = await client.PostAsync("/odata/CreateInvoice", ODataJson.Body(new
            {
                recipientId = recipient.Id,
                lines = new[] { new { itemId = item.Id, count = 1, unitPrice = (decimal?)null } },
            }));
            created.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Bulk_normal_download_contains_only_normal_documents()
    {
        await SeedTwoInvoicesAsync();
        using var client = _host.CreateClient();

        var response = await client.PostAsync("/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Normal" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.False(n.Contains("Kopie")));
    }

    [Fact]
    public async Task Bulk_copy_download_contains_only_copies()
    {
        await SeedTwoInvoicesAsync();
        using var client = _host.CreateClient();

        var response = await client.PostAsync("/odata/BulkDownloadDocuments",
            ODataJson.Body(new { kind = "Copy" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.Contains("Kopie", n));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests --filter BulkDownloadTests -v q`
Expected: FAIL — 404 on `/odata/BulkDownloadDocuments`.

- [ ] **Step 3: Implement the controller**

`BulkDownloadController.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace Burkard.Invoicing.Api.OData.Controllers;

[Route("odata")]
public class BulkDownloadController : ODataController
{
    private readonly AppDbContext _db;

    public BulkDownloadController(AppDbContext db) => _db = db;

    [HttpPost("BulkDownloadDocuments")]
    public async Task<IActionResult> Bulk([FromBody] BulkDownloadRequest request)
    {
        if (request.Kind is not ("Normal" or "Copy"))
        {
            return BadRequest(new
            {
                error = new { code = "InvalidKind", message = "Kind must be Normal or Copy." }
            });
        }

        var kind = request.Kind == "Normal" ? DocumentKind.Normal : DocumentKind.Copy;
        IQueryable<Document> query = _db.Documents.Where(d => d.Kind == kind);
        if (request.Year is not null && request.Half is not null)
        {
            query = query.Where(d => d.Invoice != null
                && d.Invoice.PeriodYear == request.Year && d.Invoice.PeriodHalf == request.Half);
        }

        var documents = await query
            .OrderBy(d => d.FileName)
            .ToListAsync();

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in documents)
            {
                var entry = zip.CreateEntry(document.FileName);
                await using var stream = entry.Open();
                await stream.WriteAsync(document.Content);
            }
        }

        var bytes = memory.ToArray();
        var fileName = request.Kind == "Normal" ? "rechnungen.zip" : "kopien.zip";
        return File(bytes, "application/zip", fileName);
    }

    public sealed class BulkDownloadRequest
    {
        public string Kind { get; set; } = "";
        public int? Year { get; set; }
        public int? Half { get; set; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/Invoicing.IntegrationTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: separate bulk downloads for normal documents and copies"
```

---

### Task 13: Frontend scaffold — UI5 TypeScript app, shell, navigation, E2E harness

**Files:**

- Create: `frontend/package.json`, `frontend/ui5.yaml`, `frontend/tsconfig.json`, `frontend/src/index.html`, `frontend/src/manifest.json`, `frontend/src/Component.ts`, `frontend/src/model/models.ts`, `frontend/src/controller/Base.controller.ts`, `frontend/src/controller/App.controller.ts`, `frontend/src/controller/Settings.controller.ts`, `frontend/src/controller/Items.controller.ts`, `frontend/src/controller/Recipients.controller.ts`, `frontend/src/controller/Invoices.controller.ts`, `frontend/src/controller/Graves.controller.ts`, `frontend/src/view/App.view.xml`, `frontend/src/view/Settings.view.xml`, `frontend/src/view/Items.view.xml`, `frontend/src/view/Recipients.view.xml`, `frontend/src/view/Invoices.view.xml`, `frontend/src/view/Graves.view.xml`, `frontend/src/view/fragments/Navigation.fragment.xml`, `frontend/src/i18n/i18n.properties`, `e2e/package.json`, `e2e/playwright.config.ts`, `e2e/tests/app.spec.ts`
- Modify: `docker-compose.yml` (add `frontend` service)

**Interfaces:**

- Consumes: backend at `/odata` (proxied in dev).
- Produces: app namespace `burkard.invoicing`; routes `settings` (default), `items`, `recipients`, `invoices`, `graves`; `models.createODataModel()`; `models.bindRoot(view)`; `BaseController.onNavSelect(event)`; German i18n bundle `burkard.invoicing.i18n.i18n`.

- [ ] **Step 1: Write the failing E2E smoke test**

`e2e/package.json`:

```json
{
  "name": "invoicing-e2e",
  "private": true,
  "scripts": {
    "test": "playwright test"
  },
  "devDependencies": {
    "@playwright/test": "^1.50.0"
  }
}
```

`e2e/playwright.config.ts`:

```ts
import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 60000,
  use: {
    baseURL: "http://localhost:8080",
  },
});
```

`e2e/tests/app.spec.ts`:

```ts
import { test, expect } from "@playwright/test";

test("app loads and navigation works", async ({ page }) => {
  await page.goto("/index.html");
  await expect(page.locator("body")).toContainText("Einstellungen");

  await page.locator("li.sapTntNavLI", { hasText: "Rechnungen" }).click();
  await expect(page).toHaveURL(/invoices/);

  await page.locator("li.sapTntNavLI", { hasText: "Gräber" }).click();
  await expect(page).toHaveURL(/graves/);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd e2e && npm install && npx playwright install chromium && cd .. && docker compose up -d --build && cd e2e && npx playwright test`
Expected: FAIL — port 8080 has nothing (or connection refused).

- [ ] **Step 3: Create the frontend project files**

`frontend/package.json`:

```json
{
  "name": "burkard.invoicing",
  "version": "0.0.1",
  "private": true,
  "scripts": {
    "start": "ui5 serve",
    "build": "ui5 build --clean-dest",
    "typecheck": "tsc --noEmit"
  },
  "devDependencies": {
    "@openui5/ts-types": "~1.132.0",
    "@ui5/cli": "^4.0.0",
    "typescript": "^5.5.0",
    "ui5-middleware-simpleproxy": "^3.0.0",
    "ui5-tooling-transpile": "^3.0.0"
  }
}
```

`frontend/ui5.yaml`:

```yaml
specVersion: "3.2"
metadata:
  name: burkard.invoicing
type: application
framework:
  name: OpenUI5
  version: "1.132.0"
  libraries:
    - name: sap.m
    - name: sap.ui.core
    - name: sap.ui.layout
    - name: sap.tnt
    - name: themelib_sap_horizon
builder:
  customTasks:
    - name: ui5-tooling-transpile-task
      afterTask: replaceVersion
      configuration:
        debug: true
        transpileTypeScript: true
server:
  customMiddleware:
    - name: ui5-tooling-transpile-middleware
      afterMiddleware: compression
    - name: ui5-middleware-simpleproxy
      afterMiddleware: compression
      mountPath: /odata
      configuration:
        target: "http://backend:8080"
```

`frontend/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ES2022",
    "moduleResolution": "node",
    "skipLibCheck": true,
    "allowJs": true,
    "strict": true,
    "noImplicitAny": true,
    "noUnusedParameters": true,
    "noUnusedLocals": true,
    "types": ["@openui5/ts-types"]
  },
  "include": ["./src/**/*"]
}
```

`frontend/src/index.html`:

```html
<!DOCTYPE html>
<html>
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Rechnungen</title>
    <script
      id="sap-ui-bootstrap"
      src="resources/sap-ui-core.js"
      data-sap-ui-theme="sap_horizon"
      data-sap-ui-resourceroots='{"burkard.invoicing": "./"}'
      data-sap-ui-oninit="module:sap/ui/core/ComponentSupport"
      data-sap-ui-compatVersion="edge"
      data-sap-ui-async="true"
      data-sap-ui-frameOptions="trusted"
    ></script>
  </head>
  <body class="sapUiBody" id="content"></body>
</html>
```

`frontend/src/manifest.json`:

```json
{
  "_version": "1.58.0",
  "sap.app": {
    "id": "burkard.invoicing",
    "type": "application",
    "i18n": "i18n/i18n.properties",
    "applicationVersion": { "version": "0.0.1" },
    "title": "{{appTitle}}",
    "description": "{{appDescription}}",
    "dataSources": {
      "invoicing": {
        "uri": "/odata/",
        "type": "OData",
        "settings": { "odataVersion": "4.0" }
      }
    }
  },
  "sap.ui5": {
    "rootView": {
      "viewName": "burkard.invoicing.view.App",
      "type": "XML",
      "async": true,
      "id": "app"
    },
    "dependencies": {
      "minUI5Version": "1.132.0",
      "libs": {
        "sap.m": {},
        "sap.ui.core": {},
        "sap.ui.layout": {},
        "sap.tnt": {}
      }
    },
    "contentDensities": { "compact": true, "cozy": true },
    "models": {
      "i18n": {
        "type": "sap.ui.model.resource.ResourceModel",
        "settings": { "bundleName": "burkard.invoicing.i18n.i18n" }
      }
    },
    "routing": {
      "config": {
        "routerClass": "sap.m.routing.Router",
        "viewPath": "burkard.invoicing.view",
        "controlId": "app",
        "controlAggregation": "pages",
        "async": true
      },
      "routes": [
        { "name": "settings", "pattern": "", "target": ["settings"] },
        { "name": "items", "pattern": "items", "target": ["items"] },
        {
          "name": "recipients",
          "pattern": "recipients",
          "target": ["recipients"]
        },
        { "name": "invoices", "pattern": "invoices", "target": ["invoices"] },
        { "name": "graves", "pattern": "graves", "target": ["graves"] }
      ],
      "targets": {
        "settings": { "viewName": "Settings", "viewLevel": 0 },
        "items": { "viewName": "Items", "viewLevel": 0 },
        "recipients": { "viewName": "Recipients", "viewLevel": 0 },
        "invoices": { "viewName": "Invoices", "viewLevel": 0 },
        "graves": { "viewName": "Graves", "viewLevel": 0 }
      }
    }
  }
}
```

`frontend/src/i18n/i18n.properties`:

```
appTitle=Rechnungen
appDescription=Rechnungsverwaltung für Gräber
menuSettings=Einstellungen
menuItems=Leistungen
menuRecipients=Empfänger
menuInvoices=Rechnungen
menuGraves=Gräber
```

`frontend/src/Component.ts`:

```ts
import UIComponent from "sap/ui/core/UIComponent";
import models from "./model/models";

export default class Component extends UIComponent {
  public static metadata = {
    manifest: "json",
    interfaces: ["sap.ui.core.IAsyncContentCreation"],
  };

  public init(): void {
    super.init();
    this.setModel(models.createODataModel(), "odata");
    this.getRouter().initialize();
  }
}
```

`frontend/src/model/models.ts`:

```ts
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import type Context from "sap/ui/model/odata/v4/Context";
import type View from "sap/ui/core/mvc/View";

export default {
  createODataModel(): ODataModel {
    return new ODataModel({
      serviceUrl: "/odata/",
      synchronizationMode: "None",
    });
  },

  bindRoot(view: View): void {
    const model = view.getModel("odata") as ODataModel;
    const context = model.createBindingContext("/") as Context;
    view.setBindingContext(context, "odata");
  },
};
```

`frontend/src/controller/Base.controller.ts`:

```ts
import Controller from "sap/ui/core/mvc/Controller";
import type Event from "sap/ui/base/Event";
import Router from "sap/m/routing/Router";
import UIComponent from "sap/ui/core/UIComponent";

export default abstract class BaseController extends Controller {
  public onNavSelect(event: Event): void {
    const key = (event.getParameter("item") as { getKey(): string }).getKey();
    if (key) {
      (UIComponent.getRouterFor(this) as Router).navTo(key);
    }
  }
}
```

`frontend/src/controller/App.controller.ts`:

```ts
import BaseController from "./Base.controller";
import models from "../model/models";

export default class App extends BaseController {
  public onInit(): void {
    models.bindRoot(this.getView()!);
  }
}
```

`frontend/src/view/App.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  controllerName="burkard.invoicing.controller.App"
  displayBlock="true"
>
    <App id="app" />
</mvc:View>
```

`frontend/src/view/fragments/Navigation.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m" xmlns:tnt="sap.tnt">
    <tnt:NavigationList id="navList" itemSelect="onNavSelect">
        <tnt:NavigationListItem text="{i18n>menuSettings}" key="settings" />
        <tnt:NavigationListItem text="{i18n>menuItems}" key="items" />
        <tnt:NavigationListItem text="{i18n>menuRecipients}" key="recipients" />
        <tnt:NavigationListItem text="{i18n>menuInvoices}" key="invoices" />
        <tnt:NavigationListItem text="{i18n>menuGraves}" key="graves" />
    </tnt:NavigationList>
</core:FragmentDefinition>
```

The five view files start as identical shells (toolpage with the navigation fragment + a titled page). `Settings.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  controllerName="burkard.invoicing.controller.Settings"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuSettings}" titleLevel="H1" />
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

(For `Items`, `Recipients`, `Invoices`, `Graves`: same file with the matching controller name and title binding.)

The five controllers start as:

```ts
import BaseController from "./Base.controller";

export default class Settings extends BaseController {}
```

(name and class per view: `Items`, `Recipients`, `Invoices`, `Graves`.)

Compose addition — extend `docker-compose.yml`:

```yaml
  frontend:
    image: node:22
    working_dir: /app/frontend
    volumes:
      - ./frontend:/app/frontend
      - frontend-node-modules:/app/frontend/node_modules
    command: ["sh", "-c", "npm install && npx ui5 serve --port 8080"]
    ports:
      - "8080:8080"
volumes:
  invoicing-data:
  frontend-node-modules:
```

- [ ] **Step 4: Run the E2E test to verify it passes**

Run: `docker compose up -d --build && cd e2e && npx playwright test`
Expected: PASS. Also run `cd frontend && npm run typecheck` — expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend e2e docker-compose.yml
git commit -m "feat: UI5 TypeScript app shell with routing and E2E harness"
```

---

### Task 14: Settings, Items, and Recipients views (Stories 1–3)

**Files:**

- Modify: `frontend/src/view/Settings.view.xml`, `frontend/src/controller/Settings.controller.ts`, `frontend/src/view/Items.view.xml`, `frontend/src/controller/Items.controller.ts`, `frontend/src/view/Recipients.view.xml`, `frontend/src/controller/Recipients.controller.ts`, `frontend/src/model/api.ts` (create), `frontend/src/i18n/i18n.properties` (add keys), `frontend/src/view/fragments/ItemDialog.fragment.xml` (create), `frontend/src/view/fragments/RecipientDialog.fragment.xml` (create)
- Test: `e2e/tests/masterdata.spec.ts`

**Interfaces:**

- Consumes: `/odata/Settings` (GET/PATCH), `/odata/VatRates`, `/odata/InvoiceItems`, `/odata/Recipients`; `BaseController`.
- Produces: `api.patchSettings`, `api.createVatRate`, `api.deleteVatRate`, `api.createItem`, `api.createRecipient`, `api.showError(controller, code, fallbackMessage)` (maps known error codes to German i18n texts, MessageBox otherwise).

- [ ] **Step 1: Write the failing E2E tests**

`e2e/tests/masterdata.spec.ts`:

```ts
import { test, expect } from "@playwright/test";

test("settings: stamp word validation and VAT rate (Story 1)", async ({ page }) => {
  await page.goto("/index.html");

  await page.getByLabel("Stempelwort").fill("");
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(page.locator(".sapMMessageToast, .sapMDialog")).toContainText("Stempelwort");

  await page.getByLabel("Stempelwort").fill("Kopie");
  await page.getByLabel("VAT-Satz").fill("7");
  await page.getByRole("button", { name: "VAT-Satz hinzufügen" }).click();
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator(".sapMListTbl")).toContainText("7");
});

test("items: create an item without default price (Story 2)", async ({ page }) => {
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.goto("/index.html#/items");
  await page.getByRole("button", { name: "Leistung anlegen" }).click();
  await page.getByLabel("Nummer").fill("1001");
  await page.getByLabel("Bezeichnung").fill("Grabpflege");
  await page.getByLabel("VAT-Satz").fill("7");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator("table")).toContainText("1001");
  await expect(page.locator("table")).toContainText("Grabpflege");
});

test("recipients: create and search a recipient (Story 3)", async ({ page }) => {
  await page.goto("/index.html#/recipients");
  await page.getByRole("button", { name: "Empfänger anlegen" }).click();
  await page.getByLabel("Name").fill("Familie Mahr");
  await page.getByLabel("Adresse").fill("Musterstraße 1\n12345 Musterstadt");
  await page.getByRole("button", { name: "Speichern" }).click();

  await page.getByRole("searchbox").fill("Mahr");
  await expect(page.locator("table")).toContainText("Familie Mahr");
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd e2e && npx playwright test masterdata`
Expected: FAIL — buttons/fields do not exist yet.

- [ ] **Step 3: Implement API helpers and i18n keys**

`frontend/src/model/api.ts`:

```ts
import type Controller from "sap/ui/core/mvc/Controller";
import MessageBox from "sap/m/MessageBox";
import ResourceBundle from "sap/base/i18n/ResourceBundle";
import ResourceModel from "sap/ui/model/resource/ResourceModel";

function toJson(response: Response): Promise<any> {
  return response.json().catch(() => ({}));
}

async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  const body = await toJson(response);
  throw new Error(body?.error?.code ?? `HTTP ${response.status}`);
}

function post(url: string, payload: unknown): Promise<Response> {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

const CODE_TO_I18N: Record<string, string> = {
  StampWordRequired: "errorStampWordRequired",
  PriceRequired: "errorPriceRequired",
  RecipientAddressRequired: "errorRecipientAddressRequired",
  ItemNumberExists: "errorItemNumberExists",
  VatRateExists: "errorVatRateExists",
  VatRateNotConfigured: "errorVatRateNotConfigured",
  InvalidVatRate: "errorInvalidVatRate",
  GravesAlreadyInvoiced: "errorGravesAlreadyInvoiced",
  JobAlreadyRunning: "errorJobAlreadyRunning",
};

export async function showError(controller: Controller, error: unknown): Promise<void> {
  const raw = error instanceof Error ? error.message : String(error);
  const key = CODE_TO_I18N[raw] ?? "errorGeneric";
  const bundle = (
    controller.getOwnerComponent()!.getModel("i18n") as ResourceModel
  ).getResourceBundle() as ResourceBundle;
  let text = bundle.getText(key);
  if (key === "errorGeneric") {
    text = `${text} (${raw})`;
  }

  await MessageBox.error(text);
}

export async function patchSettings(payload: Record<string, unknown>): Promise<void> {
  const response = await fetch("/odata/Settings", {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  await ensureOk(response);
}

export async function createVatRate(rate: number): Promise<void> {
  await ensureOk(await post("/odata/VatRates", { Rate: rate }));
}

export async function deleteVatRate(id: number): Promise<void> {
  await ensureOk(await fetch(`/odata/VatRates(${id})`, { method: "DELETE" }));
}

export async function createItem(item: {
  Number: string;
  Description: string;
  VatRate: number;
  DefaultPrice?: number | null;
}): Promise<void> {
  await ensureOk(
    await post("/odata/InvoiceItems", {
      ...item,
      DefaultPrice: item.DefaultPrice ?? null,
    }),
  );
}

export async function createRecipient(recipient: { Name: string; Address: string }): Promise<void> {
  await ensureOk(await post("/odata/Recipients", recipient));
}
```

Add to `i18n.properties`:

```
errorStampWordRequired=Das Stempelwort darf nicht leer sein.
errorPriceRequired=Zu dieser Leistung fehlt ein Preis.
errorRecipientAddressRequired=Die Adresse des Empfängers fehlt.
errorItemNumberExists=Diese Leistungsnummer existiert bereits.
errorVatRateExists=Dieser VAT-Satz ist bereits konfiguriert.
errorVatRateNotConfigured=Dieser VAT-Satz ist nicht konfiguriert.
errorInvalidVatRate=Der VAT-Satz muss positiv sein.
errorGravesAlreadyInvoiced=Für dieses Halbjahr wurden bereits Rechnungen erstellt.
errorJobAlreadyRunning=Es läuft bereits eine Aufgabe.
errorGeneric=Ein Fehler ist aufgetreten.
save=Speichern
settingsStampWord=Stempelwort
settingsEndingText=Schlusstext
settingsVatRates=VAT-Sätze
vatRate=VAT-Satz
addVatRate=VAT-Satz hinzufügen
```

- [ ] **Step 4: Implement the Settings view and controller**

`Settings.view.xml` (replaces the shell from Task 13):

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  xmlns:form="sap.ui.layout.form"
  controllerName="burkard.invoicing.controller.Settings"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuSettings}" titleLevel="H1">
                <content>
                    <Panel headerText="{i18n>menuSettings}">
                        <form:SimpleForm editable="true" layout="ResponsiveGridLayout">
                            <Label text="{i18n>settingsStampWord}" labelFor="stampWord" />
                            <Input id="stampWord" value="{odata>StampWord}" width="20rem" />
                            <Label text="{i18n>settingsEndingText}" labelFor="endingText" />
                            <TextArea
                id="endingText"
                value="{odata>EndingText}"
                rows="6"
                width="100%"
              />
                        </form:SimpleForm>
                    </Panel>
                    <Panel headerText="{i18n>settingsVatRates}">
                        <content>
                            <Table
                id="vatRates"
                items="{odata>/VatRates}"
                noDataText="{i18n>noData}"
              >
                                <columns>
                                    <Column><Text text="{i18n>vatRate}" /></Column>
                                    <Column />
                                </columns>
                                <items>
                                    <ColumnListItem>
                                        <cells>
                                            <Text text="{odata>Rate}" />
                                            <Button
                        text="{i18n>delete}"
                        press="onDeleteVatRate"
                        type="Transparent"
                      />
                                        </cells>
                                    </ColumnListItem>
                                </items>
                            </Table>
                            <HBox class="sapUiMediumMarginTop">
                                <Label text="{i18n>vatRate}" labelFor="newVatRate" />
                                <Input id="newVatRate" type="Number" width="8rem" />
                                <Button text="{i18n>addVatRate}" press="onAddVatRate" />
                            </HBox>
                        </content>
                    </Panel>
                    <Button id="save" text="{i18n>save}" type="Emphasized" press="onSave" />
                </content>
            </Page>
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

`Settings.controller.ts`:

```ts
import BaseController from "./Base.controller";
import * as api from "../model/api";
import Context from "sap/ui/model/odata/v4/Context";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";

export default class Settings extends BaseController {
  public onInit(): void {
    const view = this.getView()!;
    const model = view.getModel("odata") as ODataModel;
    // bind the view to the /Settings singleton; tables use absolute paths
    const context = model.createBindingContext("/Settings") as Context;
    view.setBindingContext(context, "odata");
  }

  public async onSave(): Promise<void> {
    const context = this.getView()!.getBindingContext("odata") as Context;
    try {
      await api.patchSettings({
        StampWord: context.getProperty("StampWord"),
        EndingText: context.getProperty("EndingText"),
      });
      await MessageBox.information(
        (this.getView()!.getModel("i18n") as any).getResourceBundle().getText("saved"),
      );
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onAddVatRate(): Promise<void> {
    const input = this.byId("newVatRate") as Input;
    const rate = Number(input.getValue());
    if (!rate || rate <= 0) {
      await api.showError(this, new Error("InvalidVatRate"));
      return;
    }

    try {
      await api.createVatRate(rate);
      input.setValue("");
      this.refreshVatRates();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onDeleteVatRate(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    const id = context.getProperty("Id") as number;
    try {
      await api.deleteVatRate(id);
      this.refreshVatRates();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  private refreshVatRates(): void {
    const table = this.byId("vatRates");
    const binding = (table as any).getBinding("items") as ODataListBinding;
    binding.refresh();
  }
}
```

- [ ] **Step 5: Implement Items and Recipients views + dialog fragments**

`Items.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  controllerName="burkard.invoicing.controller.Items"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuItems}" titleLevel="H1">
                <subHeader>
                    <Toolbar>
                        <SearchField
              width="16rem"
              search="onSearch"
              placeholder="{i18n>searchByNumber}"
            />
                        <ToolbarSpacer />
                        <Button text="{i18n>createItem}" press="onCreateItem" type="Emphasized" />
                    </Toolbar>
                </subHeader>
                <content>
                    <Table id="itemsTable" items="{odata>/InvoiceItems}">
                        <columns>
                            <Column><Text text="{i18n>itemNumber}" /></Column>
                            <Column><Text text="{i18n>itemDescription}" /></Column>
                            <Column><Text text="{i18n>vatRate}" /></Column>
                            <Column><Text text="{i18n>itemDefaultPrice}" /></Column>
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{odata>Number}" />
                                    <Text text="{odata>Description}" />
                                    <Text text="{odata>VatRate}" />
                                    <Text text="{odata>DefaultPrice}" />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                </content>
            </Page>
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

`Items.controller.ts`:

```ts
import BaseController from "./Base.controller";
import models from "../model/models";
import * as api from "../model/api";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Fragment from "sap/ui/core/Fragment";
import Input from "sap/m/Input";

export default class Items extends BaseController {
  public onInit(): void {
    models.bindRoot(this.getView()!);
  }

  public onSearch(event: any): void {
    const term = event.getParameter("query") as string;
    const binding = (this.byId("itemsTable") as any).getBinding("items") as ODataListBinding;
    if (!term) {
      binding.filter([]);
      return;
    }

    binding.filter(
      new Filter({
        filters: [
          new Filter("Number", FilterOperator.Contains, term),
          new Filter("Description", FilterOperator.Contains, term),
        ],
        and: false,
      }),
    );
  }

  public async onCreateItem(): Promise<void> {
    const dialog = (await Fragment.load({
      name: "burkard.invoicing.view.fragments.ItemDialog",
      controller: this,
    })) as any;
    (this.getView() as any).addDependent(dialog);
    dialog.open();
  }

  public async onDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      await api.createItem({
        Number: (this.byId("itemNumber") as Input).getValue(),
        Description: (this.byId("itemDescription") as Input).getValue(),
        VatRate: Number((this.byId("itemVatRate") as Input).getValue()),
        DefaultPrice: (this.byId("itemDefaultPrice") as Input).getValue()
          ? Number((this.byId("itemDefaultPrice") as Input).getValue())
          : null,
      });
      dialog.close();
      ((this.byId("itemsTable") as any).getBinding("items") as ODataListBinding).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }
}
```

`ItemDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <Dialog title="{i18n>createItem}">
        <content>
            <VBox class="sapUiContentPadding">
                <Label text="{i18n>itemNumber}" labelFor="itemNumber" />
                <Input id="itemNumber" width="20rem" />
                <Label text="{i18n>itemDescription}" labelFor="itemDescription" />
                <Input id="itemDescription" width="20rem" />
                <Label text="{i18n>vatRate}" labelFor="itemVatRate" />
                <Input id="itemVatRate" type="Number" width="20rem" />
                <Label text="{i18n>itemDefaultPrice}" labelFor="itemDefaultPrice" />
                <Input id="itemDefaultPrice" type="Number" width="20rem" />
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>save}" type="Emphasized" press="onDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press="onDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

`Recipients.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  controllerName="burkard.invoicing.controller.Recipients"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuRecipients}" titleLevel="H1">
                <subHeader>
                    <Toolbar>
                        <SearchField
              width="16rem"
              search="onSearch"
              placeholder="{i18n>searchByName}"
            />
                        <ToolbarSpacer />
                        <Button
              text="{i18n>createRecipient}"
              press="onCreateRecipient"
              type="Emphasized"
            />
                    </Toolbar>
                </subHeader>
                <content>
                    <Table id="recipientsTable" items="{odata>/Recipients}">
                        <columns>
                            <Column><Text text="{i18n>recipientName}" /></Column>
                            <Column><Text text="{i18n>recipientAddress}" /></Column>
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{odata>Name}" />
                                    <Text text="{odata>Address}" />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                </content>
            </Page>
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

`Recipients.controller.ts`:

```ts
import BaseController from "./Base.controller";
import models from "../model/models";
import * as api from "../model/api";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Fragment from "sap/ui/core/Fragment";
import Input from "sap/m/Input";

export default class Recipients extends BaseController {
  public onInit(): void {
    models.bindRoot(this.getView()!);
  }

  public onSearch(event: any): void {
    const term = event.getParameter("query") as string;
    const binding = (this.byId("recipientsTable") as any).getBinding("items") as ODataListBinding;
    if (!term) {
      binding.filter([]);
      return;
    }

    binding.filter(new Filter("Name", FilterOperator.Contains, term));
  }

  public async onCreateRecipient(): Promise<void> {
    const dialog = (await Fragment.load({
      name: "burkard.invoicing.view.fragments.RecipientDialog",
      controller: this,
    })) as any;
    (this.getView() as any).addDependent(dialog);
    dialog.open();
  }

  public async onDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      await api.createRecipient({
        Name: (this.byId("recipientName") as Input).getValue(),
        Address: (this.byId("recipientAddress") as Input).getValue(),
      });
      dialog.close();
      ((this.byId("recipientsTable") as any).getBinding("items") as ODataListBinding).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }
}
```

`RecipientDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <Dialog title="{i18n>createRecipient}">
        <content>
            <VBox class="sapUiContentPadding">
                <Label text="{i18n>recipientName}" labelFor="recipientName" />
                <Input id="recipientName" width="20rem" />
                <Label text="{i18n>recipientAddress}" labelFor="recipientAddress" />
                <TextArea id="recipientAddress" rows="4" width="20rem" />
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>save}" type="Emphasized" press="onDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press="onDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

Add to `i18n.properties`:

```
noData=Keine Daten
delete=Löschen
saved=Gespeichert.
cancel=Abbrechen
searchByNumber=Nach Nummer suchen
createItem=Leistung anlegen
itemNumber=Nummer
itemDescription=Bezeichnung
itemDefaultPrice=Standardpreis
searchByName=Nach Name suchen
createRecipient=Empfänger anlegen
recipientName=Name
recipientAddress=Adresse
```

- [ ] **Step 6: Run E2E tests to verify they pass**

Run: `cd frontend && npm run typecheck && cd ../e2e && npx playwright test masterdata`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add frontend e2e
git commit -m "feat: settings, items and recipients views"
```

---

### Task 15: Invoices view — create, edit (revision), downloads (Stories 4, 8, 9)

**Files:**

- Modify: `frontend/src/view/Invoices.view.xml`, `frontend/src/controller/Invoices.controller.ts`, `frontend/src/model/api.ts` (add `createInvoice`, `createRevision`, `fetchDocuments`, `bulkDownload`, `downloadBlob`), `frontend/src/i18n/i18n.properties` (add keys)
- Create: `frontend/src/view/fragments/InvoiceDialog.fragment.xml`
- Test: `e2e/tests/invoices.spec.ts`

**Interfaces:**

- Consumes: `POST /odata/CreateInvoice`, `POST /odata/CreateRevision`, `GET /odata/Invoices({id})/Documents`, `GET /odata/Invoices({id})/Lines`, `GET /odata/Documents({id})/$value`, `POST /odata/BulkDownloadDocuments`.
- Produces: `api.createInvoice(recipientId, lines)`, `api.createRevision(invoiceId, recipientId, lines)`, `api.fetchDocuments(invoiceId) → DocumentDto[]`, `api.bulkDownload(kind)` (triggers browser download), `api.downloadBlob`.

- [ ] **Step 1: Write the failing E2E tests**

`e2e/tests/invoices.spec.ts`:

```ts
import { test, expect } from "@playwright/test";

async function seedMasterData(page: import("@playwright/test").Page): Promise<void> {
  // direct API calls keep the test focused on the UI flow
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("http://localhost:8080/odata/InvoiceItems", {
    data: {
      Number: "9001",
      Description: "Grabpflege",
      VatRate: 7,
      DefaultPrice: null,
    },
  });
  await page.request.post("http://localhost:8080/odata/Recipients", {
    data: {
      Name: "Familie Mahr",
      Address: "Musterstraße 1\n12345 Musterstadt",
    },
  });
}

test("create an invoice with a mandatory price and download documents (Story 4, 8)", async ({
  page,
}) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await page.locator("[id$='invoiceDialog--recipient']").click(); // Select opens popover
  await page.locator("li.sapMSelectListItem", { hasText: "Familie Mahr" }).click();
  await page.locator("[id$='lineItem']").click();
  await page.locator("li.sapMSelectListItem", { hasText: "Grabpflege" }).click();
  await page.getByLabel("Anzahl").fill("2");
  await page.getByLabel("Preis").fill("120");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator("table")).toContainText(/2026-0001/);

  const downloadPromise = page.waitForEvent("download");
  await page.locator("table").getByText("Rechnung").first().click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toContain(".pdf");
  expect(download.suggestedFilename()).not.toContain("Kopie");
});

test("invoice without price cannot be saved (Story 4.3/edge case)", async ({ page }) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await page.locator("[id$='invoiceDialog--recipient']").click();
  await page.locator("li.sapMSelectListItem", { hasText: "Familie Mahr" }).click();
  await page.locator("[id$='lineItem']").click();
  await page.locator("li.sapMSelectListItem", { hasText: "Grabpflege" }).click();
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator(".sapMDialog")).toContainText("Preis");
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd e2e && npx playwright test invoices`
Expected: FAIL — "Rechnung erstellen" button does not exist.

- [ ] **Step 3: Extend the API helper**

Add to `frontend/src/model/api.ts`:

```ts
export interface InvoiceLineDto {
  itemId: number;
  count: number;
  unitPrice: number | null;
}

export interface DocumentDto {
  Id: number;
  FileName: string;
}

export async function createInvoice(recipientId: number, lines: InvoiceLineDto[]): Promise<void> {
  await ensureOk(await post("/odata/CreateInvoice", { recipientId, lines }));
}

export async function createRevision(
  invoiceId: number,
  recipientId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(await post("/odata/CreateRevision", { invoiceId, recipientId, lines }));
}

export async function fetchDocuments(invoiceId: number): Promise<DocumentDto[]> {
  const response = await fetch(`/odata/Invoices(${invoiceId})/Documents`);
  await ensureOk(response);
  const body = await response.json();
  return body.value as DocumentDto[];
}

export function downloadUrl(documentId: number): string {
  return `/odata/Documents(${documentId})/$value`;
}

export async function bulkDownload(kind: "Normal" | "Copy", fileName: string): Promise<void> {
  const response = await post("/odata/BulkDownloadDocuments", { kind });
  await ensureOk(response);
  const blob = await response.blob();
  await downloadBlob(blob, fileName);
}

export async function downloadBlob(blob: Blob, fileName: string): Promise<void> {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
```

- [ ] **Step 4: Implement the Invoices view**

`Invoices.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  controllerName="burkard.invoicing.controller.Invoices"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuInvoices}" titleLevel="H1">
                <subHeader>
                    <Toolbar>
                        <Select id="halfYearFilter" width="12rem" change="onHalfYearChange">
                            <core:Item key="" text="{i18n>allInvoices}" />
                            <core:Item key="2026-1" text="H1 2026" />
                            <core:Item key="2026-2" text="H2 2026" />
                            <core:Item key="2025-1" text="H1 2025" />
                            <core:Item key="2025-2" text="H2 2025" />
                        </Select>
                        <ToolbarSpacer />
                        <Button text="{i18n>bulkNormal}" press="onBulkNormal" />
                        <Button text="{i18n>bulkCopy}" press="onBulkCopy" />
                        <Button
              text="{i18n>createInvoice}"
              press="onCreateInvoice"
              type="Emphasized"
            />
                    </Toolbar>
                </subHeader>
                <content>
                    <Table id="invoicesTable" items="{odata>/Invoices}">
                        <columns>
                            <Column><Text text="{i18n>invoiceNumber}" /></Column>
                            <Column><Text text="{i18n>recipientName}" /></Column>
                            <Column><Text text="{i18n>invoiceDate}" /></Column>
                            <Column><Text text="{i18n>revision}" /></Column>
                            <Column />
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{odata>InvoiceNumber}" />
                                    <Text text="{odata>RecipientName}" />
                                    <Text text="{odata>GeneratedAt}" />
                                    <Text text="{odata>RevisionNumber}" />
                                    <HBox>
                                        <Button
                      text="{i18n>downloadNormal}"
                      press="onDownloadNormal"
                      type="Transparent"
                    />
                                        <Button
                      text="{i18n>downloadCopy}"
                      press="onDownloadCopy"
                      type="Transparent"
                    />
                                        <Button
                      text="{i18n>editInvoice}"
                      press="onEditInvoice"
                      type="Transparent"
                    />
                                    </HBox>
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                </content>
            </Page>
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

`Invoices.controller.ts`:

```ts
import BaseController from "./Base.controller";
import models from "../model/models";
import * as api from "../model/api";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Context from "sap/ui/model/odata/v4/Context";
import Fragment from "sap/ui/core/Fragment";
import Select from "sap/m/Select";
import JSONModel from "sap/ui/model/json/JSONModel";

export default class Invoices extends BaseController {
  public onInit(): void {
    models.bindRoot(this.getView()!);
  }

  public onHalfYearChange(event: any): void {
    const key = (event.getSource() as Select).getSelectedKey();
    const binding = (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding;
    if (!key) {
      binding.filter([]);
      return;
    }

    const [year, half] = key.split("-").map(Number);
    binding.filter(
      new Filter({
        filters: [
          new Filter("PeriodYear", FilterOperator.EQ, year),
          new Filter("PeriodHalf", FilterOperator.EQ, half),
        ],
        and: true,
      }),
    );
  }

  public async onCreateInvoice(): Promise<void> {
    await this.openInvoiceDialog(null);
  }

  public async onEditInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openInvoiceDialog(context);
  }

  private async openInvoiceDialog(invoiceContext: Context | null): Promise<void> {
    if (!this._dialog) {
      this._dialog = (await Fragment.load({
        name: "burkard.invoicing.view.fragments.InvoiceDialog",
        controller: this,
      })) as any;
      (this.getView() as any).addDependent(this._dialog);
    }

    const lines = invoiceContext
      ? await this.loadLines(invoiceContext.getProperty("Id") as number)
      : [];
    this._dialog.setModel(
      new JSONModel({
        recipientId: invoiceContext?.getProperty("RecipientId") ?? null,
        invoiceId: invoiceContext?.getProperty("Id") ?? null,
        lines,
      }),
      "invoiceDraft",
    );
    this._dialog.open();
  }

  private async loadLines(invoiceId: number): Promise<any[]> {
    const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
    const body = await response.json();
    return (body.value as any[]).map((line) => ({
      itemId: line.ItemId,
      count: line.Count,
      unitPrice: line.UnitPrice,
    }));
  }

  public onAddLine(event: any): void {
    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const lines = (model.getProperty("/lines") as any[]) ?? [];
    lines.push({ itemId: null, count: 1, unitPrice: null });
    model.setProperty("/lines", lines);
  }

  public async onInvoiceDialogSave(): Promise<void> {
    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const lines = (draft.lines as any[]).map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      unitPrice: line.unitPrice ? Number(line.unitPrice) : null,
    }));

    try {
      if (draft.invoiceId) {
        await api.createRevision(Number(draft.invoiceId), Number(draft.recipientId), lines);
      } else {
        await api.createInvoice(Number(draft.recipientId), lines);
      }

      this._dialog.close();
      ((this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onInvoiceDialogCancel(): void {
    this._dialog.close();
  }

  public async onDownloadNormal(event: any): Promise<void> {
    await this.download(event.getSource().getBindingContext("odata") as Context, false);
  }

  public async onDownloadCopy(event: any): Promise<void> {
    await this.download(event.getSource().getBindingContext("odata") as Context, true);
  }

  private async download(context: Context, isCopy: boolean): Promise<void> {
    const invoiceId = context.getProperty("Id") as number;
    const documents = await api.fetchDocuments(invoiceId);
    const match = documents.find((d) => d.FileName.includes("Kopie") === isCopy);
    if (match) {
      window.open(api.downloadUrl(match.Id), "_blank");
    }
  }

  public async onBulkNormal(): Promise<void> {
    await api.bulkDownload("Normal", "rechnungen.zip");
  }

  public async onBulkCopy(): Promise<void> {
    await api.bulkDownload("Copy", "kopien.zip");
  }

  private _dialog: any;
}
```

`InvoiceDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <Dialog id="invoiceDialog" title="{i18n>createInvoice}" stretch="true">
        <content>
            <VBox class="sapUiContentPadding">
                <Label text="{i18n>recipientName}" labelFor="recipient" />
                <Select
          id="recipient"
          items="{odata>/Recipients}"
          selectedKey="{invoiceDraft>/recipientId}"
        >
                    <core:Item key="{odata>Id}" text="{odata>Name}" />
                </Select>
                <Table id="lineTable" items="{invoiceDraft>/lines}">
                    <columns>
                        <Column><Text text="{i18n>itemNumber}" /></Column>
                        <Column><Text text="{i18n>itemCount}" /></Column>
                        <Column><Text text="{i18n>itemPrice}" /></Column>
                    </columns>
                    <items>
                        <ColumnListItem>
                            <cells>
                                <Select
                  id="lineItem"
                  items="{odata>/InvoiceItems}"
                  selectedKey="{invoiceDraft>itemId}"
                >
                                    <core:Item key="{odata>Id}" text="{odata>Number}" />
                                </Select>
                                <Input
                  value="{invoiceDraft>count}"
                  type="Number"
                  ariaLabel="{i18n>itemCount}"
                />
                                <Input
                  value="{invoiceDraft>unitPrice}"
                  type="Number"
                  ariaLabel="{i18n>itemPrice}"
                />
                            </cells>
                        </ColumnListItem>
                    </items>
                </Table>
                <Button text="{i18n>addLine}" press="onAddLine" />
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>save}" type="Emphasized" press="onInvoiceDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press="onInvoiceDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

Add to `i18n.properties`:

```
allInvoices=Alle Rechnungen
bulkNormal=Normale Rechnungen (ZIP)
bulkCopy=Kopien (ZIP)
createInvoice=Rechnung erstellen
editInvoice=Bearbeiten
invoiceNumber=Rechnungsnummer
invoiceDate=Datum
revision=Revision
downloadNormal=Rechnung
downloadCopy=Kopie
itemCount=Anzahl
itemPrice=Preis
addLine=Zeile hinzufügen
```

- [ ] **Step 5: Run E2E tests to verify they pass**

Run: `cd frontend && npm run typecheck && cd ../e2e && npx playwright test invoices`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend e2e
git commit -m "feat: invoice creation, revision and document downloads"
```

---

### Task 16: Graves view — management, half-year status, generation with blocking progress (Stories 5, 6, 13)

**Files:**

- Modify: `frontend/src/view/Graves.view.xml`, `frontend/src/controller/Graves.controller.ts`, `frontend/src/model/api.ts` (add `generateGraveInvoices`, `fetchGraveStatus`, `createGrave`, `deleteGraveLink`, `createGraveItemLink`, `createGraveRecipientLink`, `fetchGrave`), `frontend/src/i18n/i18n.properties` (add keys)
- Create: `frontend/src/view/fragments/GraveDialog.fragment.xml`, `frontend/src/view/fragments/GenerateDialog.fragment.xml`
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: `/odata/Graves`, `/odata/GraveItems`, `/odata/GraveRecipients`, `POST /odata/GenerateGraveInvoices`, `GET /odata/GraveInvoicingStatus`, `GET /odata/Jobs({id})`.
- Produces: `api.generateGraveInvoices(year, half) → JobDto`; `api.fetchGraveStatus(year, half) → { value: [{Id, GraveNumber, Invoiced}] }`; blocking progress helper `pollJob(jobId, onDone)` in the controller.

- [ ] **Step 1: Write the failing E2E tests**

`e2e/tests/graves.spec.ts`:

```ts
import { test, expect } from "@playwright/test";

async function seedManyGraves(page: import("@playwright/test").Page, count: number): Promise<void> {
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  const item = await (
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
      data: {
        Number: "8001",
        Description: "Grabpflege",
        VatRate: 7,
        DefaultPrice: 100,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("http://localhost:8080/odata/Recipients", {
      data: { Name: "Anna Beispiel", Address: "Hauptstraße 1" },
    })
  ).json();

  for (let i = 1; i <= count; i++) {
    const grave = await (
      await page.request.post("http://localhost:8080/odata/Graves", {
        data: {
          GraveNumber: `G-${String(i).padStart(3, "0")}`,
          StartDate: "2026-01-01",
          EndDate: "2040-12-31",
        },
      })
    ).json();
    await page.request.post("http://localhost:8080/odata/GraveItems", {
      data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
    });
    await page.request.post("http://localhost:8080/odata/GraveRecipients", {
      data: { GraveId: grave.Id, RecipientId: recipient.Id },
    });
  }
}

test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({
  page,
}) => {
  await seedManyGraves(page, 60);
  await page.goto("/index.html#/graves");
  await expect(page.locator("table")).toContainText("G-001");

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();

  // blocking progress indicator is visible while the job runs
  await expect(page.locator(".sapMBusyDialog")).toBeVisible({ timeout: 5000 });
  await expect(page.locator(".sapMBusyDialog")).not.toBeVisible({
    timeout: 90000,
  });

  // status column now shows all graves as invoiced
  await expect(page.locator("table").getByText("erstellt").first()).toBeVisible();

  // dismiss the success message, then a second generation is prevented
  await page.locator(".sapMDialog").getByRole("button", { name: "OK" }).click();
  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();
  await expect(page.locator(".sapMDialog")).toContainText("bereits");
});

test("create a grave and link item and recipient (Story 5)", async ({ page }) => {
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("http://localhost:8080/odata/InvoiceItems", {
    data: {
      Number: "8002",
      Description: "Pflege",
      VatRate: 7,
      DefaultPrice: 50,
    },
  });
  await page.request.post("http://localhost:8080/odata/Recipients", {
    data: { Name: "Bert Beispiel", Address: "Nebenweg 2" },
  });

  await page.goto("/index.html#/graves");
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await page.getByLabel("Grabnummer").fill("X-100");
  await page.getByLabel("Beginn").fill("2026-01-01");
  await page.getByLabel("Ende").fill("2040-12-31");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator("table")).toContainText("X-100");

  // open the edit dialog and add the item and recipient links
  await page.locator("table").getByText("Bearbeiten").first().click();
  await page.locator("[id$='linkItem']").click();
  await page.locator("li.sapMSelectListItem", { hasText: "8002" }).click();
  await page.locator("[id$='linkCount']").fill("1");
  await page.getByRole("button", { name: "Hinzufügen" }).first().click();
  await expect(page.locator(".sapMDialog")).toContainText("8002");

  await page.locator("[id$='linkRecipient']").click();
  await page.locator("li.sapMSelectListItem", { hasText: "Bert Beispiel" }).click();
  await page.getByRole("button", { name: "Hinzufügen" }).last().click();
  await expect(page.locator(".sapMDialog")).toContainText("Bert Beispiel");

  await page.getByRole("button", { name: "Speichern" }).click();
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd e2e && npx playwright test graves`
Expected: FAIL — "Rechnungen erzeugen" button does not exist.

- [ ] **Step 3: Extend the API helper**

Add to `frontend/src/model/api.ts`:

```ts
export interface JobDto {
  Id: number;
  Status: string;
  ProgressPercent: number;
  ResultJson?: string;
}

export async function generateGraveInvoices(year: number, half: number): Promise<JobDto> {
  const response = await post("/odata/GenerateGraveInvoices", { year, half });
  await ensureOk(response);
  return (await response.json()) as JobDto;
}

export async function fetchGraveStatus(year: number, half: number): Promise<any[]> {
  const response = await fetch(`/odata/GraveInvoicingStatus?year=${year}&half=${half}`);
  await ensureOk(response);
  const body = await response.json();
  return body.value as any[];
}

export async function createGrave(grave: {
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
}): Promise<number> {
  const response = await post("/odata/Graves", grave);
  await ensureOk(response);
  const body = await response.json();
  return body.Id as number;
}

export async function createGraveItemLink(
  graveId: number,
  itemId: number,
  count: number,
): Promise<void> {
  await ensureOk(
    await post("/odata/GraveItems", {
      GraveId: graveId,
      ItemId: itemId,
      Count: count,
    }),
  );
}

export async function createGraveRecipientLink(
  graveId: number,
  recipientId: number,
): Promise<void> {
  await ensureOk(
    await post("/odata/GraveRecipients", {
      GraveId: graveId,
      RecipientId: recipientId,
    }),
  );
}

export async function fetchGraveDetails(graveId: number): Promise<any> {
  const response = await fetch(
    `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`,
  );
  await ensureOk(response);
  return response.json();
}

export async function patchGrave(graveId: number, fields: Record<string, unknown>): Promise<void> {
  const response = await fetch(`/odata/Graves(${graveId})`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(fields),
  });
  await ensureOk(response);
}

export async function deleteGraveItemLink(linkId: number): Promise<void> {
  await ensureOk(await fetch(`/odata/GraveItems(${linkId})`, { method: "DELETE" }));
}

export async function deleteGraveRecipientLink(linkId: number): Promise<void> {
  await ensureOk(await fetch(`/odata/GraveRecipients(${linkId})`, { method: "DELETE" }));
}
```

Also add `GraveNotSaved: "errorGraveNotSaved"` to the `CODE_TO_I18N` map in `api.ts` (the map was created in Task 14).

- [ ] **Step 4: Implement the Graves view and controller**

`Graves.view.xml`:

```xml
<mvc:View
  xmlns:mvc="sap.ui.core.mvc"
  xmlns="sap.m"
  xmlns:tnt="sap.tnt"
  xmlns:core="sap.ui.core"
  controllerName="burkard.invoicing.controller.Graves"
  displayBlock="true"
>
    <tnt:ToolPage>
        <tnt:sideContent>
            <tnt:SideNavigation>
                <tnt:item>
                    <core:Fragment
            fragmentName="burkard.invoicing.view.fragments.Navigation"
            type="XML"
          />
                </tnt:item>
            </tnt:SideNavigation>
        </tnt:sideContent>
        <tnt:mainContents>
            <Page title="{i18n>menuGraves}" titleLevel="H1">
                <subHeader>
                    <Toolbar>
                        <Label text="{i18n>halfYear}" labelFor="halfYearSelect" />
                        <Select id="halfYearSelect" width="10rem" change="onHalfYearChange">
                            <core:Item key="2026-1" text="H1 2026" />
                            <core:Item key="2026-2" text="H2 2026" />
                            <core:Item key="2025-1" text="H1 2025" />
                            <core:Item key="2025-2" text="H2 2025" />
                        </Select>
                        <ToolbarSpacer />
                        <Button text="{i18n>createGrave}" press="onCreateGrave" />
                        <Button
              text="{i18n>generateInvoices}"
              press="onGenerateInvoices"
              type="Emphasized"
            />
                    </Toolbar>
                </subHeader>
                <content>
                    <Table id="gravesTable" items="{odata>/Graves}">
                        <columns>
                            <Column><Text text="{i18n>graveNumber}" /></Column>
                            <Column><Text text="{i18n>startDate}" /></Column>
                            <Column><Text text="{i18n>endDate}" /></Column>
                            <Column><Text text="{i18n>invoicingStatus}" /></Column>
                            <Column />
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{odata>GraveNumber}" />
                                    <Text text="{odata>StartDate}" />
                                    <Text text="{odata>EndDate}" />
                                    <Text
                    text="{parts: [{path: 'odata>Id'}, {path: 'status>/statuses'}], formatter: '.statusFormatter'}"
                  />
                                    <Button
                    text="{i18n>edit}"
                    press="onEditGrave"
                    type="Transparent"
                  />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                </content>
            </Page>
        </tnt:mainContents>
    </tnt:ToolPage>
</mvc:View>
```

`Graves.controller.ts`:

```ts
import BaseController from "./Base.controller";
import models from "../model/models";
import * as api from "../model/api";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import JSONModel from "sap/ui/model/json/JSONModel";
import Select from "sap/m/Select";
import Context from "sap/ui/model/odata/v4/Context";
import Fragment from "sap/ui/core/Fragment";
import Input from "sap/m/Input";
import BusyDialog from "sap/m/BusyDialog";
import MessageBox from "sap/m/MessageBox";

export default class Graves extends BaseController {
  public onInit(): void {
    models.bindRoot(this.getView()!);
    this.getView()!.setModel(new JSONModel({ statuses: {} }), "status");
    void this.refreshStatus();
  }

  public statusFormatter(graveId: number, statuses: Record<string, any>): string {
    const bundle = (this.getView()!.getModel("i18n") as any).getResourceBundle();
    return statuses?.[String(graveId)]?.Invoiced
      ? bundle.getText("statusInvoiced")
      : bundle.getText("statusOpen");
  }

  public onHalfYearChange(): void {
    void this.refreshStatus();
  }

  private async refreshStatus(): Promise<void> {
    const [year, half] = (this.byId("halfYearSelect") as Select)
      .getSelectedKey()
      .split("-")
      .map(Number);
    try {
      const rows = await api.fetchGraveStatus(year, half);
      const statuses: Record<string, any> = {};
      for (const row of rows) {
        statuses[String(row.Id)] = row;
      }

      (this.getView()!.getModel("status") as JSONModel).setProperty("/statuses", statuses);
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onCreateGrave(): Promise<void> {
    await this.openGraveDialog(null);
  }

  public async onEditGrave(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openGraveDialog(context);
  }

  private async openGraveDialog(graveContext: Context | null): Promise<void> {
    const dialog = (await Fragment.load({
      name: "burkard.invoicing.view.fragments.GraveDialog",
      controller: this,
    })) as any;
    (this.getView() as any).addDependent(dialog);
    this._graveDialog = dialog;

    if (graveContext) {
      const details = await api.fetchGraveDetails(graveContext.getProperty("Id") as number);
      dialog.setModel(
        new JSONModel({
          Id: details.Id,
          GraveNumber: details.GraveNumber,
          StartDate: details.StartDate,
          EndDate: details.EndDate,
          Items: details.Items.map((link: any) => ({
            Id: link.Id,
            Number: link.Item.Number,
            Count: link.Count,
          })),
          Recipients: details.Recipients.map((link: any) => ({
            Id: link.Id,
            Name: link.Recipient.Name,
          })),
        }),
        "graveDraft",
      );
    } else {
      dialog.setModel(new JSONModel({ Items: [], Recipients: [] }), "graveDraft");
    }

    dialog.open();
  }

  public async onGraveDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    const draft = (dialog.getModel("graveDraft") as JSONModel).getData() as any;
    try {
      if (draft.Id) {
        await api.patchGrave(Number(draft.Id), {
          GraveNumber: (this.byId("graveNumber") as Input).getValue(),
          StartDate: (this.byId("graveStart") as any).getValue(),
          EndDate: (this.byId("graveEnd") as any).getValue(),
        });
      } else {
        await api.createGrave({
          GraveNumber: (this.byId("graveNumber") as Input).getValue(),
          StartDate: (this.byId("graveStart") as any).getValue(),
          EndDate: (this.byId("graveEnd") as any).getValue(),
        });
      }

      dialog.close();
      ((this.byId("gravesTable") as any).getBinding("items") as ODataListBinding).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onGraveDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  public async onAddItemLink(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const graveId = draft.getProperty("/Id");
    if (!graveId) {
      await api.showError(this, new Error("GraveNotSaved"));
      return;
    }

    try {
      await api.createGraveItemLink(
        Number(graveId),
        Number((this.byId("linkItem") as Select).getSelectedKey()),
        Number((this.byId("linkCount") as Input).getValue()),
      );
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onAddRecipientLink(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const graveId = draft.getProperty("/Id");
    if (!graveId) {
      await api.showError(this, new Error("GraveNotSaved"));
      return;
    }

    try {
      await api.createGraveRecipientLink(
        Number(graveId),
        Number((this.byId("linkRecipient") as Select).getSelectedKey()),
      );
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onDeleteItemLink(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    try {
      await api.deleteGraveItemLink(context.getProperty("Id") as number);
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onDeleteRecipientLink(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    try {
      await api.deleteGraveRecipientLink(context.getProperty("Id") as number);
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  private async reloadGraveLinks(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const details = await api.fetchGraveDetails(Number(draft.getProperty("/Id")));
    draft.setProperty(
      "/Items",
      details.Items.map((link: any) => ({
        Id: link.Id,
        Number: link.Item.Number,
        Count: link.Count,
      })),
    );
    draft.setProperty(
      "/Recipients",
      details.Recipients.map((link: any) => ({
        Id: link.Id,
        Name: link.Recipient.Name,
      })),
    );
  }

  public async onGenerateInvoices(): Promise<void> {
    if (this._running) {
      return;
    }

    const dialog = (await Fragment.load({
      name: "burkard.invoicing.view.fragments.GenerateDialog",
      controller: this,
    })) as any;
    (this.getView() as any).addDependent(dialog);
    dialog.open();
  }

  public async onGenerateDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    const year = Number((this.byId("genYear") as Select).getSelectedKey());
    const half = Number((this.byId("genHalf") as Select).getSelectedKey());

    this._running = true;
    dialog.close();
    const busy = new BusyDialog({ title: this.i18nText("generationRunning") });
    busy.open();

    try {
      const job = await api.generateGraveInvoices(year, half);
      const finished = await this.pollJob(job.Id);
      if (finished.Status === "Failed") {
        await MessageBox.error(this.i18nText("generationFailed"));
      } else {
        const result = JSON.parse(finished.ResultJson ?? "{}") as any;
        await MessageBox.information(
          `${this.i18nText("generationDone")}: ${result.InvoicedGraves ?? 0}` +
            (result.SkippedGraves?.length
              ? ` (${this.i18nText("skippedGraves")}: ${result.SkippedGraves.join(", ")})`
              : ""),
        );
      }

      await this.refreshStatus();
    } catch (error) {
      await api.showError(this, error);
    } finally {
      busy.close();
      this._running = false;
    }
  }

  public onGenerateDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  private async pollJob(jobId: number): Promise<api.JobDto> {
    while (true) {
      const response = await fetch(`/odata/Jobs(${jobId})`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const job = (await response.json()) as api.JobDto;
      if (job.Status === "Succeeded" || job.Status === "Failed") {
        return job;
      }

      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
  }

  private i18nText(key: string): string {
    return (this.getView()!.getModel("i18n") as any).getResourceBundle().getText(key);
  }

  private _running = false;
  private _graveDialog: any;
}
```

`GraveDialog.fragment.xml` (creation mode plus edit mode with item/recipient links):

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <Dialog id="graveDialog" title="{i18n>createGrave}">
        <content>
            <VBox class="sapUiContentPadding">
                <Label text="{i18n>graveNumber}" labelFor="graveNumber" />
                <Input id="graveNumber" width="16rem" value="{graveDraft>/GraveNumber}" />
                <Label text="{i18n>startDate}" labelFor="graveStart" />
                <DatePicker
          id="graveStart"
          width="16rem"
          displayFormat="yyyy-MM-dd"
          valueFormat="yyyy-MM-dd"
          value="{graveDraft>/StartDate}"
        />
                <Label text="{i18n>endDate}" labelFor="graveEnd" />
                <DatePicker
          id="graveEnd"
          width="16rem"
          displayFormat="yyyy-MM-dd"
          valueFormat="yyyy-MM-dd"
          value="{graveDraft>/EndDate}"
        />
                <Panel headerText="{i18n>graveItems}">
                    <Table items="{graveDraft>/Items}">
                        <columns>
                            <Column><Text text="{i18n>itemNumber}" /></Column>
                            <Column><Text text="{i18n>itemCount}" /></Column>
                            <Column />
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{graveDraft>Number}" />
                                    <Text text="{graveDraft>Count}" />
                                    <Button
                    text="{i18n>delete}"
                    press="onDeleteItemLink"
                    type="Transparent"
                  />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                    <HBox class="sapUiSmallMarginTop">
                        <Select id="linkItem" items="{odata>/InvoiceItems}">
                            <core:Item key="{odata>Id}" text="{odata>Number}" />
                        </Select>
                        <Input
              id="linkCount"
              type="Number"
              width="6rem"
              value="1"
              ariaLabel="{i18n>itemCount}"
            />
                        <Button text="{i18n>add}" press="onAddItemLink" />
                    </HBox>
                </Panel>
                <Panel headerText="{i18n>graveRecipients}">
                    <Table items="{graveDraft>/Recipients}">
                        <columns>
                            <Column><Text text="{i18n>recipientName}" /></Column>
                            <Column />
                        </columns>
                        <items>
                            <ColumnListItem>
                                <cells>
                                    <Text text="{graveDraft>Name}" />
                                    <Button
                    text="{i18n>delete}"
                    press="onDeleteRecipientLink"
                    type="Transparent"
                  />
                                </cells>
                            </ColumnListItem>
                        </items>
                    </Table>
                    <HBox class="sapUiSmallMarginTop">
                        <Select id="linkRecipient" items="{odata>/Recipients}">
                            <core:Item key="{odata>Id}" text="{odata>Name}" />
                        </Select>
                        <Button text="{i18n>add}" press="onAddRecipientLink" />
                    </HBox>
                </Panel>
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>save}" type="Emphasized" press="onGraveDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press="onGraveDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

`GenerateDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns:core="sap.ui.core" xmlns="sap.m">
    <Dialog title="{i18n>generateInvoices}">
        <content>
            <VBox class="sapUiContentPadding">
                <Label text="{i18n>halfYear}" labelFor="genYear" />
                <Select id="genYear" width="10rem">
                    <core:Item key="2026" text="2026" />
                    <core:Item key="2025" text="2025" />
                    <core:Item key="2024" text="2024" />
                </Select>
                <Select id="genHalf" width="10rem">
                    <core:Item key="1" text="H1" />
                    <core:Item key="2" text="H2" />
                </Select>
            </VBox>
        </content>
        <beginButton>
            <Button text="{i18n>generateNow}" type="Emphasized" press="onGenerateDialogSave" />
        </beginButton>
        <endButton>
            <Button text="{i18n>cancel}" press="onGenerateDialogCancel" />
        </endButton>
    </Dialog>
</core:FragmentDefinition>
```

Add to `i18n.properties`:

```
halfYear=Halbjahr
createGrave=Grab anlegen
generateInvoices=Rechnungen erzeugen
generateNow=Jetzt erzeugen
graveNumber=Grabnummer
startDate=Beginn
endDate=Ende
invoicingStatus=Status
statusInvoiced=erstellt
statusOpen=offen
generationRunning=Rechnungen werden erstellt …
generationDone=Erstellt
skippedGraves=Übersprungen
generationFailed=Die Erstellung ist fehlgeschlagen.
graveItems=Leistungen
graveRecipients=Empfänger
add=Hinzufügen
edit=Bearbeiten
errorGraveNotSaved=Bitte speichern Sie zuerst das Grab.
```

- [ ] **Step 5: Run E2E tests to verify they pass**

Run: `cd frontend && npm run typecheck && cd ../e2e && npx playwright test graves`
Expected: PASS (2 tests). If the BusyDialog assertion flakes because the job is too fast, raise the seed count to 120 graves.

- [ ] **Step 6: Commit**

```bash
git add frontend e2e
git commit -m "feat: grave management, half-year status and blocking generation"
```

---

### Task 17: Combined release image, CI, and release workflow

**Files:**

- Create: `Dockerfile` (repo root), `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**

- Consumes: `frontend` UI5 build (`dist/`), `backend` solution.
- Produces: combined image `ghcr.io/<owner>/<repo>:<version>` and `:latest`; CI on PRs; manual release with semver bump (patch/minor/major).

- [ ] **Step 1: Write the CI and release workflow files**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - name: Build
        run: dotnet build backend/Invoicing.sln -c Release
      - name: Test
        run: dotnet test backend/Invoicing.sln -c Release --no-build

  frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: frontend
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: npm install
      - run: npm run typecheck
      - run: npm run build

  image:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build combined image
        run: docker build -t invoicing:ci .
```

`Dockerfile` (repo root — the combined release image):

```dockerfile
# syntax=docker/dockerfile:1

FROM node:22 AS frontend
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.0.0
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Invoicing.Api/Invoicing.Api.csproj -c Release -o /out -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /out ./
COPY --from=frontend /app/frontend/dist ./wwwroot
ENV ConnectionStrings__Invoicing="Data Source=/data/invoicing.db"
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Invoicing.Api.dll"]
```

`.github/workflows/release.yml`:

```yaml
name: Release

on:
  workflow_dispatch:
    inputs:
      bump:
        description: "Version bump"
        required: true
        type: choice
        default: patch
        options:
          - patch
          - minor
          - major

jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      packages: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Compute next version
        run: |
          LATEST=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
          CUR=${LATEST#v}
          IFS=. read -r MAJ MIN PAT <<< "$CUR"
          case "${{ github.event.inputs.bump }}" in
            major) MAJ=$((MAJ+1)); MIN=0; PAT=0 ;;
            minor) MIN=$((MIN+1)); PAT=0 ;;
            patch) PAT=$((PAT+1)) ;;
          esac
          echo "NEW_VERSION=v${MAJ}.${MIN}.${PAT}" >> "$GITHUB_ENV"

      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        run: |
          IMAGE="ghcr.io/${{ github.repository }}:${{ env.NEW_VERSION }}"
          docker build --build-arg VERSION="${{ env.NEW_VERSION }}" -t "$IMAGE" .
          docker tag "$IMAGE" "ghcr.io/${{ github.repository }}:latest"
          docker push "$IMAGE"
          docker push "ghcr.io/${{ github.repository }}:latest"

      - name: Create tag and GitHub release
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git tag "${{ env.NEW_VERSION }}"
          git push origin "${{ env.NEW_VERSION }}"
          gh release create "${{ env.NEW_VERSION }}" --title "${{ env.NEW_VERSION }}" --generate-notes
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

Note: this workflow sets git identity only inside the ephemeral CI runner (not the user's machine) and is required to push the release tag.

- [ ] **Step 2: Verify the image builds locally**

Run: `docker build -t invoicing:local .` then `docker run --rm -p 8080:8080 -v invoicing-test-data:/data invoicing:local` and `curl -s http://localhost:8080/healthz`
Expected: `{"status":"ok"}`; `curl -s http://localhost:8080/` returns the UI5 index page. Then stop the container.

- [ ] **Step 3: Commit**

```bash
git add Dockerfile .github
git commit -m "ci: combined release image, CI and release workflow"
```

---

## Final Verification Checklist (run before declaring Phase 1 done)

- [ ] `dotnet test backend/Invoicing.sln -c Release` — all backend + document tests green.
- [ ] `cd frontend && npm run typecheck && npm run build` — clean build.
- [ ] `docker compose up -d --build && cd e2e && npx playwright test` — all E2E green (smoke, masterdata, invoices, graves).
- [ ] Exactly one EF Core migration exists: `backend/src/Invoicing.Api/Migrations/` contains only `InitialCreate`.
- [ ] No hard-coded German strings in the frontend outside `i18n.properties`; backend document strings only in `DocumentTexts`.
- [ ] `git log` shows the task commits in order.
