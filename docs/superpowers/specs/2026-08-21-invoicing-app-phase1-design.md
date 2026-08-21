# Design: Invoicing App — Phase 1 (Core Invoicing)

**Date**: 2026-08-21
**Status**: Approved design (pre-implementation)
**Feature Branch**: `001-invoice-generation`
**Scope**: Stories 1–9 of `spec.md`. Stories 10–13 (import/export) are Phase 2 and
get their own spec → plan → implementation cycle.

## 1. Recorded Decisions

| Topic | Decision |
|---|---|
| Phasing | Phase 1 = Stories 1–9; Phase 2 = Stories 10–13 |
| Document engine | QuestPDF (deterministic in-memory layout, per-page overlays) |
| UI languages | German only for now; i18n resource-bundle mechanism in place |
| Backend runtime | ASP.NET Core on .NET 10 LTS, OData v4 via ASP.NET Core OData 8.x |
| Frontend | OpenUI5 with TypeScript (UI5 tooling TS template), OData V4 model |
| Background jobs | In-process job runner: SQLite `Jobs` table + `BackgroundService` worker |
| Invoice numbering | Year + sequence + revision suffix: `2026-0001`, revisions `2026-0001-R1`, `-R2`, … |
| Deployment model | One combined container image (see deviation D-1) |

### D-1: Deviation from Principle I (recorded justification)

Principle I requires each side to be independently deployable and containerized.
We ship **one combined image**: the backend serves the built frontend from its
`wwwroot`. Justification: a small, single-instance deployment values operational
simplicity; the product owner accepted backend redeploys on UI changes. The
frontend remains a standalone buildable project, so splitting into a second
image later is possible without restructuring. Recorded per the constitution's
governance section.

## 2. Repository Layout & Deployment Topology

```
rechnungen-burkard/
├── constitution.md, spec.md
├── backend/            # ASP.NET Core 10: OData v4 API, EF Core + SQLite,
│                       # QuestPDF document service, job runner
├── frontend/           # OpenUI5 TypeScript app (own package.json, ui5.yaml)
├── docker-compose.yml
├── .github/workflows/  # CI + release
└── docs/superpowers/specs/
```

- **Development** (in Docker, Principle V): compose runs two services —
  `backend` (`dotnet watch`, SQLite on a named volume) and `frontend` (UI5
  tooling dev server with live reload, proxying `/odata` to the backend).
  Migrations run automatically at startup.
- **Release**: multi-stage Dockerfile — frontend built with UI5 tooling, output
  copied into the backend image's `wwwroot`. ASP.NET Core serves the UI at `/`
  and the OData v4 API at `/odata`.
- SQLite file on a Docker volume.

## 3. Data Model (SQLite via EF Core)

All monetary values stored as `decimal` (EF Core → SQLite `TEXT`, invariant
culture). All rounding math done in integer cents; per-VAT-rate totals rounded
half-up once.

| Entity | Key fields |
|---|---|
| `VatRate` | `Rate` (e.g. 7.0, 19.0) — the globally selectable list (Story 1) |
| `GlobalSettings` | single row: `EndingText` (multi-line, preserved verbatim), `StampWord` (default "Kopie", never empty) |
| `InvoiceItem` | `Number` (unique), `Description`, `VatRate`, `DefaultPrice?` (nullable — Story 2) |
| `Recipient` | `Name`, `Address` (multi-line) |
| `Grave` | `GraveNumber`, `StartDate`, `EndDate`, `CreatedAt`; children `GraveItem` (ItemId, `Count`), `GraveRecipient` (RecipientId) |
| `Invoice` | `RootId`, `RevisionNumber` (0 = original), `Year` (invoice-number year = year of generation, incl. historical half-year generations), `Sequence`, computed `InvoiceNumber`, `GraveId?`, `PeriodYear?` + `PeriodHalf?` (invoiced half-year period, e.g. H1 2025, only for grave invoices), `SharePercent?`, snapshots: `RecipientName`, `RecipientAddress`, `EndingText`, `StampWord`, `GeneratedAt` |
| `InvoiceLine` | `ItemId?`, `ItemNumber`, `Description`, `Count`, `UnitPrice` (required even if item has no default price), `VatRate` (from item), line total = count × unit price |
| `Document` | `InvoiceId`, `Kind` (Normal \| Copy), `Content` (PDF BLOB), `FileName`, `GeneratedAt` |
| `GraveInvoiceRun` | `GraveId`, `Year`, `Half` (1 \| 2), `JobId` — **unique (GraveId, Year, Half)** → duplicate prevention at DB level |
| `Job` | `Type`, `Status` (Queued \| Running \| Succeeded \| Failed), `ProgressPercent`, `Total`, `Processed`, `Result` (JSON), timestamps |

