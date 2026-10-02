# Grave Site Fields, Month Dates and Invoice Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Graves get a field number, a site name and a position, their term becomes month-precise with an optional end, grave numbers may not be reused in overlapping terms, and every invoice PDF follows the layout of the owner's scanned invoices.

**Architecture:** The domain gains a `GravePosition` enum, three grave columns, a nullable end month and five invoice snapshot columns, all in one consolidated migration that replaces the unreleased `RemoveDocumentLanguage`. `SaveGrave` parses months and checks the new rules in `GraveService`; the invoice writers snapshot the new values; `DocumentDataBuilder` hands them to a rewritten `InvoiceDocument` (head block, site lines, six-column table, VAT block at the foot of the last page). The Vue editor, lists, import and E2E specs follow.

**Tech Stack:** .NET 10, ASP.NET Core OData 8, EF Core 10 + SQLite, QuestPDF, xUnit + PdfPig; Vue 3 + PrimeVue 4 + vue-i18n, odata2ts; Playwright.

**Spec:** `docs/superpowers/specs/2026-10-02-grave-site-fields-and-invoice-layout-design.md`

## Global Constraints

- Code, identifiers and code comments in English; every user-visible string German (UI through `frontend/src/i18n/de.json`, documents through `DocumentTexts`). There is no English catalogue any more.
- Exactly one new migration for this version, and it replaces `20261002064856_RemoveDocumentLanguage` (unreleased; latest tag `v2.1.0`). Never two migrations.
- A month is stored as `DateOnly` on day 1. The API and the import take `yyyy-MM`; `yyyy-MM-dd` is accepted and truncated to its month.
- `Grave.EndDate` is optional. No end means the term has no upper bound.
- `FieldNumber`, `SiteName`, `Position` are required by `SaveGrave` and by the import. Only `GraveNumber` is unique, and only among graves whose terms overlap; comparison is trimmed and case-insensitive. The field number may repeat.
- Grave start/end do not affect half-year generation.
- `GravePosition` members, in this order: `DoubleInside`, `DoubleOutside`, `SingleInside`, `SingleOutside`, `UrnInside`, `UrnOutside`. Short forms on the invoice: `dGr.i`, `dGr.a`, `eGr.i`, `eGr.a`, `uGr.i`, `uGr.a`. Stored by name.
- The invoice's "KUNDEN-NR" is the recipient's number on every invoice; "LAUFWEG-NR" is the grave number, on grave invoices only.
- The VAT column ("ST") prints the rate itself ("19 %"), plus " Incl." on a VAT-included line, where unit price and line total are printed gross.
- Share invoices: the VAT block shows the recipient's share; one line above it reads `Nettosumme gesamt <100 % net> € – davon Ihr Anteil <percent> %`.
- `Letterhead.cs` is not changed. After any change to `InvoiceDocument`, page two of a multi-page invoice must still carry the full letterhead (CLAUDE.md).
- Backend commands run in the SDK container: `scripts/dotnet.sh test Invoicing.sln`. If the dev stack is up, first `docker compose stop backend`, then `find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`; afterwards `docker compose start backend` (CLAUDE.md: shared `backend/obj`).
- Never point anything at the dev stack (port 8080) or `data/invoicing.db`. E2E: `cd e2e && npm test`. Metadata: start the E2E stack with `docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build`, then `cd frontend && npm run gen:odata`.
- Frontend checks: `cd frontend && npm ci` (once), then `npm run typecheck && npm run lint && npm run build`, all clean.
- Conventional Commits; the commit hooks need `npm install` at the repo root once. The final commit of this work that changes the import format is `feat!` with a `BREAKING CHANGE:` footer. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Re-saving an existing grave without changing its number must not collide with itself — the overlap query excludes the grave's own id (test in Task 2).
2. A grave migrated with empty site fields still gets invoiced, and its PDF has no dangling "GRABSTÄTTE:" or "FELD-GRAB-NR:" line (test in Task 5).
3. A month picked in the browser stays that month after save and reopen; nothing goes through `toISOString` (E2E in Task 8, helper in Task 7).
4. When the VAT block does not fit under the last line items, it moves whole to a new page and that page still has the letterhead (test in Task 5).
5. An old grave invoice — `RecipientCount` null, no snapshot columns — still renders, with its share note and without "LAUFWEG-NR" (test in Task 5).

---

### Task 1: Schema — grave fields, invoice snapshot, one consolidated migration

**Files:**

- Create: `backend/src/Invoicing.Domain/Entities/GravePosition.cs`
- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs`
- Modify: `backend/src/Invoicing.Domain/Entities/Invoice.cs`
- Modify: `backend/src/Invoicing.Api/Persistence/AppDbContext.cs` (inside `OnModelCreating`)
- Delete: `backend/src/Invoicing.Api/Migrations/20261002064856_RemoveDocumentLanguage.cs`, `…RemoveDocumentLanguage.Designer.cs`
- Create (generated): `backend/src/Invoicing.Api/Migrations/<timestamp>_GraveSiteFieldsAndGermanOnly.cs` + `.Designer.cs`
- Modify (generated): `backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs`
- Create: `scripts/patch-dev-db.sh`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveMonthMigrationTests.cs`

**Interfaces:**

- Produces: `enum GravePosition { DoubleInside, DoubleOutside, SingleInside, SingleOutside, UrnInside, UrnOutside }` in `Burkard.Invoicing.Domain.Entities`.
- Produces on `Grave`: `string FieldNumber`, `string SiteName`, `GravePosition? Position`, `DateOnly StartDate`, `DateOnly? EndDate`.
- Produces on `Invoice`: `string? RecipientNumber`, `string? GraveNumber`, `string? GraveSiteName`, `string? GraveFieldNumber`, `GravePosition? GravePosition`.

- [ ] **Step 1: Write the failing migration test**

Create `backend/tests/Invoicing.IntegrationTests/GraveMonthMigrationTests.cs`:

```csharp
using Burkard.Invoicing.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A grave's term became a month. A database written before that has
/// days in it; upgrading turns each into the first of its month and leaves the
/// new site fields empty for the editor to fill.</summary>
public class GraveMonthMigrationTests
{
    // the last migration of v2.1.0, the release before this change
    private const string PreviousMigration = "20260901134020_PaymentVatAndGraveShare";

    [Fact]
    public async Task Upgrading_truncates_grave_terms_to_their_month()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbFile}")
                .Options;
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Graves (GraveNumber, StartDate, EndDate, CreatedAt) "
                        + "VALUES ('M-1', '2026-03-15', '2040-12-31', '2026-03-15')"
                );
                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var grave = await db.Graves.SingleAsync();
                Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
                Assert.Equal(new DateOnly(2040, 12, 1), grave.EndDate);
                Assert.Equal("", grave.FieldNumber);
                Assert.Equal("", grave.SiteName);
                Assert.Null(grave.Position);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbFile))
                File.Delete(dbFile);
        }
    }
}
```

If the `Graves` table at `PreviousMigration` has more NOT NULL columns than the four in the INSERT, add them with neutral values (check `20260822173058_InitialCreate.cs` and the later migrations).

- [ ] **Step 2: Run it to verify it fails**

Run: `scripts/dotnet.sh test Invoicing.sln --filter GraveMonthMigrationTests`
Expected: build FAIL — `'Grave' does not contain a definition for 'FieldNumber'`.

- [ ] **Step 3: Add the domain types**

Create `backend/src/Invoicing.Domain/Entities/GravePosition.cs`:

```csharp
namespace Burkard.Invoicing.Domain.Entities;

/// <summary>What kind of grave it is and whether it lies inside or outside.
/// Stored by name, so the members can be reordered.</summary>
public enum GravePosition
{
    DoubleInside,
    DoubleOutside,
    SingleInside,
    SingleOutside,
    UrnInside,
    UrnOutside,
}
```

In `Grave.cs`, replace the `StartDate`/`EndDate` properties and add the three fields after `GraveNumber`:

```csharp
    public string GraveNumber { get; set; } = "";
    public string FieldNumber { get; set; } = "";
    public string SiteName { get; set; } = "";

    /// <summary>Required when a grave is saved; null only on graves that were
    /// migrated from before the field existed and have not been edited since.</summary>
    public GravePosition? Position { get; set; }

    /// <summary>The term is counted in months: both ends are stored as the first
    /// of their month. No end means the grave runs indefinitely.</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
```

In `Invoice.cs`, after `RecipientAddress`:

```csharp
    /// <summary>What the document prints in its head and above the lines,
    /// snapshotted like the name and address so a later change to the recipient
    /// or the grave never alters an issued invoice. Null on invoices generated
    /// before these existed; the document leaves out whatever is missing. The
    /// grave values are set on grave invoices only.</summary>
    public string? RecipientNumber { get; set; }
    public string? GraveNumber { get; set; }
    public string? GraveSiteName { get; set; }
    public string? GraveFieldNumber { get; set; }
    public GravePosition? GravePosition { get; set; }
```

In `AppDbContext.OnModelCreating`, next to the other `Grave`/`Invoice` configuration:

```csharp
        // by name, so a reordered enum never reinterprets stored rows
        mb.Entity<Grave>().Property(g => g.Position).HasConversion<string>();
        mb.Entity<Invoice>().Property(i => i.GravePosition).HasConversion<string>();
```

- [ ] **Step 4: Build and fix the call sites the nullable end touches**

Run: `scripts/dotnet.sh build Invoicing.sln`
Expected: success or a handful of errors where a `DateOnly?` is used as `DateOnly`. Fix each by keeping the value nullable (do not invent an end). `GraveService.SaveAsync`'s `endDate < startDate` compiles with a lifted comparison and is rewritten in Task 2 anyway.

- [ ] **Step 5: Replace the unreleased migration with one consolidated migration**

```bash
git rm backend/src/Invoicing.Api/Migrations/20261002064856_RemoveDocumentLanguage.cs \
       backend/src/Invoicing.Api/Migrations/20261002064856_RemoveDocumentLanguage.Designer.cs
# the snapshot as it was before that migration, so the new diff includes its Language drops
git show 4d31f0a^:backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs \
  > backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs
scripts/ef.sh migrations add GraveSiteFieldsAndGermanOnly --project src/Invoicing.Api
```

Open the generated `*_GraveSiteFieldsAndGermanOnly.cs` and check that `Up` contains exactly: `DropColumn` `Language` on `Invoices` and on `GlobalSettings`; `AddColumn` `FieldNumber` and `SiteName` on `Graves` (`TEXT`, not nullable, `defaultValue: ""`) and `Position` (`TEXT`, nullable); `AlterColumn` `EndDate` on `Graves` to nullable; `AddColumn` `RecipientNumber`, `GraveNumber`, `GraveSiteName`, `GraveFieldNumber`, `GravePosition` on `Invoices` (`TEXT`, nullable). Anything else means the snapshot or the model is wrong — fix it, do not edit it away.

Append to the end of `Up`:

```csharp
            // A grave's term is a month now. DateOnly is stored as 'yyyy-MM-dd'
            // text, so the first eight characters are the month and '01' makes
            // it its first day. Not undone by Down: the day is gone.
            migrationBuilder.Sql(
                "UPDATE Graves SET StartDate = substr(StartDate, 1, 8) || '01', "
                    + "EndDate = CASE WHEN EndDate IS NULL THEN NULL "
                    + "ELSE substr(EndDate, 1, 8) || '01' END;"
            );
```

- [ ] **Step 6: Run the migration test**

Run: `scripts/dotnet.sh test Invoicing.sln --filter GraveMonthMigrationTests`
Expected: PASS.

- [ ] **Step 7: Add the one-off dev database patch (do not run it)**

A dev database that already applied `RemoveDocumentLanguage` has a history row for a migration that no longer exists and has no `Language` columns, so the consolidated migration would fail on it. Create `scripts/patch-dev-db.sh` and `chmod +x` it:

