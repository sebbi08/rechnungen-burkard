# Design: A Vue Frontend for Evaluation

**Created**: 2026-09-25

**Status**: Draft — awaiting review

**Input**: Create a second frontend in Vue with a more modern design, using a
component library and a typed OData client. It must have the same features as
the current one. It does not replace the OpenUI5 frontend yet; the two coexist.
No automated tests for it yet.

## Intent

The Vue frontend exists to evaluate whether it should **replace** the OpenUI5
frontend. It is therefore built to parity — a fair comparison needs every
feature — but it is not shipped: it runs in development only, next to the UI5
app, against the same backend. The backend and the release image do not change.

Success means: every flow the UI5 app offers can be done in the Vue app against
the dev backend, with the same results in the database and the same PDFs, in a
UI that looks and feels current on desktop and phone.

## Decisions

| Topic         | Decision                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------- |
| Where it runs | Dev only: its own `docker-compose.yml` service on port **8082**; not in the release image   |
| Component lib | PrimeVue 4, Aura preset, primeicons; Tailwind v4 + `tailwindcss-primeui` for layout only    |
| Dark mode     | Yes — follows the system, a toggle overrides it and is remembered in `localStorage`         |
| OData client  | odata2ts, generated from a committed `$metadata` snapshot                                   |
| Non-EDM calls | Hand-typed module (`api/endpoints.ts`) for the five actions and the D-001 endpoints         |
| Server state  | TanStack Vue Query; no Pinia                                                                |
| i18n          | vue-i18n, `de` default and fallback, `en` for English browsers — the same rule as UI5 today |
| Tests         | None. Typecheck, lint and build in CI; a manual click-through against the E2E stack         |
| Constitution  | Recorded as deviation **D-003** (Principle VI); the shipped UI stays OpenUI5                |

## Architecture

### Location and stack

A new top-level directory `frontend-vue/`, independent of `frontend/`. Nothing is
shared at runtime or build time.