**Semantics:**

- **Revisions**: editing an invoice inserts a new `Invoice` row with the same
  `RootId` and `RevisionNumber + 1`; the old row and its documents remain
  untouched (Story 4.5, edge cases).
- **Snapshots** make historical invoices immutable (Stories 1.5, 3.3, 7.3) and
  documents reproducible from persisted data (Principle II).
- **Invoice-number year**: always the generation year (including invoices
  generated later for a historical half-year); the invoiced period is stored
  separately in `PeriodYear`/`PeriodHalf` (edge case: grave end date before
  the selected half-year).
- **Splitting** (Story 6): total computed in cents, split `n` ways with the
  remainder distributed one cent at a time in recipient order — the sum of
  shares always equals the total.
- **Atomic pairs**: normal + copy generated and persisted in one transaction;
  a failure on either persists neither.
- **Jobs** persist across restarts; on startup, `Running` rows are reset to
  `Queued` and requeued.
- **Migrations**: exactly one EF Core migration per app version (Principle IV).

## 4. Backend

### 4.1 OData v4 surface (`/odata`)

- Entity sets: `VatRates`, `GlobalSettings` (singleton), `InvoiceItems`,
  `Recipients`, `Graves` (with `items`/`recipients` navigation), `Invoices`
  (with `lines`/`documents`), `Documents`, `Jobs`.
- Actions (server owns number allocation, snapshots, document generation):
  - `CreateInvoice(recipientId, lines[{itemId, count, unitPrice}])` → assigns
    number, snapshots everything, generates normal + copy atomically (Story 4).
  - `CreateRevision(invoiceId, lines)` → inserts revision row, regenerates both
    documents; original untouched.
  - `GenerateGraveInvoices(halfYear)` → enqueues batch job; returns the `Job`.
  - `BulkDownloadDocuments(kind, halfYear?)` → ZIP stream over the invoices matching the current filter (all invoices when no half-year is given); `kind` selects exclusively normal documents or copies (Stories 8/9).
- Function: `GraveInvoicingStatus(halfYear)` → per-grave invoiced/not-invoiced
  status (Stories 6.4/6.5, 9).
- Single-document download as OData media stream:
  `GET /odata/Documents({id})/$value`.
- Plain CRUD on items, recipients, graves, VAT rates, settings.

### 4.2 Internal layout (`backend/`)

- `Domain/` — entities + pure services: `ShareCalculator` (integer-cent split
  with remainder distribution), `InvoiceNumberService` (per-year sequence,
  transaction-locked), `DocumentService` (QuestPDF composers:
  `InvoiceComposer`, `CopyStampOverlay` — rotated stamp word on **every** page).
- `OData/` — EDM model + controllers; validation in action handlers.
- `Jobs/` — `JobQueue` (Channel) + `JobRunner` (`BackgroundService`): enqueue
  inserts a `Job` row; worker updates progress (throttled writes). Duplicate
  protection: a same-type active job yields HTTP 409.
- `Persistence/` — `AppDbContext`, EF Core migrations (one per version).

### 4.3 Cross-cutting

- **Deterministic PDFs**: fixed document metadata (creation date = invoice
  `GeneratedAt`, no dynamic timestamps) so regeneration from persisted data
  yields identical content (Principle II).