```bash
#!/usr/bin/env bash
# One-off: a dev database that ran main between the German-only change and the
# grave site fields applied 20261002064856_RemoveDocumentLanguage, which was
# then consolidated into *_GraveSiteFieldsAndGermanOnly. This puts such a
# database back where the consolidated migration starts: it re-adds the two
# Language columns that migration drops (nothing reads them any more) and
# deletes the stale history row. The backend's next start then applies the
# consolidated migration as a whole.
#
# Stop the backend first. Safe to re-run: every step is guarded. Takes a backup.
set -euo pipefail
DB="${1:-data/invoicing.db}"

cp "$DB" "$DB.backup-$(date +%Y%m%d%H%M%S)"

python3 - "$DB" <<'PY'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
stale = "20261002064856_RemoveDocumentLanguage"
if not db.execute(
    "SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = ?", (stale,)
).fetchone():
    print("nothing to do:", stale, "was never applied")
    sys.exit(0)

def columns(table):
    return {r[1] for r in db.execute(f"pragma table_info({table})")}

for table in ("Invoices", "GlobalSettings"):
    if "Language" not in columns(table):
        db.execute(f"ALTER TABLE {table} ADD COLUMN Language TEXT NOT NULL DEFAULT 'de'")
db.execute("DELETE FROM __EFMigrationsHistory WHERE MigrationId = ?", (stale,))
db.commit()
print("reverted", stale, "- the next start applies the consolidated migration")
PY
```

Verify it against a throwaway copy only, never `data/`:

```bash
tmp=$(mktemp -d)
python3 - "$tmp/t.db" <<'PY'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
db.execute("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT, ProductVersion TEXT)")
db.execute("CREATE TABLE Invoices (Id INTEGER)")
db.execute("CREATE TABLE GlobalSettings (Id INTEGER)")
db.execute("INSERT INTO __EFMigrationsHistory VALUES ('20261002064856_RemoveDocumentLanguage', '10.0.0')")
db.commit()
PY
scripts/patch-dev-db.sh "$tmp/t.db" && scripts/patch-dev-db.sh "$tmp/t.db"
rm -rf "$tmp"
```

Expected: first run prints `reverted …`, second prints `nothing to do …`.

- [ ] **Step 8: Run the whole backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS (nothing reads the new columns yet).

- [ ] **Step 9: Commit**

```bash
git add -A backend/src/Invoicing.Domain backend/src/Invoicing.Api/Persistence \
  backend/src/Invoicing.Api/Migrations backend/tests/Invoicing.IntegrationTests/GraveMonthMigrationTests.cs \
  scripts/patch-dev-db.sh
git commit -m "refactor(db): add grave site fields and invoice snapshots in one consolidated migration"
```

---

### Task 2: Saving a grave — months, site fields, grave number overlap

**Files:**

- Create: `backend/src/Invoicing.Api/Services/GraveTerm.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs` (`SaveAsync`, `ValidateAsync`, new exception)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs` (`Save`, `SaveGraveRequest`, `Get(key)`, `Search`)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoicingStatusController.cs`
- Modify (existing SaveGrave call sites): `backend/tests/Invoicing.IntegrationTests/{SaveGraveTests,InvoiceItemTests,GraveRecipientImmutabilityTests,GraveTests,GraveBillingValidationTests,ItemGravesTests,LinkIntegrityTests}.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/SaveGraveTests.cs`

**Interfaces:**

- Consumes: Task 1's `Grave` properties and `GravePosition`.
- Produces: `static class GraveTerm` with `bool TryParseMonth(string? raw, out DateOnly month)`, `bool TryParsePosition(string? raw, out GravePosition position)`, `bool Overlap(DateOnly startA, DateOnly? endA, DateOnly startB, DateOnly? endB)`, `string FormatMonth(DateOnly month)` ("03/2026"). Task 6 uses `TryParseMonth` and `TryParsePosition`.
- Produces: `sealed record GraveFields(string GraveNumber, string FieldNumber, string SiteName, GravePosition? Position, DateOnly StartDate, DateOnly? EndDate)`; `GraveService.SaveAsync(long? graveId, GraveFields fields, IReadOnlyList<GraveItemInput> items, IReadOnlyList<long> recipientIds, CancellationToken ct)`.
- Produces (HTTP): `POST /odata/SaveGrave` body adds `fieldNumber`, `siteName`, `position` (member name), `startDate`/`endDate` as `yyyy-MM` (end may be null/empty). New error codes `FieldNumberRequired`, `SiteNameRequired`, `PositionRequired`, `GraveNumberInUse`; the latter's body is `{error:{code,message,args}}` with `args` = `[number, "MM/yyyy" start]` for an open-ended clash or `[number, start, end]` otherwise. `Graves(id)` adds `FieldNumber`, `SiteName`, `Position` (string or null). `GraveInvoicingStatus` rows add `FieldNumber`, `SiteName`. `SearchGraves` also matches those two.

- [ ] **Step 1: Give the existing SaveGrave call sites the new required fields**

Every test request below already sends `graveNumber = …,`; add the three fields after it:

```bash
cd backend/tests/Invoicing.IntegrationTests
sed -i -E 's/^(\s*)graveNumber = (.*),$/&\n\1fieldNumber = "F-1",\n\1siteName = "Grabstätte Test",\n\1position = "SingleInside",/' \
  SaveGraveTests.cs InvoiceItemTests.cs GraveRecipientImmutabilityTests.cs GraveTests.cs \
  GraveBillingValidationTests.cs ItemGravesTests.cs LinkIntegrityTests.cs
cd -
git diff --stat
```

Then replace the theory `Save_rejects_invalid_grave_fields` in `SaveGraveTests.cs` (it uses the `graveNumber,` shorthand the sed skips) with:

```csharp
    [Theory]
    [InlineData("", "F-1", "Grabstätte", "SingleInside", "2026-01", "GraveNumberRequired")]
    [InlineData("S-005", "", "Grabstätte", "SingleInside", "2026-01", "FieldNumberRequired")]
    [InlineData("S-005", "F-1", " ", "SingleInside", "2026-01", "SiteNameRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", null, "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "Sideways", "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "3", "2026-01", "PositionRequired")]
    [InlineData("S-005", "F-1", "Grabstätte", "SingleInside", "not-a-date", "InvalidDate")]
    [InlineData("S-005", "F-1", "Grabstätte", "SingleInside", "", "InvalidDate")]
    public async Task Save_rejects_invalid_grave_fields(
        string graveNumber,
        string fieldNumber,
        string siteName,
        string? position,
        string startDate,
        string expectedCode
    )
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedAsync(client);

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber,
                    fieldNumber,
                    siteName,
                    position,
                    startDate,
                    endDate = (string?)null,
                    items = new List<object>(),
                    recipientIds = new List<long>(),
                }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        Assert.Equal(
            expectedCode,
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }
```

- [ ] **Step 2: Write the new failing tests**

Append to `SaveGraveTests.cs`:

```csharp
    private static Task<HttpResponseMessage> PostGraveAsync(
        HttpClient client,
        string graveNumber,
        string startDate,
        string? endDate,
        long itemId,
        long recipientId,
        long? graveId = null,
        string fieldNumber = "27",
        string siteName = "Schineller Bäcke",
        string position = "DoubleOutside"
    ) =>
        client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveId,
                    graveNumber,
                    fieldNumber,
                    siteName,
                    position,
                    startDate,
                    endDate,
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );

    private static async Task<(string? Code, string[] Args)> ErrorOfAsync(
        HttpResponseMessage response
    )
    {
        using var json = await ODataJson.ReadAsync(response);
        var error = json.RootElement.GetProperty("error");
        var args = error.TryGetProperty("args", out var list)
            ? list.EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
            : Array.Empty<string>();
        return (error.GetProperty("code").GetString(), args);
    }

    [Fact]
    public async Task Save_stores_the_site_fields_and_the_term_as_months()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var response = await PostGraveAsync(client, "M-1", "2026-03-15", null, item, recipient);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var created = await ODataJson.ReadAsync(response);
        var id = created.RootElement.GetProperty("Id").GetInt64();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == id);
            Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
            Assert.Null(grave.EndDate);
            Assert.Equal("27", grave.FieldNumber);
            Assert.Equal("Schineller Bäcke", grave.SiteName);
            Assert.Equal(GravePosition.DoubleOutside, grave.Position);
        }

        using var details = await ODataJson.ReadAsync(await client.GetAsync($"/odata/Graves({id})"));
        Assert.Equal("27", details.RootElement.GetProperty("FieldNumber").GetString());
        Assert.Equal("Schineller Bäcke", details.RootElement.GetProperty("SiteName").GetString());
        Assert.Equal("DoubleOutside", details.RootElement.GetProperty("Position").GetString());

        using var status = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/GraveInvoicingStatus?year=2026&half=1")
        );
        var row = status.RootElement.GetProperty("value")[0];
        Assert.Equal("Schineller Bäcke", row.GetProperty("SiteName").GetString());
        Assert.Equal("27", row.GetProperty("FieldNumber").GetString());

        using var search = await ODataJson.ReadAsync(
            await client.GetAsync("/odata/SearchGraves?term=bäcke")
        );
        Assert.Equal(1, search.RootElement.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Save_compares_the_end_month_with_the_start_month()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var before = await PostGraveAsync(client, "R-1", "2026-05", "2026-04", item, recipient);
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        Assert.Equal("DateRangeInvalid", (await ErrorOfAsync(before)).Code);

        // one month long: start and end are the same month
        var same = await PostGraveAsync(client, "R-2", "2026-05", "2026-05", item, recipient);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task A_grave_number_cannot_be_used_twice_in_overlapping_terms()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostGraveAsync(client, "A-1", "2020-01", "2030-12", item, recipient)).StatusCode
        );

        // the last month overlaps; trimmed and case-insensitive, like the import
        var clash = await PostGraveAsync(client, " a-1 ", "2030-12", null, item, recipient);
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        var (code, args) = await ErrorOfAsync(clash);
        Assert.Equal("GraveNumberInUse", code);
        Assert.Equal(new[] { "A-1", "01/2020", "12/2030" }, args);

        // the month after the end is free again
        var next = await PostGraveAsync(client, "A-1", "2031-01", null, item, recipient);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        // that grave has no end, so every later term overlaps it
        var later = await PostGraveAsync(client, "A-1", "2045-01", "2045-12", item, recipient);
        var (laterCode, laterArgs) = await ErrorOfAsync(later);
        Assert.Equal("GraveNumberInUse", laterCode);
        Assert.Equal(new[] { "A-1", "01/2031" }, laterArgs);
    }

    [Fact]
    public async Task Resaving_a_grave_does_not_collide_with_itself()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);
        using var created = await ODataJson.ReadAsync(
            await PostGraveAsync(client, "B-1", "2020-01", null, item, recipient)
        );
        var id = created.RootElement.GetProperty("Id").GetInt64();

        var resaved = await PostGraveAsync(client, "B-1", "2020-01", null, item, recipient, id);
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);
    }

    [Fact]
    public async Task Two_graves_may_share_a_field_number()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (item, _, recipient, _) = await SeedAsync(client);

        var first = await PostGraveAsync(client, "C-1", "2020-01", null, item, recipient, fieldNumber: "27");
        var second = await PostGraveAsync(client, "C-2", "2020-01", null, item, recipient, fieldNumber: "27");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
```

Add `using System;` and `using Burkard.Invoicing.Domain.Entities;` to the file's usings if missing.

- [ ] **Step 3: Run them to verify they fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter SaveGraveTests`
Expected: FAIL — e.g. `Save_rejects_invalid_grave_fields` gets `OK`/other codes for the new rows; `A_grave_number_cannot_be_used_twice…` gets `OK` for the clash.

- [ ] **Step 4: Add `GraveTerm`**

Create `backend/src/Invoicing.Api/Services/GraveTerm.cs`:

```csharp
using System.Globalization;
using Burkard.Invoicing.Domain.Entities;

namespace Burkard.Invoicing.Api.Services;

/// <summary>A grave's term is counted in months: start and end are stored as
/// the first of their month, and the end may be missing. Shared by SaveGrave
/// and the import so both read the same text the same way.</summary>
public static class GraveTerm
{
    /// <summary>"2026-03", or a full date whose day is dropped ("2026-03-15"),
    /// so a client that still sends days keeps working.</summary>
    public static bool TryParseMonth(string? raw, out DateOnly month)
    {
        month = default;
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (
            DateOnly.TryParseExact(
                text,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
            || DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed
            )
        )
        {
            month = new DateOnly(parsed.Year, parsed.Month, 1);
            return true;
        }

        return false;
    }

    /// <summary>A member name only: Enum.TryParse alone would also take "3".</summary>
    public static bool TryParsePosition(string? raw, out GravePosition position)
    {
        position = default;
        return raw is not null
            && Enum.GetNames<GravePosition>().Contains(raw)
            && Enum.TryParse(raw, out position);
    }