- Vite, Vue 3 (`<script setup lang="ts">`), vue-router (HTML5 history mode;
  Vite's dev server falls back to `index.html`).
- PrimeVue 4 (styled mode, Aura preset), primeicons.
- Tailwind v4 with `tailwindcss-primeui`, used for layout (grid, flex, spacing)
  only. Component styling comes from the PrimeVue theme.
- vue-i18n, `@tanstack/vue-query`, `@odata2ts/odata-service`,
  `@odata2ts/http-client-fetch`; `@odata2ts/odata2ts` as a dev dependency.

### Source layout

```
frontend-vue/
  index.html
  vite.config.ts            # /odata proxy to VITE_PROXY_TARGET (default http://backend:8080)
  odata2ts.config.ts
  package.json              # dev, build, typecheck, lint, gen:odata
  src/
    main.ts                 # app, router, PrimeVue, i18n, Vue Query
    App.vue                 # AppShell + RouterView + Toast + ConfirmDialog + BlockingProgress
    router.ts
    api/
      metadata.xml          # committed snapshot of GET /odata/$metadata
      generated/            # odata2ts output, committed, excluded from lint/format
      odata.ts              # the generated service instance on the fetch client
      http.ts               # fetch wrapper; {error:{code}} → ApiError(code)
      endpoints.ts          # actions + D-001 endpoints, hand-typed
    lib/                    # pure logic, ported verbatim from frontend/src/model/api.ts
      money.ts              # grossFromNet, grossPerLine, priceFieldValue
      halfYear.ts           # currentHalfYear, halfYearOptions, yearOptions, halfYearText
      format.ts             # amount, date, VAT rate, invoice number, revision badge
      download.ts           # downloadBlob, openDocument
    composables/            # useErrorToast, useBlocking, useJobPolling, useTheme, …
    components/             # AppShell, LineEditor, RecipientPicker, RecipientQuickCreate,
                            # PaidStatusTag, PaidToggle, RevisionPanel, BlockingProgress
    pages/                  # one per route, see below
    i18n/de.json, en.json
```

### The OData client

`npm run gen:odata` fetches `$metadata` from the running backend (through the
same proxy target) into `src/api/metadata.xml` and runs odata2ts into
`src/api/generated/`. Both are committed, so `npm install && npm run build`
never needs a backend. Re-run it whenever the EDM changes.

The generated client covers every read and the plain entity writes:
`VatRates` (POST, DELETE), `InvoiceItems` and `Recipients` (POST, PATCH),
`Settings` (GET, PATCH), and the reads of `Invoices`, `Graves` lists,
`GraveInvoiceRuns`, `Jobs`. `$filter`, `$orderby`, `$select`, `$expand`,
`$top`, `$skip` and `$count` are built with its typed query builder.

Everything the EDM does not describe goes through `endpoints.ts`, with request
and response types written by hand from the backend DTOs:

- actions without EDM parameters: `CreateInvoice`, `CreateRevision`,
  `CreateGraveInvoiceRevision`, `SetInvoicePaid`, `GenerateGraveInvoices`;
- D-001 endpoints: `SaveGrave`, `SearchGraves`, `GraveInvoicingStatus`,
  `GraveInvoiceGroup`, `ItemGraves`, `ValidateGraveBilling`,
  `BulkDownloadDocuments`, `ImportTemplate`, `ImportArchive`;
- `Graves(id)`, whose response is hand-built JSON, and
  `Invoices(id)/Lines`, `Invoices(id)/Documents`, `Documents(id)/$value`.

`Edm.Decimal` arrives as a string (as the UI5 code already handles); the
endpoint layer converts decimals to `number` at the boundary so pages never see
strings.

### Server state

Every read is a Vue Query query keyed by entity set and parameters. Every write
is a mutation that invalidates the affected keys on success — this replaces the
UI5 app's manual `refresh()` calls. Queries do not refetch on window focus
(the data only changes through this app or the other frontend, and a surprise
refetch mid-edit is worse than a stale list).

### Running it

A new service in `docker-compose.yml`:

```yaml
frontend-vue:
  image: node:22
  user: "${UID:-1000}:${GID:-1000}"
  working_dir: /app/frontend-vue
  volumes:
    - ./frontend-vue:/app/frontend-vue
    - frontend-vue-node-modules:/app/frontend-vue/node_modules
  command: ["sh", "-c", "npm install && npm run dev -- --host --port 8082"]
  ports:
    - "8082:8082"
```

Both frontends talk to the one dev backend. For the manual click-through the
Vite dev server is started a second time on the host with
`VITE_PROXY_TARGET=http://localhost:8081`, pointing at the E2E stack — never at
the dev database (see `CLAUDE.md`).

### Checks

- `npm run typecheck` (vue-tsc), `npm run lint` (ESLint flat config,
  typescript-eslint type-aware rules, eslint-plugin-vue), `npm run build`.
- Prettier already walks new directories; `.prettierignore` gains
  `frontend-vue/src/api/generated/` and `frontend-vue/src/api/metadata.xml`.
  `lint-staged` gains `*.vue`.
- CI gains one job, `frontend-vue`, that runs `npm ci`, typecheck, lint and
  build. No tests.

## Look and feel

### Shell

- **Desktop**: a persistent left sidebar (icon + label): Rechnungen, Gräber,
  Leistungen, Empfänger, Import/Export, Einstellungen. A slim top bar carries the
  page title, breadcrumbs on detail pages (Gräber › 12-A › R-2026-0042) and the
  light/dark toggle.
- **Phone** (below Tailwind's `md`): the sidebar becomes a Drawer opened from a
  hamburger button in the top bar.
- `/` redirects to `/invoices`. There is no tile page.

### Lists

PrimeVue DataTable in a card surface. Toolbar above it: search and filters on
the left, the primary action on the right. On phones rows stack
(`responsiveLayout="stack"`). Row actions are an icon-button group with
tooltips and `aria-label`s; below `md`, all but the most frequent go into a
"⋯" Menu. Amounts are right-aligned with `tabular-nums`. Status is a Tag:
green paid / created, amber unpaid / partly paid, red not yet invoiced.

### Editing

- Small entities — item, recipient, generate, quick-create recipient — open in a
  centred Dialog, full screen on phones.
- Large editors — invoice, grave, grave invoice — open in a right-side Drawer
  (full width on phones) so the list stays visible behind them.
- `LineEditor` is one component for all three: item picker (AutoComplete over
  items, showing number and description), count, price (placeholder shows the
  default price), "inkl. MwSt." checkbox, delete per row, "Zeile hinzufügen",
  and a running net / gross footer.

### Detail pages

A header card (number, recipient, address, status tags, actions), the revisions
in an Accordion with the newest open, and a right-aligned totals block per
revision. Grave detail: summary card, then Leistungen, Empfänger and Rechnungen
sections.

### Feedback

- Errors and client validation: a Toast, via `useErrorToast`.
- Blocking explanations — prices that do not divide, a sibling already paid, an
  import rejected: a modal Dialog with the full text.
- Confirmations — mark unpaid, delete a VAT rate: ConfirmDialog.
- Long-running tasks (Principle VIII) — invoice create/revise, grave-invoice
  revision, generation, import: `BlockingProgress`, a full-screen BlockUI with a
  ProgressBar; indeterminate, except generation, which shows the job percent.
- Numbers and dates are always formatted de-DE, as today.

## Behaviour

### Parity

Every route of the UI5 app exists with the same path:

| Path                                | Page                 |
| ----------------------------------- | -------------------- |
| `/settings`                         | Settings + VAT rates |
| `/items`                            | Leistungen           |
| `/recipients`                       | Empfänger            |
| `/invoices`                         | Rechnungen           |
| `/invoices/:rootId`                 | Invoice detail       |
| `/graves`                           | Gräber               |
| `/graves/:graveId`                  | Grave detail         |
| `/graves/:graveId/invoices/:rootId` | Grave invoice detail |
| `/import-export`                    | Import/Export        |

Each page reproduces the behaviour of its UI5 counterpart — the same endpoints,
payloads, filters, ordering, validation and messages — as inventoried from
`frontend/src`. In particular:

- **Settings**: stamp word, ending text, document language; VAT rate list with
  add (client check: rate > 0) and delete.
- **Items**: search on number or description; create/edit dialog; VAT rate
  read-only once linked; the "graves with this item" groups from `ItemGraves`,
  each grave number a link to its detail page.
- **Recipients**: search on name; create/edit; empty number means server-assigned.
- **Invoices**: non-grave, latest revisions only; `search=` custom query option;
  payment filter; row actions details, PDF, copy, edit (disabled when paid),
  paid toggle (confirm before unmarking).
- **Invoice editor**: recipient picker defaulting to the newest recipient, with
  quick-create; lines; `ItemRequired` check; `CreateInvoice` / `CreateRevision`.
  A revision loads the existing lines and shows prices through
  `priceFieldValue`.
- **Invoice detail**: revision chain by `RootId` with the revision-id fallback;
  per-line net and `grossPerLine` gross; backend totals.
- **Graves**: `GraveInvoicingStatus` for the selected half-year; `SearchGraves`
  narrowing; client-side "unpaid only"; ZIP download; generation with job
  polling (1 s interval, 10 min timeout) and the skipped-graves summary; grave
  editor with the `ValidateGraveBilling` pre-check and its per-rate message,
  then `SaveGrave`; recipients fixed when editing.
- **Grave detail**: summary with current half-year status; lines from
  `GraveInvoiceGroup` when invoiced, else from the configuration; recipients;
  the grave's invoices with the same row actions.
- **Grave invoice detail**: share, half-year, grave number; other recipients
  as links; revisions with `RecipientCount` as the divisor. The grave-invoice
  editor refuses to open when a sibling is paid (naming them) and saves via
  `CreateGraveInvoiceRevision`.
- **Import/Export**: template download; ZIP upload; summary on success; the
  problem table (file, row, message) on rejection.

`lib/` is ported line for line; its numbers must match the UI5 app and the
backend to the cent.

### Where it deliberately differs from UI5

Small fixes, since the point is to evaluate a better UI:

- The invoice editor is titled "Rechnung bearbeiten" when revising.
- The invoice editor can delete a line, like the grave editors.
- The Items table formats the VAT rate and the default price.
- Deleting a VAT rate asks for confirmation.
- The generate dialog defaults to the half-year selected in the Graves toolbar.
- The invoice list pages server-side (50 per page, `$top`/`$skip`/`$count`)
  instead of "grow more".
- `/` redirects to the invoice list instead of a tile page.

Kept as they are: the grave-invoice revision does not run the billing pre-check
(the backend validates regardless); back from a detail page goes to its parent
route.

### Errors

`http.ts` throws `ApiError(code, status)` for any non-2xx response, reading
`error.code` from the body when present. `useErrorToast` maps the code to
`errors.<Code>` in the i18n bundle — the same codes as `CODE_TO_I18N` today —
and falls back to "Fehler (<code> / HTTP n)". Client-side validation throws the
same `ApiError` with its own code (`ItemRequired`, `InvalidVatRate`,
`ArchiveRequired`, `GraveRecipientLinkExists`). `ImportArchive`'s 400 carries a
`problems` list and surfaces as the problem table, not a toast.

Mutations disable their trigger while pending; the long ones also block the page.

### Opening documents

As today: open a blank tab synchronously (so the browser does not block the
popup), look up the invoice's documents, then point the tab at
`/odata/Documents(id)/$value`; close it on error.

## Governance

`docs/deviations.md` gains **D-003: A second frontend in Vue, for evaluation
only** — Principle VI. It is not part of the release image, which keeps serving
the OpenUI5 frontend; it exists to decide whether to replace UI5, and that
decision, if taken, goes through a constitution amendment. It consumes the same
documented OData interface and follows Principles VII (i18n) and VIII (blocking
progress).

`CLAUDE.md` gains the `frontend-vue` entry under "Running the parts", including
the `gen:odata` step and the rule that ad-hoc probing points
`VITE_PROXY_TARGET` at the E2E stack.

## Out of scope

- Automated tests (unit or E2E) for the Vue frontend.
- Shipping it in the release image; any change to the Dockerfile or backend.
- An in-app UI language switch (the browser decides, as today).
- A dashboard or any feature the UI5 app does not have.

## Verification

- `npm run typecheck`, `npm run lint`, `npm run build` clean in `frontend-vue/`.
- A manual click-through of every page and flow against the E2E stack, on
  desktop and phone widths, in light and dark mode, comparing results with the
  UI5 app on the same data.