- **Error reporting**: machine-readable error codes + detail from the backend;
  the frontend maps codes to German i18n texts (Principle VII).
- Validation rules: recipient address required; count > 0; unit price required
  when the item has no default price; stamp word non-empty on settings save;
  VAT rates validated against the configured list.

## 5. Frontend (OpenUI5 TypeScript)

App shell (toolpage + side navigation) with router; a single OData V4 model
bound to `/odata`.

| View | Purpose |
|---|---|
| Settings | VAT rate list, multi-line ending text, stamp word (non-empty validation) — Story 1 |
| Items | master-detail catalogue, search by number — Story 2 |
| Recipients | master-detail, search — Story 3 |
| Graves | list + detail (dates, items with counts, recipients); half-year selector; invoicing status via `GraveInvoicingStatus`; not-yet-invoiced first for the current half-year — Stories 5, 6.4/6.5, 9 |
| Invoices | list with half-year filter; detail with lines, totals per VAT rate, both documents (normal vs. copy distinguishable), revision history — Stories 4, 8, 9 |
| New Invoice dialog | recipient picker (create on the fly — Story 3.2); lines by item number, count, price (pre-filled default, required otherwise); live totals; save → `CreateInvoice` — Story 4 |
| Edit Invoice dialog | edits → `CreateRevision`; revision number shown prominently |
| Generate Grave Invoices dialog | half-year selection → `GenerateGraveInvoices` — Story 6 |

**Blocking progress (Story 13):** non-closable blocking dialog with
`ProgressIndicator`, polling `GET /odata/Jobs({id})` at ~1 s. Trigger actions
disabled while a same-type job is active (client + server double-start
protection). On completion, dialog closes and the result is shown.

**Downloads:** single documents via media-stream URL; bulk ZIP via
`BulkDownloadDocuments` download link — normal and copy always separate
(Story 8).

**i18n:** all UI strings in resource bundles (`i18n.properties` = German for
now); mechanism ready for additional languages.

## 6. Testing Strategy (Principle III)

Integration and E2E over unit tests; unit tests only for pure business logic
where they demonstrably reduce risk.

- **Backend integration tests** (xUnit + `WebApplicationFactory`, SQLite):
  OData CRUD flows; `CreateInvoice` (numbering, snapshots, atomic pair,
  validation failures); `CreateRevision` (original untouched, `-R1`);
  `GenerateGraveInvoices` (full job lifecycle, remainder distribution, sum of
  shares equals total, duplicate prevention, 409 on concurrent same-type job);
  settings validation.
- **Document tests** (PDF text extraction, e.g. PdfPig): invoice content
  (lines, counts, line totals, VAT groups, ending text line breaks preserved);
  copy has diagonal stamp word on every page of a multi-page invoice, normal
  has none; determinism: regenerate → byte-identical PDF.
- **E2E tests** (Playwright against docker-compose) — primary tier: full user
  journeys for Stories 1–9, including blocking progress behavior and duplicate
  submission attempts.
- **Unit tests** — only `ShareCalculator`, `InvoiceNumberService`, rounding
  helpers.

## 7. Development & Release Workflow

- **Local dev** (Docker, Principle V): compose with `backend` (`dotnet watch`)
  and `frontend` (UI5 dev server proxying `/odata`); SQLite on a named volume;
  migrations auto-applied at startup.
- **CI** (PRs): frontend build + type check; backend build + integration and
  document tests; combined image build to verify assembly. E2E on demand.
- **Release**: manually triggered workflow with semver choice
  (patch/minor/major); next version computed from latest git tag; creates git
  tag + GitHub release; pushes combined image to GHCR tagged with the version
  and `latest`. Version injected at build time from the git tag.
- **Migrations**: exactly one EF Core migration per released version
  (Principle IV).

## 8. Out of Scope (Phase 2)

Stories 10–13: import template download, archive validation, consistent import,
and the import job type. The `Job` model and blocking-progress frontend
mechanism are designed now so Phase 2 reuses them.