    /// <summary>Whether two terms share at least one month. Both ends are
    /// inclusive; no end means no upper bound.</summary>
    public static bool Overlap(DateOnly startA, DateOnly? endA, DateOnly startB, DateOnly? endB) =>
        startA <= (endB ?? DateOnly.MaxValue) && startB <= (endA ?? DateOnly.MaxValue);

    public static string FormatMonth(DateOnly month) =>
        month.ToString("MM/yyyy", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Validate and store the fields in `GraveService`**

In `GraveService.cs`, add above `GraveService`:

```csharp
public sealed record GraveFields(
    string GraveNumber,
    string FieldNumber,
    string SiteName,
    GravePosition? Position,
    DateOnly StartDate,
    DateOnly? EndDate
);
```

Change `SaveAsync`'s signature to `SaveAsync(long? graveId, GraveFields fields, IReadOnlyList<GraveItemInput> items, IReadOnlyList<long> recipientIds, CancellationToken ct)`. Replace the date check and the `ValidateAsync(graveNumber, …)` call at its top with:

```csharp
        await ValidateFieldsAsync(graveId, fields, ct);
        await ValidateAsync(items, recipientIds, ct);
```

Replace the three assignments after `LoadForReplaceAsync`/`NewGrave()` with:

```csharp
        grave.GraveNumber = fields.GraveNumber.Trim();
        grave.FieldNumber = fields.FieldNumber.Trim();
        grave.SiteName = fields.SiteName.Trim();
        grave.Position = fields.Position;
        grave.StartDate = fields.StartDate;
        grave.EndDate = fields.EndDate;
```

Remove the `graveNumber` parameter and the `GraveNumberRequired` check from `ValidateAsync`, and add:

```csharp
    private async Task ValidateFieldsAsync(long? graveId, GraveFields fields, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fields.GraveNumber))
        {
            throw new ValidationException("GraveNumberRequired", "The grave number is required.");
        }

        if (string.IsNullOrWhiteSpace(fields.FieldNumber))
        {
            throw new ValidationException(
                "FieldNumberRequired",
                "The grave field number is required."
            );
        }

        if (string.IsNullOrWhiteSpace(fields.SiteName))
        {
            throw new ValidationException("SiteNameRequired", "The grave site name is required.");
        }

        if (fields.Position is null)
        {
            throw new ValidationException("PositionRequired", "The grave position is required.");
        }

        // the import refuses the same (DateRangeInvalid)
        if (fields.EndDate < fields.StartDate)
        {
            throw new ValidationException(
                "DateRangeInvalid",
                "The end month must not be before the start month."
            );
        }

        // A number may come back once its grave has ended, but two graves that
        // run at the same time cannot share it. Compared in memory, like
        // SearchGraves: SQLite's lower() folds ASCII only.
        var number = fields.GraveNumber.Trim();
        var others = await _db
            .Graves.Where(g => graveId == null || g.Id != graveId)
            .Select(g => new
            {
                g.GraveNumber,
                g.StartDate,
                g.EndDate,
            })
            .ToListAsync(ct);
        var clash = others.FirstOrDefault(g =>
            string.Equals(g.GraveNumber.Trim(), number, StringComparison.OrdinalIgnoreCase)
            && GraveTerm.Overlap(g.StartDate, g.EndDate, fields.StartDate, fields.EndDate)
        );
        if (clash is not null)
        {
            throw new GraveNumberInUseException(clash.GraveNumber, clash.StartDate, clash.EndDate);
        }
    }
```

Add next to `GraveShareValidationException`:

```csharp
/// <summary>The grave number belongs to another grave whose term overlaps.
/// Carries that grave's number and term so the message can name them.</summary>
public sealed class GraveNumberInUseException : ValidationException
{
    public GraveNumberInUseException(string number, DateOnly start, DateOnly? end)
        : base(
            "GraveNumberInUse",
            end is { } until
                ? $"The grave number '{number}' is in use from {GraveTerm.FormatMonth(start)} to {GraveTerm.FormatMonth(until)}."
                : $"The grave number '{number}' is in use from {GraveTerm.FormatMonth(start)}."
        )
    {
        // two arguments for an open-ended grave, three otherwise: the UI picks
        // its text by the count
        Args = end is { } last
            ? [number, GraveTerm.FormatMonth(start), GraveTerm.FormatMonth(last)]
            : [number, GraveTerm.FormatMonth(start)];
    }

    public IReadOnlyList<string> Args { get; }
}
```

Run `grep -rn "SaveAsync(" backend/src` and update any other caller to pass a `GraveFields`.

- [ ] **Step 6: Parse the request in `GravesController.Save`**

Extend `SaveGraveRequest`:

```csharp
    public sealed class SaveGraveRequest
    {
        public long? GraveId { get; set; }
        public string? GraveNumber { get; set; }
        public string? FieldNumber { get; set; }
        public string? SiteName { get; set; }
        public string? Position { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public List<GraveItemInput>? Items { get; set; }
        public List<long>? RecipientIds { get; set; }
    }
```

Replace the `DateOnly.TryParse` block with:

```csharp
        DateOnly? endDate = null;
        if (!string.IsNullOrWhiteSpace(request.EndDate))
        {
            if (!GraveTerm.TryParseMonth(request.EndDate, out var end))
            {
                return InvalidDate();
            }

            endDate = end;
        }

        if (!GraveTerm.TryParseMonth(request.StartDate, out var startDate))
        {
            return InvalidDate();
        }

        var fields = new GraveFields(
            request.GraveNumber ?? "",
            request.FieldNumber ?? "",
            request.SiteName ?? "",
            // an unknown name is reported as missing: the editor only offers the six
            GraveTerm.TryParsePosition(request.Position, out var position) ? position : null,
            startDate,
            endDate
        );
```

with the helper in the controller:

```csharp
    private BadRequestObjectResult InvalidDate() =>
        BadRequest(
            new
            {
                error = new
                {
                    code = "InvalidDate",
                    message = "The start month is required; start and end are months (yyyy-MM).",
                },
            }
        );
```

Call `_graves.SaveAsync(request.GraveId, fields, request.Items ?? new List<GraveItemInput>(), request.RecipientIds ?? new List<long>(), HttpContext.RequestAborted)`. Add a catch before `catch (ValidationException ex)`:

```csharp
        catch (GraveNumberInUseException ex)
        {
            return BadRequest(
                new
                {
                    error = new
                    {
                        code = ex.Code,
                        message = ex.Message,
                        args = ex.Args,
                    },
                }
            );
        }
```

- [ ] **Step 7: Expose the fields on the read side**

In `GravesController.Get(long key)`'s anonymous `result`, after `grave.GraveNumber,`:

```csharp
            grave.FieldNumber,
            grave.SiteName,
            // by name; System.Text.Json would write the enum as a number
            Position = grave.Position?.ToString(),
```

In `Search`, project `g.FieldNumber, g.SiteName` alongside `g.GraveNumber` and extend the match:

```csharp
                g.GraveNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.SiteName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.FieldNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || g.RecipientNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase))
```

In `GraveInvoicingStatusController.Status`, add `g.FieldNumber, g.SiteName,` after `g.GraveNumber,` in both anonymous projections.

- [ ] **Step 8: Run the backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS. A failure in a test that creates two graves with the same number through SaveGrave means that test now hits `GraveNumberInUse`; give its second grave a different number unless the test is about that number.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Invoicing.Api backend/tests/Invoicing.IntegrationTests
git commit -m "feat(graves): save site fields and month terms, keep grave numbers unique in time"
```

---

### Task 3: Snapshot recipient number and grave site on every invoice

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs` (the `new Invoice` in `GenerateForGraveAsync`)
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs` (the `new Invoice` for each revision)
- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs` (both `new Invoice` initialisers: create and revision)
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceSnapshotTests.cs`

**Interfaces:**

- Consumes: Task 1's `Invoice` snapshot properties; Task 2's SaveGrave body.
- Produces: every invoice written from now on carries `RecipientNumber`; grave invoices and their revisions also `GraveNumber`, `GraveSiteName`, `GraveFieldNumber`, `GravePosition` as the grave had them at generation.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Invoicing.IntegrationTests/InvoiceSnapshotTests.cs`:

```csharp
using System.Net;
using Burkard.Invoicing.Api.Persistence;
using Burkard.Invoicing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>What the document prints in its head and above the lines is
/// snapshotted like the recipient's name and address (FR-017): a later change
/// to the recipient or the grave never reaches an issued invoice.</summary>
public class InvoiceSnapshotTests
{
    private static async Task<(long GraveId, long ItemId)> SeedGraveAsync(
        ApiTestHost host,
        HttpClient client
    )
    {
        long itemId,
            recipientId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.VatRates.Add(new VatRate { Rate = 19m });
            var item = new InvoiceItem
            {
                Number = "12001",
                Description = "Herbstbepflanzung",
                VatRate = 19m,
                DefaultPrice = 24m,
            };
            var recipient = new Recipient
            {
                Number = "067200",
                Name = "Werner Schineller",
                Address = "Bartholomäus-Weltz-Straße 4\n67346 Speyer",
            };
            db.InvoiceItems.Add(item);
            db.Recipients.Add(recipient);
            await db.SaveChangesAsync();
            (itemId, recipientId) = (item.Id, recipient.Id);
        }

        var response = await client.PostAsync(
            "/odata/SaveGrave",
            ODataJson.Body(
                new
                {
                    graveNumber = "1234",
                    fieldNumber = "27",
                    siteName = "Schineller Bäcke",
                    position = "DoubleOutside",
                    startDate = "2020-01",
                    endDate = (string?)null,
                    items = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)null,
                        },
                    },
                    recipientIds = new[] { recipientId },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ODataJson.ReadAsync(response);
        return (json.RootElement.GetProperty("Id").GetInt64(), itemId);
    }

    [Fact]
    public async Task A_grave_invoice_keeps_the_site_it_was_generated_with()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        var (graveId, itemId) = await SeedGraveAsync(host, client);
        await GraveGenerationHelper.RunAsync(client, 2026, 1);

        // the grave is renamed after the invoice went out
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grave = await db.Graves.SingleAsync(g => g.Id == graveId);
            grave.SiteName = "Neuer Name";
            await db.SaveChangesAsync();
        }

        long invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.SingleAsync(i => i.GraveId == graveId);
            Assert.Equal("067200", invoice.RecipientNumber);
            Assert.Equal("1234", invoice.GraveNumber);
            Assert.Equal("Schineller Bäcke", invoice.GraveSiteName);
            Assert.Equal("27", invoice.GraveFieldNumber);
            Assert.Equal(GravePosition.DoubleOutside, invoice.GravePosition);
            invoiceId = invoice.Id;
        }

        // a revision is newly generated, so it takes the grave as it is now
        var revision = await client.PostAsync(
            "/odata/CreateGraveInvoiceRevision",
            ODataJson.Body(
                new
                {
                    invoiceId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)24m,
                        },
                    },
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latest = await db.Invoices.SingleAsync(i =>
                i.GraveId == graveId && i.IsLatestRevision
            );
            Assert.Equal("Neuer Name", latest.GraveSiteName);
            Assert.Equal("1234", latest.GraveNumber);
            Assert.Equal("067200", latest.RecipientNumber);
        }
    }

    [Fact]
    public async Task An_ordinary_invoice_carries_the_recipient_number_and_no_grave()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await SeedGraveAsync(host, client);
        long recipientId,
            itemId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            recipientId = (await db.Recipients.SingleAsync()).Id;
            itemId = (await db.InvoiceItems.SingleAsync()).Id;
        }

        var response = await client.PostAsync(
            "/odata/CreateInvoice",
            ODataJson.Body(
                new
                {
                    recipientId,
                    lines = new[]
                    {
                        new
                        {
                            itemId,
                            count = 1,
                            unitPrice = (decimal?)24m,
                        },
                    },
                }
            )
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope2 = host.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db2.Invoices.SingleAsync(i => i.GraveId == null);
        Assert.Equal("067200", invoice.RecipientNumber);
        Assert.Null(invoice.GraveNumber);
        Assert.Null(invoice.GraveSiteName);
    }
}
```

Check the `CreateInvoice` body against `CreateInvoiceTests.cs` and adjust the property names there if they differ.

- [ ] **Step 2: Run them to verify they fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter InvoiceSnapshotTests`
Expected: FAIL — `Assert.Equal() Failure: Expected "067200", Actual null`.

- [ ] **Step 3: Write the snapshots**

`GraveInvoiceGenerator.cs`, in the `new Invoice { … }` after `RecipientAddress = recipients[r].Address,`:

```csharp
                RecipientNumber = recipients[r].Number,
                GraveNumber = grave.GraveNumber,
                GraveSiteName = grave.SiteName,
                GraveFieldNumber = grave.FieldNumber,
                GravePosition = grave.Position,
```

`GraveInvoiceRevisionService.cs`: before the loop that builds the revisions, load the grave once:

```csharp
        // like the address, a revision takes the grave as it is now
        var grave = await _db.Graves.AsNoTracking().SingleAsync(g => g.Id == edited.GraveId, ct);
```

and in its `new Invoice { … }` after `RecipientAddress = recipient.Address,`:

```csharp
                RecipientNumber = recipient.Number,
                GraveNumber = grave.GraveNumber,
                GraveSiteName = grave.SiteName,
                GraveFieldNumber = grave.FieldNumber,
                GravePosition = grave.Position,
```

`InvoiceService.cs`, create (`RevisionNumber = 0`): after `RecipientAddress = recipient.Address,` add `RecipientNumber = recipient.Number,`. Revision (`RevisionNumber = revisionNumber`): after `RecipientAddress = recipient.Address,` add

```csharp
            RecipientNumber = recipient.Number,
            // not a grave revision (those go through GraveInvoiceRevisionService),
            // so whatever grave values the edited invoice had stay as they were
            GraveNumber = edited.GraveNumber,
            GraveSiteName = edited.GraveSiteName,
            GraveFieldNumber = edited.GraveFieldNumber,
            GravePosition = edited.GravePosition,
```

- [ ] **Step 4: Run the backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Api/Services backend/tests/Invoicing.IntegrationTests/InvoiceSnapshotTests.cs
git commit -m "feat(invoices): snapshot the recipient number and the grave site"
```

---

### Task 4: Hand the snapshot and the VAT-included flag to the document

**Files:**

- Modify: `backend/src/Invoicing.Documents/InvoiceDocumentData.cs`
- Modify: `backend/src/Invoicing.Api/Services/DocumentDataBuilder.cs`
- Test: `backend/tests/Invoicing.DocumentTests/GraveShareDocumentTests.cs`

**Interfaces:**

- Consumes: Task 1's `Invoice` snapshot properties and `InvoiceLine.PriceIncludesVat`.
- Produces on `InvoiceDocumentData`: `string? RecipientNumber`, `string? GraveNumber`, `string? GraveSiteName`, `string? GraveFieldNumber`, `string? GravePosition` (the member name, e.g. `"DoubleOutside"`). `LineData` gains a last positional parameter `bool PriceIncludesVat = false`.

- [ ] **Step 1: Write the failing test**

Append to `GraveShareDocumentTests.cs`:

```csharp
    [Fact]
    public void The_builder_hands_over_the_snapshot_and_the_vat_included_flag()
    {
        var invoice = GraveInvoice();
        invoice.RecipientNumber = "067200";
        invoice.GraveNumber = "1234";
        invoice.GraveSiteName = "Schineller Bäcke";
        invoice.GraveFieldNumber = "27";
        invoice.GravePosition = GravePosition.DoubleOutside;
        invoice.Lines[0].PriceIncludesVat = true;

        var data = new DocumentDataBuilder().Build(invoice);

        Assert.Equal("067200", data.RecipientNumber);
        Assert.Equal("1234", data.GraveNumber);
        Assert.Equal("Schineller Bäcke", data.GraveSiteName);
        Assert.Equal("27", data.GraveFieldNumber);
        Assert.Equal("DoubleOutside", data.GravePosition);
        Assert.True(data.Lines[0].PriceIncludesVat);
        Assert.False(data.Lines[1].PriceIncludesVat);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `scripts/dotnet.sh test Invoicing.sln --filter The_builder_hands_over_the_snapshot`
Expected: build FAIL — `'InvoiceDocumentData' does not contain a definition for 'RecipientNumber'`.

- [ ] **Step 3: Implement**

`InvoiceDocumentData.cs`, after `RecipientAddress`:

```csharp
    public string? RecipientNumber { get; set; }

    /// <summary>Set on grave invoices only. <see cref="GravePosition"/> is the
    /// member name ("DoubleOutside"); DocumentTexts turns it into the short
    /// form the invoice prints.</summary>
    public string? GraveNumber { get; set; }
    public string? GraveSiteName { get; set; }
    public string? GraveFieldNumber { get; set; }
    public string? GravePosition { get; set; }
```

and extend `LineData`:

```csharp
    public sealed record LineData(
        int Position,
        string ItemNumber,
        string Description,
        int Count,
        decimal UnitPrice,
        decimal VatRate,
        decimal LineTotal,
        decimal VatAmount,
        // the price was entered gross; UnitPrice is still the net
        bool PriceIncludesVat = false
    );
```

`DocumentDataBuilder.Build`: pass `l.PriceIncludesVat` as the last `LineData` argument, and in the returned initialiser after `RecipientAddress = invoice.RecipientAddress,`:

```csharp
            RecipientNumber = invoice.RecipientNumber,
            GraveNumber = invoice.GraveNumber,
            GraveSiteName = invoice.GraveSiteName,
            GraveFieldNumber = invoice.GraveFieldNumber,
            GravePosition = invoice.GravePosition?.ToString(),
```

- [ ] **Step 4: Run the backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Invoicing.Documents/InvoiceDocumentData.cs backend/src/Invoicing.Api/Services/DocumentDataBuilder.cs backend/tests/Invoicing.DocumentTests/GraveShareDocumentTests.cs
git commit -m "refactor(documents): pass the invoice snapshot and VAT-included lines to the document"
```

---

### Task 5: The scanned invoice layout

**Files:**

- Modify: `backend/src/Invoicing.Documents/DocumentTexts.cs` (rewrite)
- Modify: `backend/src/Invoicing.Documents/InvoiceDocument.cs` (header, content, footer, helpers)
- Create: `backend/tests/Invoicing.DocumentTests/InvoiceLayoutTests.cs`
- Modify: `backend/tests/Invoicing.DocumentTests/GermanDocumentTests.cs`, `GraveShareDocumentTests.cs` (the two PDF tests), `LetterheadTests.cs` (two tests on "Rechnung")

**Interfaces:**

- Consumes: Task 4's `InvoiceDocumentData` fields and `LineData.PriceIncludesVat`.
- Produces: the PDF texts later specs may look for: `**** RECHNUNG ****`, `NUMMER:`, `KUNDEN-NR:`, `LAUFWEG-NR:`, `DATUM:`, `SEITE:`, `GRABSTÄTTE: <name>`, `FELD-GRAB-NR: <nr> <short>`, column heads `Artikel / Leistungsbezeichnung`, `Menge`, `E-Preis €`, `Gesamt €`, `ST`, VAT cells `19 %` / `7 % Incl.`, block `19,00 %`, `Netto €`, `MWSt €`, `Nettobetrag`, `MWSt`, `Bruttobetrag`, and continuation `Rechnung <nr> · Kunden-Nr. <k> · Seite <n> von <N>`.

- [ ] **Step 1: Write the failing layout tests**

Create `backend/tests/Invoicing.DocumentTests/InvoiceLayoutTests.cs`:

```csharp
using System;
using System.Linq;
using Burkard.Invoicing.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Burkard.Invoicing.DocumentTests;

/// <summary>The layout of the owner's scanned invoices: details beside the
/// address, the grave site above the lines, the VAT rate in the last column,
/// and the VAT block at the foot of the last page.</summary>
public class InvoiceLayoutTests
{
    private const float PointsPerMillimetre = 72f / 25.4f;
    private const float PageHeight = 297 * PointsPerMillimetre;

    // a DIN lang window spans 20 mm to 110 mm from the sheet's left edge
    private const float WindowRight = 110 * PointsPerMillimetre;

    /// <summary>The mockup's grave: two lines at 19 %, one VAT-included line at
    /// 7 % (5 x 10,70 gross = 5 x 10,00 net), two recipients.</summary>
    private static InvoiceDocumentData GraveData() =>
        new()
        {
            InvoiceNumber = "2026-0412",
            GeneratedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
            RecipientName = "Werner Schineller",
            RecipientAddress = "Bartholomäus-Weltz-Straße 4\n67346 Speyer",
            RecipientNumber = "067200",
            GraveNumber = "1234",
            GraveSiteName = "Schineller Bäcke",
            GraveFieldNumber = "27",
            GravePosition = "DoubleOutside",
            EndingText = "Zahlbar sofort nach Erhalt der Rechnung ohne Abzug",
            StampWord = "Kopie",
            Lines = new[]
            {
                new InvoiceDocumentData.LineData(1, "12001", "Herbstbepflanzung", 1, 24.00m, 19m, 24.00m, 4.56m),
                new InvoiceDocumentData.LineData(2, "12500", "Herbstschale", 1, 40.00m, 19m, 40.00m, 7.60m),
                new InvoiceDocumentData.LineData(3, "55566", "Pflanzen", 5, 10.00m, 7m, 50.00m, 3.50m, PriceIncludesVat: true),
            },
            VatSummaries = new[]
            {
                new InvoiceDocumentData.VatSummary(7m, 50.00m, 3.50m, 53.50m),
                new InvoiceDocumentData.VatSummary(19m, 64.00m, 12.16m, 76.16m),
            },
            NetTotal = 114.00m,
            TotalVat = 15.66m,
            GrossTotal = 129.66m,
            Share = new InvoiceDocumentData.ShareData(
                50m,
                57.00m,
                new[]
                {
                    new InvoiceDocumentData.VatSummary(7m, 25.00m, 1.75m, 26.75m),
                    new InvoiceDocumentData.VatSummary(19m, 32.00m, 6.08m, 38.08m),
                },
                7.83m,
                64.83m
            ),
        };

    private static string Text(InvoiceDocumentData data)
    {
        using var document = PdfDocument.Open(DocumentService.GeneratePdf(data, isCopy: false));
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static Word FirstWord(Page page, string text) =>
        page.GetWords().First(w => w.Text == text);

    [Fact]
    public void The_details_block_names_number_customer_grave_date_and_page()
    {
        var text = Text(GraveData());

        Assert.Contains("**** RECHNUNG ****", text);
        Assert.Contains("NUMMER:", text);
        Assert.Contains("2026-0412", text);
        Assert.Contains("KUNDEN-NR:", text);
        Assert.Contains("067200", text);
        Assert.Contains("LAUFWEG-NR:", text);
        Assert.Contains("1234", text);
        Assert.Contains("DATUM:", text);
        Assert.Contains("02.10.2026", text);
        Assert.Contains("SEITE:", text);
        Assert.Contains("1 von 1", text);
        // the old centred page footer is gone
        Assert.DoesNotContain("Seite 1 von 1", text);
    }

    [Fact]
    public void The_details_block_sits_right_of_the_envelope_window()
    {
        using var document = PdfDocument.Open(
            DocumentService.GeneratePdf(GraveData(), isCopy: false)
        );
        var page = document.GetPage(1);

        Assert.True(FirstWord(page, "NUMMER:").BoundingBox.Left > WindowRight);
        Assert.True(FirstWord(page, "DATUM:").BoundingBox.Left > WindowRight);
    }

    [Fact]
    public void An_ordinary_invoice_has_no_grave_number_and_no_site_lines()
    {
        var data = GraveData();
        data.GraveNumber = null;
        data.GraveSiteName = null;
        data.GraveFieldNumber = null;
        data.GravePosition = null;
        data.Share = null;
        var text = Text(data);

        Assert.Contains("KUNDEN-NR:", text);
        Assert.DoesNotContain("LAUFWEG-NR", text);
        Assert.DoesNotContain("GRABSTÄTTE", text);
        Assert.DoesNotContain("FELD-GRAB-NR", text);
    }

    [Fact]
    public void An_invoice_without_a_recipient_number_leaves_the_line_out()
    {
        var data = GraveData();
        data.RecipientNumber = null;

        Assert.DoesNotContain("KUNDEN-NR", Text(data));
    }

    [Fact]
    public void The_grave_site_is_printed_above_the_lines()
    {
        var text = Text(GraveData());

        Assert.Contains("GRABSTÄTTE: Schineller Bäcke", text);
        Assert.Contains("FELD-GRAB-NR: 27 dGr.a", text);
        Assert.True(
            text.IndexOf("FELD-GRAB-NR", StringComparison.Ordinal)
                < text.IndexOf("Herbstbepflanzung", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("DoubleInside", "dGr.i")]
    [InlineData("SingleInside", "eGr.i")]
    [InlineData("SingleOutside", "eGr.a")]
    [InlineData("UrnInside", "uGr.i")]
    [InlineData("UrnOutside", "uGr.a")]
    public void Each_position_prints_its_short_form(string position, string shortForm)
    {
        var data = GraveData();
        data.GravePosition = position;

        Assert.Contains($"FELD-GRAB-NR: 27 {shortForm}", Text(data));
    }

    [Fact]
    public void A_grave_migrated_without_site_fields_prints_no_empty_lines()
    {
        var data = GraveData();
        data.GraveSiteName = "";
        data.GraveFieldNumber = "";
        data.GravePosition = null;
        var text = Text(data);

        Assert.DoesNotContain("GRABSTÄTTE", text);
        Assert.DoesNotContain("FELD-GRAB-NR", text);
        Assert.Contains("LAUFWEG-NR:", text);
    }

    [Fact]
    public void The_table_has_item_numbers_and_the_rate_in_the_last_column()
    {
        var text = Text(GraveData());

        Assert.Contains("Artikel / Leistungsbezeichnung", text);
        Assert.Contains("Menge", text);
        Assert.Contains("E-Preis €", text);
        Assert.Contains("Gesamt €", text);
        Assert.Contains("12001", text);
        Assert.Contains("19 %", text);
        Assert.Contains("7 % Incl.", text);
    }

    [Fact]
    public void A_vat_included_line_prints_its_gross_prices()
    {
        var text = Text(GraveData());

        // entered as 10,70 gross; 5 of them are 53,50
        Assert.Contains("10,70", text);
        Assert.Contains("53,50", text);
        Assert.DoesNotContain("10,00", text);
    }

    [Fact]
    public void A_share_invoice_bills_the_share_in_the_vat_block()
    {
        var text = Text(GraveData());

        Assert.Contains("Nettosumme gesamt 114,00 € – davon Ihr Anteil 50 %", text);
        Assert.Contains("7,00 %", text);
        Assert.Contains("19,00 %", text);
        Assert.Contains("Netto €", text);
        Assert.Contains("MWSt €", text);
        Assert.Contains("Nettobetrag", text);
        Assert.Contains("Bruttobetrag", text);
        Assert.Contains("57,00", text);
        Assert.Contains("7,83", text);
        Assert.Contains("64,83", text);
        // the 100 % VAT and gross are not what this recipient pays
        Assert.DoesNotContain("15,66", text);
        Assert.DoesNotContain("129,66", text);
    }

    [Fact]
    public void An_ordinary_invoice_bills_its_whole_amount()
    {
        var data = GraveData();
        data.Share = null;
        var text = Text(data);

        Assert.DoesNotContain("Nettosumme gesamt", text);
        Assert.Contains("114,00", text);
        Assert.Contains("15,66", text);
        Assert.Contains("129,66", text);
    }

    [Fact]
    public void An_old_grave_invoice_still_renders_its_share_note()
    {
        // generated before the share and the snapshot: RecipientCount null, so
        // no Share; the lines are already the share and the note says so
        var data = GraveData();
        data.Share = null;
        data.RecipientNumber = null;
        data.GraveNumber = null;
        data.GraveSiteName = null;
        data.GraveFieldNumber = null;
        data.GravePosition = null;
        data.ShareText = "Diese Rechnung enthält 50 % des Gesamtbetrags.";
        var text = Text(data);

        Assert.Contains("Diese Rechnung enthält 50 % des Gesamtbetrags.", text);
        Assert.DoesNotContain("LAUFWEG-NR", text);
        Assert.Contains("Bruttobetrag", text);
    }

    [Fact]
    public void The_vat_block_sits_below_the_ending_text_at_the_foot_of_the_page()
    {
        using var document = PdfDocument.Open(
            DocumentService.GeneratePdf(GraveData(), isCopy: false)
        );
        var page = document.GetPage(1);

        var ending = FirstWord(page, "Zahlbar").BoundingBox.Bottom;
        var gross = FirstWord(page, "Bruttobetrag").BoundingBox.Top;
        Assert.True(gross < ending, "the VAT block must come after the ending text");
        // pushed down to the foot of the content area, not straight after the text
        Assert.True(gross < 80 * PointsPerMillimetre, "the VAT block is not at the foot of the page");
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter InvoiceLayoutTests`
Expected: FAIL — `Assert.Contains() Failure: Sub-string not found` for `**** RECHNUNG ****`.

- [ ] **Step 3: Rewrite `DocumentTexts`**

Replace the whole of `backend/src/Invoicing.Documents/DocumentTexts.cs` with:

```csharp
using System.Globalization;

namespace Burkard.Invoicing.Documents;

/// <summary>Every string a generated document shows. Documents are always
/// German, with German number and date formats, and follow the layout of the
/// old system's invoices.</summary>
public static class DocumentTexts
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("de-DE");

    public const string Title = "**** RECHNUNG ****";
    public const string LabelNumber = "NUMMER:";
    public const string LabelCustomerNumber = "KUNDEN-NR:";
    public const string LabelGraveNumber = "LAUFWEG-NR:";
    public const string LabelDate = "DATUM:";
    public const string LabelPage = "SEITE:";
    public const string PageOf = " von ";
    public const string PageWord = "Seite";
    public const string CustomerNumberShort = "Kunden-Nr.";

    public const string ColumnDescription = "Artikel / Leistungsbezeichnung";
    public const string ColumnCount = "Menge";
    public const string ColumnUnitPrice = "E-Preis €";
    public const string ColumnTotal = "Gesamt €";
    public const string ColumnVat = "ST";
    public const string VatIncluded = "Incl.";

    public const string NetRow = "Netto €";
    public const string VatRow = "MWSt €";
    public const string Currency = "€";
    public const string NetAmount = "Nettobetrag";
    public const string VatAmount = "MWSt";
    public const string GrossAmount = "Bruttobetrag";

    public static string ShareText(decimal percent) =>
        string.Format(
            Culture,
            "Diese Rechnung enthält {0} % des Gesamtbetrags.",
            percent.ToString("0.##", Culture)
        );

    public static string InvoiceTitle(string invoiceNumber) =>
        string.Format(Culture, "Rechnung {0}", invoiceNumber);

    public static string GraveSite(string name) => $"GRABSTÄTTE: {name}";

    /// <summary>"FELD-GRAB-NR: 27 dGr.a", with whatever of the two exists.</summary>
    public static string GraveField(string? fieldNumber, string? positionShortForm) =>
        "FELD-GRAB-NR: "
        + string.Join(
            " ",
            new[] { fieldNumber, positionShortForm }.Where(s => !string.IsNullOrWhiteSpace(s))
        );

    /// <summary>The old system's abbreviation of a grave position, from the
    /// position's member name; null for none or an unknown name.</summary>
    public static string? PositionShortForm(string? position) =>
        position switch
        {
            "DoubleInside" => "dGr.i",
            "DoubleOutside" => "dGr.a",
            "SingleInside" => "eGr.i",
            "SingleOutside" => "eGr.a",
            "UrnInside" => "uGr.i",
            "UrnOutside" => "uGr.a",
            _ => null,
        };

    /// <summary>"19 %", or "7,7 %" for a rate with a decimal: the line's ST cell.</summary>
    public static string VatRate(decimal rate) => $"{rate.ToString("0.#", Culture)} %";

    /// <summary>"19,00 %": a column head of the VAT block.</summary>
    public static string VatRateHeading(decimal rate) => $"{rate.ToString("N2", Culture)} %";

    /// <summary>Above the VAT block of an invoice that bills one recipient's
    /// share: the grave's whole net, and the share of it the block bills.</summary>
    public static string ShareLine(decimal netTotal, decimal percent) =>
        string.Format(
            Culture,
            "Nettosumme gesamt {0} € – davon Ihr Anteil {1} %",
            netTotal.ToString("N2", Culture),
            percent.ToString("0.##", Culture)
        );
}
```

- [ ] **Step 4: Rewrite the page in `InvoiceDocument`**

In `Compose`, keep the page setup, background and copy stamp. Replace everything from `page.Header()` to the end of the `page.Footer()` call with:

```csharp
            page.Header()
                .Column(column =>
                {
                    column.Item().ShowOnce().Element(FirstPageHead);
                    column.Item().SkipOnce().Element(ContinuationLine);
                });

            page.Content()
                .Column(column =>
                {
                    // The old share note above the table only ever applied when the
                    // lines were already the share (Share is null); once Share is
                    // set, the VAT block bills the share and says so itself.
                    if (_data.Share is null && !string.IsNullOrEmpty(_data.ShareText))
                    {
                        column.Item().PaddingBottom(8).Text(_data.ShareText).SemiBold();
                    }

                    column.Item().Table(LineTable);

                    if (!string.IsNullOrEmpty(_data.EndingText))
                    {
                        column.Item().PaddingTop(16).Text(_data.EndingText);
                    }

                    // At the foot of the last page, as on the old stationery: the
                    // item takes the rest of the page and puts the block at its
                    // bottom. ShowEntire moves it to a new page as a whole when it
                    // does not fit under the lines.
                    column
                        .Item()
                        .ExtendVertical()
                        .AlignBottom()
                        .ShowEntire()
                        .PaddingTop(10)
                        .Element(VatBlock);
                });
```

(No `page.Footer()`: the page number moved into the head.)

Replace `AddressBlock` and `ContinuationLine` and add the new helpers (keep `Money`, `UnitPrice` and `HeaderCell`):

```csharp
    // The details start right of the DIN lang window's edge at 110 mm.
    private const float DetailsWidthMillimetre = 72;

    /// <summary>The first page's head: the recipient in the envelope's window,
    /// the invoice's details beside it.</summary>
    private void FirstPageHead(IContainer container) =>
        container
            .PaddingBottom(6, Unit.Millimetre)
            .Row(row =>
            {
                // The letterhead prints the sender line immediately above this, so
                // the recipient goes directly under it: folded for a DIN lang
                // envelope, both addresses then show through the window, which
                // reveals the sheet from 45 mm to 90 mm below its top edge. The
                // row is at least that tall, so the table always starts clear of
                // the window; a longer address pushes it down instead.
                row.RelativeItem()
                    .PaddingLeft(
                        Letterhead.AddressFieldLeftMillimetres - SideMarginMillimetre,
                        Unit.Millimetre
                    )
                    .MinHeight(
                        WindowBottomMillimetre + 2 - Letterhead.HeaderHeightMillimetres,
                        Unit.Millimetre
                    )
                    .Column(recipient =>
                    {
                        recipient.Item().Text(_data.RecipientName).SemiBold();
                        recipient.Item().Text(_data.RecipientAddress);
                    });
                row.ConstantItem(DetailsWidthMillimetre, Unit.Millimetre).Element(Details);
            });

    private void Details(IContainer container) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(2, Unit.Millimetre).Text(DocumentTexts.Title);
            column
                .Item()
                .Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(28, Unit.Millimetre);
                        cols.RelativeColumn();
                    });

                    void Row(string label, string value)
                    {
                        table.Cell().Element(DetailCell).Text(label);
                        table.Cell().Element(DetailCell).Text(value);
                    }

                    Row(DocumentTexts.LabelNumber, _data.InvoiceNumber);
                    if (!string.IsNullOrWhiteSpace(_data.RecipientNumber))
                    {
                        Row(DocumentTexts.LabelCustomerNumber, _data.RecipientNumber);
                    }

                    if (!string.IsNullOrWhiteSpace(_data.GraveNumber))
                    {
                        Row(DocumentTexts.LabelGraveNumber, _data.GraveNumber);
                    }

                    Row(DocumentTexts.LabelDate, _data.GeneratedAt.ToString("d", DocumentTexts.Culture));
                    table.Cell().Element(DetailCell).Text(DocumentTexts.LabelPage);
                    table
                        .Cell()
                        .Element(DetailCell)
                        .Text(text =>
                        {
                            text.CurrentPageNumber();
                            text.Span(DocumentTexts.PageOf);
                            text.TotalPages();
                        });
                });
        });

    /// <summary>
    /// Every page after the first. Only the sheet that goes in the envelope needs the
    /// address block; the rest just have to say which invoice they belong to.
    /// </summary>
    private void ContinuationLine(IContainer container) =>
        container
            .PaddingBottom(6, Unit.Millimetre)
            .Text(text =>
            {
                text.Span(DocumentTexts.InvoiceTitle(_data.InvoiceNumber)).SemiBold();
                if (!string.IsNullOrWhiteSpace(_data.RecipientNumber))
                {
                    text.Span($" · {DocumentTexts.CustomerNumberShort} {_data.RecipientNumber}");
                }

                text.Span($" · {DocumentTexts.PageWord} ");
                text.CurrentPageNumber();
                text.Span(DocumentTexts.PageOf);
                text.TotalPages();
            });

    private void LineTable(TableDescriptor table)
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(16, Unit.Millimetre); // item number
            cols.RelativeColumn(); // description
            cols.ConstantColumn(16, Unit.Millimetre); // count
            cols.ConstantColumn(22, Unit.Millimetre); // unit price
            cols.ConstantColumn(24, Unit.Millimetre); // total
            cols.ConstantColumn(20, Unit.Millimetre); // VAT rate
        });

        table.Header(header =>
        {
            header.Cell().Element(HeaderCell);
            header.Cell().Element(HeaderCell).Text(DocumentTexts.ColumnDescription);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnCount);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnUnitPrice);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnTotal);
            header.Cell().Element(HeaderCell).AlignRight().Text(DocumentTexts.ColumnVat);
        });

        var site = SiteLines();
        for (var i = 0; i < site.Count; i++)
        {
            table.Cell();
            table
                .Cell()
                .ColumnSpan(5)
                .PaddingTop(i == 0 ? 2 : 0, Unit.Millimetre)
                .PaddingBottom(i == site.Count - 1 ? 3 : 0, Unit.Millimetre)
                .Text(site[i])
                .SemiBold();
        }

        foreach (var line in _data.Lines)
        {
            // A VAT-included line prints what was entered, the gross. The net
            // it was converted to is exact (saving refuses a gross that is not),
            // so the gross comes back unchanged.
            var unitPrice = line.PriceIncludesVat
                ? decimal.Round(
                    line.UnitPrice * (1m + line.VatRate / 100m),
                    2,
                    MidpointRounding.AwayFromZero
                )
                : line.UnitPrice;
            var total = line.PriceIncludesVat ? unitPrice * line.Count : line.LineTotal;
            var rate = DocumentTexts.VatRate(line.VatRate);

            table.Cell().Text(line.ItemNumber);
            table.Cell().Text(line.Description);
            table.Cell().AlignRight().Text(line.Count.ToString(DocumentTexts.Culture));
            table.Cell().AlignRight().Text(UnitPrice(unitPrice));
            table.Cell().AlignRight().Text(Money(total));
            table
                .Cell()
                .AlignRight()
                .Text(line.PriceIncludesVat ? $"{rate} {DocumentTexts.VatIncluded}" : rate);
        }
    }

    /// <summary>The grave site lines above the items; each one only when there
    /// is something to print on it.</summary>
    private List<string> SiteLines()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_data.GraveSiteName))
        {
            lines.Add(DocumentTexts.GraveSite(_data.GraveSiteName));
        }

        var shortForm = DocumentTexts.PositionShortForm(_data.GravePosition);
        if (!string.IsNullOrWhiteSpace(_data.GraveFieldNumber) || shortForm is not null)
        {
            lines.Add(DocumentTexts.GraveField(_data.GraveFieldNumber, shortForm));
        }

        return lines;
    }

    /// <summary>Net and VAT per rate on the left, the totals on the right. On a
    /// grave invoice split across recipients it bills this recipient's share,
    /// and a line above it says what that is a share of.</summary>
    private void VatBlock(IContainer container)
    {
        var summaries = _data.Share?.VatSummaries ?? _data.VatSummaries;
        var net = _data.Share?.NetTotal ?? _data.NetTotal;
        var vat = _data.Share?.VatTotal ?? _data.TotalVat;
        var gross = _data.Share?.GrossTotal ?? _data.GrossTotal;

        container.Column(column =>
        {
            if (_data.Share is { } share)
            {
                column
                    .Item()
                    .PaddingBottom(2, Unit.Millimetre)
                    .Text(DocumentTexts.ShareLine(_data.NetTotal, share.Percent));
            }

            column
                .Item()
                .BorderTop(1)
                .BorderColor(Colors.Grey.Darken2)
                .PaddingTop(2)
                .Row(row =>
                {
                    row.RelativeItem()
                        .Table(rates =>
                        {
                            rates.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(16, Unit.Millimetre);
                                foreach (var _ in summaries)
                                {
                                    cols.RelativeColumn();
                                }
                            });

                            rates.Cell();
                            foreach (var s in summaries)
                            {
                                rates.Cell().AlignRight().Text(DocumentTexts.VatRateHeading(s.Rate));
                            }

                            rates.Cell().Text(DocumentTexts.NetRow);
                            foreach (var s in summaries)
                            {
                                rates.Cell().AlignRight().Text(Money(s.NetTotal));
                            }

                            rates.Cell().Text(DocumentTexts.VatRow);
                            foreach (var s in summaries)
                            {
                                rates.Cell().AlignRight().Text(Money(s.VatAmount));
                            }
                        });

                    row.ConstantItem(10, Unit.Millimetre);

                    row.ConstantItem(60, Unit.Millimetre)
                        .Table(totals =>
                        {
                            totals.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn();
                                cols.ConstantColumn(24, Unit.Millimetre);
                            });

                            totals.Cell();
                            totals.Cell().AlignRight().Text(DocumentTexts.Currency).SemiBold();
                            totals.Cell().Text(DocumentTexts.NetAmount);
                            totals.Cell().AlignRight().Text(Money(net));
                            totals.Cell().Text(DocumentTexts.VatAmount);
                            totals.Cell().AlignRight().Text(Money(vat));
                            totals.Cell().Text(DocumentTexts.GrossAmount).SemiBold();
                            totals.Cell().AlignRight().Text(Money(gross)).SemiBold();
                        });
                });
        });
    }

    private static IContainer DetailCell(IContainer container) =>
        container.PaddingBottom(1.5f, Unit.Millimetre);
```

Add `using System.Collections.Generic;` and `using System.Linq;` where needed (`DocumentTexts` uses `Where`). If the project does not have implicit usings for `QuestPDF.Elements.Table`, add `using QuestPDF.Elements.Table;` for `TableDescriptor`.

- [ ] **Step 5: Run the layout tests**

Run: `scripts/dotnet.sh test Invoicing.sln --filter InvoiceLayoutTests`
Expected: PASS. If `The_vat_block_sits_below_the_ending_text_at_the_foot_of_the_page` fails because the block follows the text directly, `ExtendVertical` is not taking the remaining height: check that it is the column's last item and that nothing after it adds content.

- [ ] **Step 6: Bring the older document tests to the new texts**

- `GermanDocumentTests.Documents_use_german_labels_and_number_format`: replace the asserts with `**** RECHNUNG ****`, `2026-0004`, `Artikel / Leistungsbezeichnung`, `Bruttobetrag`, `240,00`, `15.01.2026`.
- `GraveShareDocumentTests.A_grave_invoice_prints_each_rate_label_exactly_once`: rename to `A_grave_invoice_bills_the_share_per_rate` and assert the share line `Nettosumme gesamt 216,21 € – davon Ihr Anteil 33,33 %`, the block heads `7,00 %` and `19,00 %` once each, the share's per-rate figures `39,50`, `2,77`, `32,57`, `6,19`, the gross `81,03`, and that the 100 % VAT figures `8,30` and `18,56` do not appear. Keep the comments that explain why only the share's rows are printed, reworded to the block.
- `GraveShareDocumentTests.A_non_grave_invoice_still_prints_its_own_rate_block`: assert `118,50`, `8,30`, `97,71`, `18,56`, `216,21`, `26,86`, `243,07`, and no `Nettosumme gesamt`.
- `LetterheadTests.Invoice_number_is_printed_below_the_recipient`: replace with `Invoice_details_sit_beside_the_recipient` — `TopOf(page, "NUMMER:")` is within 12 mm of `TopOf(page, "Familie")`.
- `LetterheadTests.Invoice_number_clears_the_din_lang_window`: keep the theory, rename to `The_line_table_starts_below_the_din_lang_window`, assert `TopOf(page, "Artikel") < WindowBottom`.
- `LetterheadTests.Continuation_pages_replace_the_address_block_with_one_line`: additionally assert each continuation page contains `Seite` and does not contain `NUMMER:`.

Add to `LetterheadTests`:

```csharp
    // The VAT block comes once, on the last page, and that page is still on the
    // stationery. MultiPageData's 40 lines and long ending text fill page one,
    // so the block can only land on a later page.
    [Fact]
    public void The_vat_block_on_the_last_page_keeps_the_letterhead()
    {
        var data = MultiPageData();
        var pdf = DocumentService.GeneratePdf(data, isCopy: false);

        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().ToList();
        var last = pages[^1];

        Assert.Contains("Bruttobetrag", last.Text);
        Assert.Equal(1, pages.Count(p => p.Text.Contains("Bruttobetrag")));
        var images = last.GetImages().ToList();
        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => i.BoundingBox.Bottom > HeaderBandBottom);
        Assert.Contains(images, i => i.BoundingBox.Top < FooterBandTop);
    }
```

- [ ] **Step 7: Run the whole backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS, including `DeterminismTests`, `CopyStampTests` and every `LetterheadTests` band test.

- [ ] **Step 8: Look at page two**

CLAUDE.md asks for a look, not only tests. Write `MultiPageShareData()`'s PDF to the scratchpad from a throwaway test or `dotnet script`, open page 1 and page 2, and check: details block right of the address and inside the content area, site lines bold above the table, ST column readable, VAT block at the foot of the last page, letterhead complete on page 2. Do not commit the throwaway code.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Invoicing.Documents backend/tests/Invoicing.DocumentTests
git commit -m "feat(documents): lay invoices out like the scanned originals"
```

---

### Task 6: Import the new grave columns

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs` (`GravesHeader`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportModel.cs` (`GraveRow`)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportValidator.cs` (`ReadGraves`; remove `TryParseDate` if nothing else uses it)
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportService.cs` (grave creation)
- Modify: `frontend/src/i18n/de.json` (`importProblemDateInvalid`, new `importProblemPositionInvalid`)
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`

**Interfaces:**

- Consumes: Task 2's `GraveTerm.TryParseMonth`, `GraveTerm.TryParsePosition`.
- Produces: `graves.csv` header `GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate`; `GraveRow(int Row, string Number, string FieldNumber, string SiteName, GravePosition Position, DateOnly StartDate, DateOnly? EndDate)`; problem code `PositionInvalid` with args `[raw, "DoubleInside, DoubleOutside, …"]`. Task 8's E2E fixtures use this header.

- [ ] **Step 1: Move the existing fixtures to the new header**

```bash
cd backend/tests/Invoicing.IntegrationTests
sed -i 's/private const string GravesHeader = "GraveNumber,StartDate,EndDate\\r\\n";/private const string GravesHeader = "GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate\\r\\n";/' ImportTests.cs
sed -i -E 's/"(IMP-[0-9]+),([^,"]*),([^,"]*)\\r\\n"/"\1,27,Grabstätte Import,DoubleOutside,\2,\3\\r\\n"/g' ImportTests.cs
cd -
git diff --stat backend/tests
```

Check the diff: every `graves.csv` row now has six columns, and no `mapping.csv` row changed.

- [ ] **Step 2: Write the failing tests**

Append to `ImportTests.cs`:

```csharp
    [Fact]
    public async Task Graves_import_with_site_fields_month_terms_and_an_open_end()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader + "E-1000,Anna Beispiel,Hauptstraße 1\r\n"),
            ("items.csv", ItemsHeader + "9001,Grabpflege,7,120.00\r\n"),
            ("graves.csv", GravesHeader + "IMP-1,27,Schineller Bäcke,DoubleOutside,2026-03,\r\n"),
            ("mapping.csv", MappingHeader + "Item,IMP-1,9001,1,\r\n" + "Recipient,IMP-1,E-1000,,\r\n")
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grave = await db.Graves.SingleAsync();
        Assert.Equal("27", grave.FieldNumber);
        Assert.Equal("Schineller Bäcke", grave.SiteName);
        Assert.Equal(GravePosition.DoubleOutside, grave.Position);
        Assert.Equal(new DateOnly(2026, 3, 1), grave.StartDate);
        Assert.Null(grave.EndDate);
    }

    [Theory]
    [InlineData("IMP-1,27,Grabstätte,Sideways,2026-01,", "PositionInvalid")]
    [InlineData("IMP-1,,Grabstätte,DoubleOutside,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,,DoubleOutside,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,Grabstätte,,2026-01,", "FieldRequired")]
    [InlineData("IMP-1,27,Grabstätte,DoubleOutside,2026-13,", "DateInvalid")]
    [InlineData("IMP-1,27,Grabstätte,DoubleOutside,2026-05,2026-04", "DateRangeInvalid")]
    public async Task Invalid_grave_rows_are_reported(string row, string code)
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", GravesHeader + row + "\r\n"),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "graves.csv" && p.Row == 2 && p.Code == code);
    }

    [Fact]
    public async Task An_archive_with_the_old_three_column_graves_file_is_refused()
    {
        using var host = new ApiTestHost();
        await ConfigureVatAsync(host, 7m);
        using var client = host.CreateClient();

        var archive = Zip(
            ("recipients.csv", RecipientsHeader),
            ("items.csv", ItemsHeader),
            ("graves.csv", "GraveNumber,StartDate,EndDate\r\nIMP-1,2026-01-01,2040-12-31\r\n"),
            ("mapping.csv", MappingHeader)
        );
        var response = await client.PostAsync("/odata/ImportArchive", Upload(archive));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problems = await ProblemsAsync(response);
        Assert.Contains(problems, p => p.File == "graves.csv" && p.Code == "HeaderInvalid");
    }
```

Use the header problem's actual code (look for `FR-056` in `ImportValidator.cs`) if it is not `HeaderInvalid`. Add `using Burkard.Invoicing.Domain.Entities;` if missing.

- [ ] **Step 3: Run them to verify they fail**

Run: `scripts/dotnet.sh test Invoicing.sln --filter ImportTests`
Expected: FAIL — the moved fixtures now report `HeaderInvalid`, and the new tests fail.

- [ ] **Step 4: Implement**

`ImportArchiveLayout.cs`:

```csharp
    public static readonly string[] GravesHeader =
    {
        "GraveNumber",
        "FieldNumber",
        "SiteName",
        "Position",
        "StartDate",
        "EndDate",
    };
```

`ImportModel.cs`:

```csharp
public sealed record GraveRow(
    int Row,
    string Number,
    string FieldNumber,
    string SiteName,
    GravePosition Position,
    DateOnly StartDate,
    DateOnly? EndDate
);
```

`ImportValidator.ReadGraves`, replace the body of the row loop up to (not including) the `seen.TryGetValue` duplicate check with:

```csharp
            var row = rows[i];
            var line = i + 1;
            if (!HasColumns(row, 6, file, line, problems))
                continue;

            var number = row[0].Trim();
            var fieldNumber = row[1].Trim();
            var siteName = row[2].Trim();
            var rawPosition = row[3].Trim();
            var rawStart = row[4].Trim();
            var rawEnd = row[5].Trim();
            if (number.Length > 0)
                data.DeclaredGraves.Add(number);
            if (
                Missing(number, file, line, "GraveNumber", problems)
                | Missing(fieldNumber, file, line, "FieldNumber", problems)
                | Missing(siteName, file, line, "SiteName", problems)
                | Missing(rawPosition, file, line, "Position", problems)
                | Missing(rawStart, file, line, "StartDate", problems)
            )
            {
                continue;
            }

            if (!GraveTerm.TryParsePosition(rawPosition, out var position))
            {
                var allowed = string.Join(", ", Enum.GetNames<GravePosition>());
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "PositionInvalid",
                        $"'{rawPosition}' is not a grave position (expected one of {allowed}).",
                        "PositionInvalid",
                        [rawPosition, allowed]
                    )
                );
                continue;
            }

            if (!GraveTerm.TryParseMonth(rawStart, out var start))
            {
                problems.Add(MonthInvalid(file, line, rawStart));
                continue;
            }

            DateOnly? end = null;
            if (rawEnd.Length > 0)
            {
                if (!GraveTerm.TryParseMonth(rawEnd, out var parsedEnd))
                {
                    problems.Add(MonthInvalid(file, line, rawEnd));
                    continue;
                }

                end = parsedEnd;
            }

            if (end < start)
            {
                problems.Add(
                    new ImportProblem(
                        file,
                        line,
                        "DateRangeInvalid",
                        "The end month must not be before the start month.",
                        "DateRangeInvalid"
                    )
                );
                continue;
            }
```

and after the duplicate check: `data.Graves.Add(new GraveRow(line, number, fieldNumber, siteName, position, start, end));`. Add the helper:

```csharp
    private static ImportProblem MonthInvalid(string file, int line, string raw) =>
        new(
            file,
            line,
            "DateInvalid",
            $"'{raw}' is not a valid month (expected YYYY-MM).",
            "DateInvalid",
            [raw]
        );
```

Run `grep -n "TryParseDate" backend/src -r`; delete `TryParseDate` if `ReadGraves` was its only caller. Add `using Burkard.Invoicing.Domain.Entities;` to `ImportValidator.cs` and `ImportModel.cs`.

`ImportService`, in the grave initialiser:

```csharp
            row => new Grave
            {
                GraveNumber = row.Number,
                FieldNumber = row.FieldNumber,
                SiteName = row.SiteName,
                Position = row.Position,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                CreatedAt = createdAt,
            },
```

If `ImportExportController.Template()` writes example rows, give them the six columns too.

`frontend/src/i18n/de.json`:

```json
  "importProblemDateInvalid": "„{0}“ ist kein gültiger Monat (erwartet: JJJJ-MM).",
  "importProblemPositionInvalid": "„{0}“ ist keine Grabposition; erlaubt sind: {1}.",
```

- [ ] **Step 5: Run the backend suite**

Run: `scripts/dotnet.sh test Invoicing.sln`
Expected: PASS.

- [ ] **Step 6: Commit (the breaking change)**

```bash
git add backend/src/Invoicing.Api/Services/Import backend/src/Invoicing.Api/OData/Controllers/ImportExportController.cs \
  backend/tests/Invoicing.IntegrationTests/ImportTests.cs frontend/src/i18n/de.json
git commit -F - <<'MSG'
feat(import)!: import grave site fields and month terms

graves.csv carries the field number, site name and position of each grave,
and its start and end are months; the end may be left empty.

BREAKING CHANGE: graves.csv has six columns now
(GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate); an archive
with the old three-column file is refused. Download the template again.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MSG
```

---

### Task 7: Frontend — editor, lists, detail page, error texts

**Files:**

- Modify (generated): `frontend/src/api/metadata.xml`, `frontend/src/api/generated/**`
- Create: `frontend/src/lib/gravePosition.ts`
- Modify: `frontend/src/lib/format.ts`
- Modify: `frontend/src/api/endpoints.ts` (`GraveStatusRow`, `SaveGraveInput`, `GraveDetails`)
- Modify: `frontend/src/components/GraveEditor.vue`
- Modify: `frontend/src/pages/GravesPage.vue`, `frontend/src/pages/GraveDetailPage.vue`
- Modify: `frontend/src/composables/useErrorToast.ts`
- Modify: `frontend/src/i18n/de.json`

**Interfaces:**

- Consumes: Task 2's HTTP contract.
- Produces: `GRAVE_POSITIONS` and `type GravePositionName` in `@/lib/gravePosition`; `toIsoMonth(date: Date | null | undefined): string | null` and `formatMonth(value: string | null | undefined): string` in `@/lib/format`. Editor field ids `graveFieldNumber`, `graveSiteName`, `gravePosition`, `graveStart`, `graveEnd`; labels "Grabnummer", "Feld-Nr.", "Grabstätte", "Grabposition", "Beginn", "Ende" (Task 8 locates by these).

- [ ] **Step 1: Regenerate the OData client**

```bash
docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build
cd frontend && npm ci && npm run gen:odata && cd -
git diff --stat frontend/src/api
```

Expected: `metadata.xml` gains `FieldNumber`, `SiteName`, `Position` (an `EnumType` `GravePosition`) on `Grave`, `EndDate` nullable, and the five invoice properties; `Language` is already gone. The generated `Grave` and `Invoice` models change accordingly.

- [ ] **Step 2: Add the month helpers and the position list**

`frontend/src/lib/format.ts`, after `parseIsoDate`:

```ts
/** "2026-03" from a month picker's value, by its local fields (never
 *  toISOString, which is the previous day -- and at the turn of a month the
 *  previous month -- here). Null for no value. */
export function toIsoMonth(date: Date | null | undefined): string | null {
  if (!date) {
    return null;
  }
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}

/** "03/2026" from an Edm.Date ("2026-03-01"); empty for none. */
export function formatMonth(value: string | null | undefined): string {
  if (!value) {
    return "";
  }
  const [year, month] = value.split("-");
  return `${month}/${year}`;
}
```

Create `frontend/src/lib/gravePosition.ts`:

```ts
/** The backend's GravePosition members, in the order the editor offers them. */
export const GRAVE_POSITIONS = [
  "DoubleInside",
  "DoubleOutside",
  "SingleInside",
  "SingleOutside",
  "UrnInside",
  "UrnOutside",
] as const;

export type GravePositionName = (typeof GRAVE_POSITIONS)[number];
```

- [ ] **Step 3: Extend the hand-typed endpoints**

In `endpoints.ts`: `GraveStatusRow` gets `FieldNumber: string; SiteName: string;` and `EndDate: string | null;`. `GraveDetails` gets `FieldNumber: string; SiteName: string; Position: GravePositionName | null;` and `EndDate: string | null;`. `SaveGraveInput` becomes:

```ts
export interface SaveGraveInput {
  graveId: number | null;
  graveNumber: string;
  fieldNumber: string;
  siteName: string;
  position: GravePositionName | null;
  /** "yyyy-MM" */
  startDate: string | null;
  /** "yyyy-MM"; null for a grave without an end */
  endDate: string | null;
  items: InvoiceLineInput[];
  recipientIds: number[];
}
```

with `import type { GravePositionName } from "@/lib/gravePosition";`.

- [ ] **Step 4: The editor**

In `GraveEditor.vue`:

- Imports: add `import Select from "primevue/select";`, `import { computed } from "vue"` (merge with the existing vue import), `import { GRAVE_POSITIONS, type GravePositionName } from "@/lib/gravePosition";`, and replace `parseIsoDate, toIsoDate` with `parseIsoDate, toIsoMonth`.
- Form state and both `Object.assign` calls:

```ts
const form = reactive({
  GraveNumber: "",
  FieldNumber: "",
  SiteName: "",
  Position: null as GravePositionName | null,
  StartDate: null as Date | null,
  EndDate: null as Date | null,
});
const positionOptions = computed(() =>
  GRAVE_POSITIONS.map((value) => ({ value, label: t(`gravePosition${value}`) })),
);
```

```ts
Object.assign(form, {
  GraveNumber: "",
  FieldNumber: "",
  SiteName: "",
  Position: null,
  StartDate: null,
  EndDate: null,
});
```

```ts
Object.assign(form, {
  GraveNumber: grave.GraveNumber,
  FieldNumber: grave.FieldNumber,
  SiteName: grave.SiteName,
  Position: grave.Position,
  StartDate: parseIsoDate(grave.StartDate),
  EndDate: parseIsoDate(grave.EndDate),
});
```

- The save call:

```ts
await saveGrave({
  graveId: props.graveId,
  graveNumber: form.GraveNumber,
  fieldNumber: form.FieldNumber,
  siteName: form.SiteName,
  position: form.Position,
  startDate: toIsoMonth(form.StartDate),
  endDate: toIsoMonth(form.EndDate),
  items: inputs,
  recipientIds: recipients.value.map((recipient) => recipient.id),
});
```

- Replace the `<div class="grid gap-4 sm:grid-cols-3">…</div>` block with:

```vue
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <div class="flex flex-col gap-2">
          <label for="graveNumber" class="font-medium">{{ t("graveNumber") }}</label>
          <InputText id="graveNumber" v-model="form.GraveNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveFieldNumber" class="font-medium">{{ t("graveFieldNumber") }}</label>
          <InputText id="graveFieldNumber" v-model="form.FieldNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveSiteName" class="font-medium">{{ t("graveSiteName") }}</label>
          <InputText id="graveSiteName" v-model="form.SiteName" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="gravePosition" class="font-medium">{{ t("gravePosition") }}</label>
          <Select
            v-model="form.Position"
            input-id="gravePosition"
            :options="positionOptions"
            option-label="label"
            option-value="value"
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveStart" class="font-medium">{{ t("startDate") }}</label>
          <DatePicker
            v-model="form.StartDate"
            input-id="graveStart"
            view="month"
            date-format="mm/yy"
            show-icon
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveEnd" class="font-medium">{{ t("endDate") }}</label>
          <DatePicker
            v-model="form.EndDate"
            input-id="graveEnd"
            view="month"
            date-format="mm/yy"
            show-icon
            show-button-bar
            fluid
          />
        </div>
      </div>
```

`show-button-bar` gives the end picker PrimeVue's "Löschen" button for taking an end away again. If `frontend/node_modules/primevue/datepicker/index.d.ts` declares `showClear`, use `show-clear` instead.

- [ ] **Step 5: Lists and the detail page**

`GravesPage.vue`: import `formatMonth` instead of `formatDate` (keep `formatDate` only if still used). After the grave number column:

```vue
<Column
  :header="t('graveSiteName')"
  header-class="hidden md:table-cell"
  body-class="hidden md:table-cell"
>
          <template #body="{ data }">{{ data.SiteName }}</template>
        </Column>
<Column
  :header="t('graveFieldNumber')"
  header-class="hidden lg:table-cell"
  body-class="hidden lg:table-cell"
>
          <template #body="{ data }">{{ data.FieldNumber }}</template>
        </Column>
```

and the start/end bodies become `{{ formatMonth(data.StartDate) }}` and `{{ formatMonth(data.EndDate) || "–" }}`.

`GraveDetailPage.vue`: the card's start/end use `formatMonth` (end `|| "–"`), and three more entries go before them:

```vue
<div>
            <dt class="text-sm text-muted-color">{{ t("graveFieldNumber") }}</dt>
            <dd class="m-0">{{ grave.FieldNumber }}</dd>
          </div>
<div>
            <dt class="text-sm text-muted-color">{{ t("graveSiteName") }}</dt>
            <dd class="m-0">{{ grave.SiteName }}</dd>
          </div>
<div>
            <dt class="text-sm text-muted-color">{{ t("gravePosition") }}</dt>
            <dd class="m-0">{{ grave.Position ? t(`gravePosition${grave.Position}`) : "" }}</dd>
          </div>
```

If `grave` on that page comes from the generated client rather than `fetchGraveDetails`, read the generated `Grave` type for the property names.

- [ ] **Step 6: Error texts with arguments**

`useErrorToast.ts`, replace the body of `showError` after the busy check:

```ts
const { code, body } = toApiError(error);
// A refusal that names things (GraveNumberInUse) sends them as args; its
// text is chosen by how many there are, e.g. errorGraveNumberInUse2.
const args = (body as { error?: { args?: unknown } } | undefined)?.error?.args;
const list = Array.isArray(args) ? args.map(String) : undefined;
const key = list ? `error${code}${list.length}` : `error${code}`;
toast.add({
  severity: "error",
  summary: t("errorTitle"),
  detail: i18n.te(key) ? (list ? t(key, list) : t(key)) : `${t("errorGeneric")} (${code})`,
  life: 8000,
});
```

`de.json` — add, and change `errorInvalidDate`/`errorDateRangeInvalid`:

```json
  "graveFieldNumber": "Feld-Nr.",
  "graveSiteName": "Grabstätte",
  "gravePosition": "Grabposition",
  "gravePositionDoubleInside": "Doppelgrab innen",
  "gravePositionDoubleOutside": "Doppelgrab außen",
  "gravePositionSingleInside": "Einzelgrab innen",
  "gravePositionSingleOutside": "Einzelgrab außen",
  "gravePositionUrnInside": "Urnengrab innen",
  "gravePositionUrnOutside": "Urnengrab außen",
  "errorFieldNumberRequired": "Die Feld-Nr. darf nicht leer sein.",
  "errorSiteNameRequired": "Die Grabstätte darf nicht leer sein.",
  "errorPositionRequired": "Bitte wählen Sie eine Grabposition.",
  "errorGraveNumberInUse2": "Die Grabnummer „{0}“ ist ab {1} bereits vergeben.",
  "errorGraveNumberInUse3": "Die Grabnummer „{0}“ ist von {1} bis {2} bereits vergeben.",
  "errorInvalidDate": "Bitte geben Sie den Beginn als Monat und Jahr an.",
  "errorDateRangeInvalid": "Das Ende darf nicht vor dem Beginn liegen.",
```

- [ ] **Step 7: Check**

Run: `cd frontend && npm run typecheck && npm run lint && npm run build`
Expected: all three clean.

Then click through against the E2E stack (never 8080): `cd frontend && VITE_PROXY_TARGET=http://localhost:8081 npm run dev -- --port 5173`, create a grave with all fields and no end, reopen it, save a second grave with the same number and see the toast, check the list columns at md/lg/xl widths.

- [ ] **Step 8: Commit**

```bash
git add frontend
git commit -m "feat(frontend): edit grave site fields and month terms"
```

---

### Task 8: E2E specs

**Files:**

- Modify: `e2e/seed.ts` (`SeedGraveInput`, `seedGrave`)
- Modify: `e2e/tests/graves.spec.ts` (`startNewGrave`, the create test, a new overlap test, a new open-end test)
- Modify: `e2e/tests/layout.spec.ts` (graves page `secondary`, and the editor flow if it needs the new required fields)
- Modify: `e2e/tests/import-export.spec.ts` (`GRAVES_HEADER` and rows)

**Interfaces:**

- Consumes: Task 7's labels/ids, Task 2's HTTP contract, Task 6's CSV header.

- [ ] **Step 1: Seed with the new fields**

`e2e/seed.ts`:

```ts
export interface SeedGraveInput {
  graveNumber: string;
  fieldNumber?: string;
  siteName?: string;
  position?: string;
  /** "yyyy-MM" */
  startDate?: string;
  /** "yyyy-MM", or null for no end */
  endDate?: string | null;
  items: SeedGraveLine[];
  recipientIds: number[];
}
```

and in `seedGrave`'s body:

```ts
    graveNumber: grave.graveNumber,
    fieldNumber: grave.fieldNumber ?? "1",
    siteName: grave.siteName ?? `Grabstätte ${grave.graveNumber}`,
    position: grave.position ?? "SingleInside",
    startDate: grave.startDate ?? "2026-01",
    endDate: grave.endDate === undefined ? "2040-12" : grave.endDate,
```

- [ ] **Step 2: Fill the new fields in the editor**

`graves.spec.ts`, `startNewGrave`:

```ts
/** Opens the grave editor for a new grave and fills in its number, site and term. */
async function startNewGrave(
  page: Page,
  graveNumber: string,
  { end = "12/2040" }: { end?: string | null } = {},
): Promise<Locator> {
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await editor.getByLabel("Grabnummer").fill(graveNumber);
  await editor.getByLabel("Feld-Nr.").fill("27");
  await editor.getByLabel("Grabstätte").fill(`Schineller Bäcke ${graveNumber}`);
  await pickOption(page, page.locator("#gravePosition"), "Doppelgrab außen");
  await fillDate(page, editor.getByLabel("Beginn"), "01/2026");
  if (end) {
    await fillDate(page, editor.getByLabel("Ende"), end);
  }
  return editor;
}
```

Import `pickOption` from `../vue` if the spec does not yet. In the create test, the OData check becomes:

```ts
const graves = await (
  await page.request.get(
    `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id,StartDate,EndDate,FieldNumber,SiteName,Position`,
  )
).json();
const grave = graves.value[0];
// the picked months, stored as their first day -- not the month before
expect(grave.StartDate).toBe("2026-01-01");
expect(grave.EndDate).toBe("2040-12-01");
expect(grave.FieldNumber).toBe("27");
expect(grave.SiteName).toBe(`Schineller Bäcke ${graveNumber}`);
expect(grave.Position).toBe("DoubleOutside");
```

and the reopen asserts `await expect(editor.getByLabel("Beginn")).toHaveValue("01/2026");`, `await expect(editor.getByLabel("Feld-Nr.")).toHaveValue("27");`.

- [ ] **Step 3: Add the new behaviours**

Append to `graves.spec.ts` (reuse the create test's item/recipient setup pattern; `seedGrave` needs an item id and a recipient id):

```ts
test("a grave number cannot be given to two graves at the same time", async ({ page }) => {
  const itemNumber = `OV-${RUN}`;
  const recipientName = `Olga Overlap ${RUN}`;
  const graveNumber = `OV-${RUN}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Weg 1",
  });
  await seedGrave(page, {
    graveNumber,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber, { end: null });
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  await addRecipient(page, editor, recipientName);
  await editor.getByRole("button", { name: "Speichern" }).click();

  await expect(
    page.getByText(`Die Grabnummer „${graveNumber}“ ist von 01/2026 bis 12/2040 bereits vergeben.`),
  ).toBeVisible();
  await expect(editor).toBeVisible();
});

test("a grave can be saved without an end", async ({ page }) => {
  const itemNumber = `OE-${RUN}`;
  const recipientName = `Otto Openend ${RUN}`;
  const graveNumber = `OE-${RUN}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Weg 2" });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber, { end: null });
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  await addRecipient(page, editor, recipientName);
  await saveEditor(editor);

  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=StartDate,EndDate`,
    )
  ).json();
  expect(graves.value[0].StartDate).toBe("2026-01-01");
  expect(graves.value[0].EndDate).toBeNull();
});
```

Check the names `createRow` returns (`Id`) and `seedGrave`'s line shape (`SeedGraveLine`) against `e2e/seed.ts`, and use the save button's real accessible name.

- [ ] **Step 4: Layout and import specs**

`layout.spec.ts`, the graves entry:

```ts
    secondary: { Grabstätte: "md", "Feld-Nr.": "lg", Beginn: "xl", Ende: "xl", Zahlung: "sm" },
```

If its editor flow saves a grave, give it the new fields the way `startNewGrave` does.

`import-export.spec.ts`:

```ts
const GRAVES_HEADER = "GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate\r\n";
```

and each grave row becomes `` `${number},27,Grabstätte Import,DoubleOutside,2026-01,2040-12\r\n` ``.

- [ ] **Step 5: Run the suite on its own stack**

Run: `cd e2e && npm test`
Expected: all green on desktop, phone and tablet. A red spec that passes when run alone (`npx playwright test tests/<file>`) is cross-spec interference (memory: E2E specs share one stack); check the base commit before blaming this change.

- [ ] **Step 6: Commit**

```bash
git add e2e
git commit -m "test(e2e): cover grave site fields, month terms and reused grave numbers"
```

---

### Task 9: Requirements text and final verification

**Files:**

- Modify: `docs/spec.md` (FR-018, the grave rules, the invoice document description)

- [ ] **Step 1: Update the requirements**

In `docs/spec.md`:

- FR-018 becomes: "A grave MUST have a grave number, a field number, a site name, a position (double, single or urn grave, inside or outside) and a start month; the end month is optional."
- Add after it: "**FR-018a**: Two graves MUST NOT share a grave number while their terms overlap; a term runs from its start month through its end month, or indefinitely without one. Field numbers may repeat."
- Wherever the invoice document's content is described, describe the head (number, customer number, grave number on grave invoices, date, page), the grave site lines, the table columns (item number, description, count, unit price, total, VAT rate with "Incl." for VAT-included lines, which print gross) and the VAT block (per rate net and VAT; net, VAT, gross; the share line on split grave invoices).
- The edge case at "a grave's end date lies before the selected half-year" stays true: the term still does not affect generation.

- [ ] **Step 2: Run everything**

```bash
scripts/dotnet.sh test Invoicing.sln
(cd frontend && npm run typecheck && npm run lint && npm run build)
(cd e2e && npm test)
npm run format:check
```

Expected: all green.

- [ ] **Step 3: Commit**

```bash
git add docs/spec.md
git commit -m "docs: state the grave site fields, month terms and the invoice layout in the spec"
```

- [ ] **Step 4: Tell the owner about their dev database**

Do not run anything against `data/`. Report: if the dev stack ran `main` after the German-only change, its database applied `RemoveDocumentLanguage`; before starting the new backend, run `docker compose stop backend && scripts/patch-dev-db.sh` (it takes a backup first), then `docker compose start backend`.
