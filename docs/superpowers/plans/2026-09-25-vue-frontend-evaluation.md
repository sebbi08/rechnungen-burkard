# Vue Evaluation Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A second, dev-only frontend in `frontend-vue/` (Vue 3 + PrimeVue 4) with feature parity to the OpenUI5 app, talking to the unchanged backend through a generated, typed OData client.

**Architecture:** Vite SPA. Reads and plain entity writes go through an odata2ts client generated from a committed `$metadata` snapshot; the five EDM actions and the D-001 endpoints go through a hand-typed `api/endpoints.ts` on a small fetch wrapper. TanStack Vue Query holds server state; every successful write invalidates all queries. Pure calculations (`grossPerLine`, price conversion, half-years) are ported verbatim from `frontend/src/model/api.ts`.

**Tech Stack:** Vue 3.5, vue-router 4, Vite 8, TypeScript 5.9, PrimeVue 4.5 (Aura preset, styled mode), primeicons 7, Tailwind CSS 4 + tailwindcss-primeui, vue-i18n 11, @tanstack/vue-query 5, odata2ts 0.45 (`@odata2ts/odata-service`, `@odata2ts/http-client-fetch`), ESLint 10 + typescript-eslint + eslint-plugin-vue.

**Spec:** `docs/superpowers/specs/2026-09-25-vue-frontend-evaluation-design.md`

## Global Constraints

- Everything new lives in `frontend-vue/`. `frontend/`, `backend/`, `Dockerfile` and `docker-compose.e2e.yml` are not touched.
- Dev only: `docker-compose.yml` service `frontend-vue` on port **8082**; not in the release image.
- **PrimeVue stays on 4.x** (`primevue@~4.5.5`, `@primeuix/themes@^2.0.3`). PrimeVue 5 changed its licence ("SEE LICENSE IN LICENSE.md", with a licence-manager dependency); 4.5 is MIT. Do not upgrade.
- TypeScript `~5.9.3` (typescript-eslint supports `<6.1`). vue-router stays on 4.x.
- No automated tests (spec: out of scope). Every task ends with `npm run typecheck && npm run lint && npm run build` clean, plus the manual check it lists.
- **Never point the dev server at the dev stack.** Manual checks run the Vite dev server on the host with `VITE_PROXY_TARGET=http://localhost:8081` against the E2E stack (`docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build`). The E2E stack's database lives in tmpfs and is disposable; the dev database is the user's (`CLAUDE.md`).
- Do not start the `frontend-vue` compose service during implementation — it also binds 8082.
- User-visible text only through vue-i18n (`de` default and fallback, `en` for English browsers). Numbers and dates always formatted `de-DE`.
- Every long-running task (invoice create/revise, grave-invoice revision, generation, import) runs inside `useBlocking().run(...)` (constitution VIII).
- Page files are named `XxxPage.vue`, components multi-word (eslint `vue/multi-word-component-names`).
- Commit messages: Conventional Commits, type **`chore(vue)`** — this frontend is not released, so its commits must not bump the release version (`scripts/next-version.sh` turns `feat` into a minor). Docs commits use `docs`. No body line may start with `word:` (commitlint footer detection). End every commit message with:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01LeUpTJLALyoUF76nj7PAeA
  ```
- Prettier formats everything outside `backend/` on commit (lint-staged); do not hand-format, and never format `src/api/generated/`.

## Review Focus

No automated tests exist, so each line below is a **manual check** added to the task that owns the code, and repeated in the final click-through (Task 16).

1. **Prices with more than two decimals round-trip unchanged.** A grave-invoice revision reopens lines at up to 3-decimal net prices (see `GraveEdit.controller.ts:393`); the price field must not round them, or saving an untouched line changes the bill. `LineEditor` uses `:max-fraction-digits="4"`. Owner: Task 8 (`LineEditor`); checked in Task 14.
2. **An empty price and a price of 0 are different.** Empty sends `unitPrice: null` (item default is billed); `0` sends `0`. Same for an item's default price. Owner: Task 3 (`toLineInputs`) and Task 6 (item dialog); checked in Tasks 6 and 9.
3. **A picked date is saved as that calendar day.** DatePicker yields local midnight; `toISOString()` would move it to the previous day in Europe/Berlin. Dates are serialised with `toIsoDate` (local fields). Owner: Task 11 (`GraveEditor`); checked in Task 12.
4. **The blocking overlay always closes**, also when the call fails or the job times out, and the error is shown after it closes. `useBlocking().run` closes in `finally`. Owner: Task 4; exercised in Tasks 9, 12, 13, 15.
5. **Changing search or filter on the invoice list returns to page 1**, and the total count follows the search (`search=` with `$count=true`). Owner: Task 9.

---

## File Structure

```
frontend-vue/
  package.json, package-lock.json   scripts: dev, build, typecheck, lint, gen:odata
  index.html
  vite.config.ts                    /odata proxy → VITE_PROXY_TARGET (default http://backend:8080)
  tsconfig.json
  eslint.config.mjs
  scripts/gen-odata.mjs             fetch $metadata → src/api/metadata.xml, run odata2ts
  src/
    main.ts                         plugins: PrimeVue, ToastService, ConfirmationService, tooltip, Vue Query, i18n, router
    main.css                        layer order, Tailwind, tailwindcss-primeui, dark variant, body colours
    App.vue                         AppShell + RouterView + Toast + ConfirmDialog + MessageDialog + BlockingProgress
    router.ts                       routes (one per page)
    api/
      metadata.xml                  committed snapshot (not formatted)
      generated/                    odata2ts output (committed, not linted/formatted)
      odata.ts                      `odata`: the generated InvoicingService on a FetchClient
      http.ts                       ApiError, toApiError, request/getJson/postJson/getBlob/postBlob
      endpoints.ts                  actions + D-001 endpoints + their DTO types, pollJob
      queries.ts                    readAll, item/recipient/VAT/settings queries, loadItemVatRates, loadChain, loadGraveDetail
    lib/
      money.ts                      grossFromNet, grossPerLine, priceFieldValue, lineNet
      halfYear.ts                   currentHalfYear, halfYearOptions, yearOptions, halfYearText, halfYearKey, parseHalfYearKey
      format.ts                     formatDate, formatAmount, formatVatRate, formatInvoiceNumber, formatRevisionBadge,
                                    formatAddressLine, formatShare, toIsoDate, parseIsoDate
      download.ts                   downloadBlob, openDocument
      lines.ts                      DraftLine, newLine, toLineInputs
      revisions.ts                  RevisionView, buildRevisions
      billing.ts                    billingProblemText
      generation.ts                 generationSummary
    composables/
      useTheme.ts  useBlocking.ts  useMessage.ts  useErrorToast.ts  useConfirmAsync.ts
      usePaidToggle.ts  useDebounced.ts  useGraveInvoiceEdit.ts
    components/
      AppShell.vue  NavLinks.vue  PageHeader.vue  BlockingProgress.vue  MessageDialog.vue
      PaidStatusTag.vue  InvoiceRowActions.vue  LineEditor.vue  RecipientPicker.vue
      RecipientQuickCreate.vue  ItemDialog.vue  RecipientDialog.vue  InvoiceEditor.vue
      RevisionPanel.vue  RevisionList.vue  GenerateDialog.vue  GraveEditor.vue  GraveInvoiceEditor.vue
    pages/
      SettingsPage.vue  ItemsPage.vue  RecipientsPage.vue  InvoicesPage.vue  InvoiceDetailPage.vue
      GravesPage.vue  GraveDetailPage.vue  GraveInvoiceDetailPage.vue  ImportExportPage.vue
    i18n/
      index.ts  de.json  en.json  prime.ts
Modified outside frontend-vue/: docker-compose.yml, .prettierignore, lint-staged.config.js,
.github/workflows/ci.yml, docs/deviations.md, CLAUDE.md
```

Behavioural reference for every page is its UI5 counterpart in `frontend/src` (views, fragments, controllers). When this plan and the UI5 app disagree on something the spec does not list as a deliberate difference, the UI5 app wins.

---

### Task 1: Scaffold `frontend-vue/` and wire it into the repo

**Files:**

- Create: `frontend-vue/package.json`, `frontend-vue/index.html`, `frontend-vue/vite.config.ts`, `frontend-vue/tsconfig.json`, `frontend-vue/eslint.config.mjs`, `frontend-vue/src/main.ts`, `frontend-vue/src/App.vue`, `frontend-vue/src/main.css`
- Modify: `docker-compose.yml`, `.prettierignore`, `lint-staged.config.js`, `.github/workflows/ci.yml`

**Interfaces:**

- Produces: npm scripts `dev`, `build`, `typecheck`, `lint`, `gen:odata`; path alias `@/` → `src/`; CSS layer order `theme, base, primevue, components, utilities`; dark mode class `.app-dark` on `<html>`.

- [ ] **Step 1: Create `frontend-vue/package.json`**

```json
{
  "name": "burkard-invoicing-vue",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "description": "Evaluation frontend in Vue (deviation D-003). Dev only; the release image ships the OpenUI5 frontend.",
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "typecheck": "vue-tsc --noEmit",
    "lint": "eslint src",
    "gen:odata": "node scripts/gen-odata.mjs"
  }
}
```

- [ ] **Step 2: Install dependencies (writes versions and the lock file)**

Run from `frontend-vue/`:

```sh
npm install vue@^3.5 vue-router@^4.6 primevue@~4.5.5 @primeuix/themes@^2.0.3 primeicons@^7.0.0 \
  vue-i18n@^11.4 @tanstack/vue-query@^5.103 \
  @odata2ts/odata-service@~0.28.0 @odata2ts/odata-query-objects@~0.32.0 @odata2ts/odata-core \
  @odata2ts/http-client-api @odata2ts/http-client-fetch
npm install -D vite@^8 @vitejs/plugin-vue@^6 typescript@~5.9.3 vue-tsc@^3.3 @types/node@^22 \
  tailwindcss@^4.3 @tailwindcss/vite@^4.3 tailwindcss-primeui@^0.6 \
  eslint@^10 eslint-plugin-vue@^10 typescript-eslint@^8.70 @vue/eslint-config-typescript@^14.9 globals@^17 \
  @odata2ts/odata2ts@~0.45.0
```

Expected: no `ERESOLVE` errors. Check `npm ls primevue` shows 4.5.x.

- [ ] **Step 3: Create `frontend-vue/index.html`**

```html
<!doctype html>
<html lang="de">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Rechnungen</title>
  </head>
  <body>
    <div id="app"></div>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
```

- [ ] **Step 4: Create `frontend-vue/vite.config.ts`**

```ts
import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

// Where /odata is proxied to. Inside docker compose that is the backend
// container. For a click-through from the host, point it at the E2E stack
// (VITE_PROXY_TARGET=http://localhost:8081) -- never at the dev stack, whose
// database holds the user's data (see CLAUDE.md).
const proxyTarget = process.env.VITE_PROXY_TARGET ?? "http://backend:8080";

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  define: {
    // vue-i18n's compile-time flags; unset they log a warning on every start
    __VUE_I18N_FULL_INSTALL__: true,
    __VUE_I18N_LEGACY_API__: false,
    __INTLIFY_PROD_DEVTOOLS__: false,
  },
  server: {
    port: 8082,
    strictPort: true,
    proxy: {
      "/odata": { target: proxyTarget, changeOrigin: true },
    },
  },
});
```

- [ ] **Step 5: Create `frontend-vue/tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "lib": ["ES2023", "DOM", "DOM.Iterable"],
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "isolatedModules": true,
    "resolveJsonModule": true,
    "skipLibCheck": true,
    "noEmit": true,
    "jsx": "preserve",
    "types": ["vite/client", "node"],
    "paths": { "@/*": ["./src/*"] }
  },
  "include": ["src/**/*.ts", "src/**/*.vue", "vite.config.ts"]
}
```

- [ ] **Step 6: Create `frontend-vue/eslint.config.mjs`**

```js
import { defineConfigWithVueTs, vueTsConfigs } from "@vue/eslint-config-typescript";
import pluginVue from "eslint-plugin-vue";
import globals from "globals";

// "essential" rather than "recommended": the recommended set is mostly
// formatting, which Prettier owns here as everywhere else in the repository.
export default defineConfigWithVueTs(
  { ignores: ["dist/", "src/api/generated/", "scripts/"] },
  pluginVue.configs["flat/essential"],
  vueTsConfigs.recommendedTypeChecked,
  { languageOptions: { globals: globals.browser } },
);
```

- [ ] **Step 7: Create `frontend-vue/src/main.css`**

```css
/* Declared before Tailwind so PrimeVue's layer sits below Tailwind's utilities:
   a utility class on a PrimeVue component (w-full, gap-2 ...) must win. */
@layer theme, base, primevue, components, utilities;
@import "tailwindcss";
@import "tailwindcss-primeui";

/* dark: utilities follow the same class PrimeVue's darkModeSelector uses */
@custom-variant dark (&:where(.app-dark, .app-dark *));

body {
  margin: 0;
  background: var(--p-surface-50);
  color: var(--p-text-color);
  font-family:
    Inter,
    ui-sans-serif,
    system-ui,
    -apple-system,
    "Segoe UI",
    sans-serif;
}

.app-dark body {
  background: var(--p-surface-950);
}
```

- [ ] **Step 8: Create a first `frontend-vue/src/main.ts` and `frontend-vue/src/App.vue`** (Task 4 replaces both)

`src/main.ts`:

```ts
import "primeicons/primeicons.css";
import "./main.css";
import Aura from "@primeuix/themes/aura";
import PrimeVue from "primevue/config";
import { createApp } from "vue";
import App from "./App.vue";

createApp(App)
  .use(PrimeVue, {
    theme: {
      preset: Aura,
      options: {
        darkModeSelector: ".app-dark",
        cssLayer: { name: "primevue", order: "theme, base, primevue" },
      },
    },
  })
  .mount("#app");
```

`src/App.vue`:

```vue
<script setup lang="ts">
import Button from "primevue/button";
</script>

<template>
  <main class="p-6">
    <Button label="PrimeVue" icon="pi pi-check" />
  </main>
</template>
```

- [ ] **Step 9: Verify the scaffold**

Run from `frontend-vue/`: `npm run typecheck && npm run lint && npm run build`
Expected: all three exit 0; `dist/index.html` exists.

Run `npx vite --port 8082` and open `http://localhost:8082`: an Aura-styled button with a check icon. Stop the server.

- [ ] **Step 10: Add the compose service**

In `docker-compose.yml`, add after the `frontend` service:

```yaml
# The Vue evaluation frontend (docs/deviations.md D-003). Dev only: the
# release image ships the OpenUI5 frontend. Proxies /odata to the backend
# container like the UI5 dev server does.
frontend-vue:
  image: node:22
  user: "${UID:-1000}:${GID:-1000}"
  working_dir: /app/frontend-vue
  volumes:
    - ./frontend-vue:/app/frontend-vue
    - frontend-vue-node-modules:/app/frontend-vue/node_modules
  command: ["sh", "-c", "npm install && npm run dev -- --host"]
  ports:
    - "8082:8082"
```

and under `volumes:` add `frontend-vue-node-modules:`.

Run: `docker compose config --quiet` — Expected: exit 0 (validates only; do not `up`).

- [ ] **Step 11: Formatting, lint-staged and CI**

Append to `.prettierignore`:

```
# Generated by odata2ts from the committed $metadata snapshot. Regenerate with
# `npm run gen:odata` in frontend-vue/ rather than formatting.
frontend-vue/src/api/generated/
frontend-vue/src/api/metadata.xml
```

In `lint-staged.config.js` change the Prettier glob to
`"*.{ts,js,mjs,cjs,json,md,yml,yaml,html,xml,vue,css}": "prettier --write",`.

In `.github/workflows/ci.yml`, add after the `frontend` job:

```yaml
# The Vue evaluation frontend (D-003). No tests by decision; this keeps it
# compiling and lint-clean while it is being evaluated.
frontend-vue:
  runs-on: ubuntu-latest
  defaults:
    run:
      working-directory: frontend-vue
  steps:
    - uses: actions/checkout@v7
    - uses: actions/setup-node@v7
      with:
        node-version: 22
    - run: npm ci
    - run: npm run typecheck
    - run: npm run lint
    - run: npm run build
```

Run from the repo root: `npx prettier --check frontend-vue docker-compose.yml .github lint-staged.config.js .prettierignore`
Expected: clean (run `npx prettier --write` on anything listed, then re-check).

- [ ] **Step 12: Commit**

```bash
git add frontend-vue docker-compose.yml .prettierignore lint-staged.config.js .github/workflows/ci.yml
git commit -m "chore(vue): scaffold the Vue evaluation frontend"
```

(Append the two attribution lines from Global Constraints to every commit message.)

---

### Task 2: Typed OData client, HTTP layer and endpoints

**Files:**

- Create: `frontend-vue/scripts/gen-odata.mjs`, `frontend-vue/src/api/metadata.xml` (generated), `frontend-vue/src/api/generated/**` (generated), `frontend-vue/src/api/odata.ts`, `frontend-vue/src/api/http.ts`, `frontend-vue/src/api/endpoints.ts`, `frontend-vue/src/api/queries.ts`

**Interfaces:**

- Produces:
  - `odata: InvoicingService` — `odata.Invoices()`, `odata.InvoiceItems(id)`, `odata.Settings()` …; each `.query(fn)/.create(x)/.patch(x)/.delete()` returns a command, run with `.execute(config?)` → `{ data }`; collections come back as `data.value`, `$count` as `data["@odata.count"]`.
  - Generated types from `@/api/generated`: `Invoice`, `InvoiceLine`, `InvoiceItem`, `Recipient`, `VatRate`, `GlobalSettings`, `GraveInvoiceRun`, `Job`, enum `JobStatus`.
  - `class ApiError extends Error { code: string; status?: number; body?: unknown }`, `toApiError(error: unknown): ApiError`.
  - `request(url, init?)`, `getJson<T>(url)`, `postJson<T = unknown>(url, payload)`, `getBlob(url)`, `postBlob(url, payload)`.
  - Endpoints (below) and `readAll`, `useVatRatesQuery`, `useItemsQuery(term: Ref<string>)`, `useRecipientsQuery(term: Ref<string>)`, `useSettingsQuery`, `loadItemVatRates(queryClient)`, `loadChain(rootId)`.

- [ ] **Step 1: Start the E2E stack** (needed for `$metadata` now and for every manual check later)

Run from the repo root:

```sh
docker compose -f docker-compose.e2e.yml -p invoicing-e2e up -d --build
until curl -fsS http://localhost:8081/healthz; do sleep 2; done
```

Expected: `{"status":"ok"}`. Leave it running for the rest of the plan; `cd e2e && npm run stack:down` stops it at the end.

- [ ] **Step 2: Create `frontend-vue/scripts/gen-odata.mjs`**

```js
// Refreshes the OData client: downloads $metadata into src/api/metadata.xml
// and regenerates src/api/generated/ from it. Both are committed, so a build
// never needs a running backend -- run this only when the EDM changes.
//
// Defaults to the E2E stack. The metadata is the same on every stack, and the
// dev stack is the user's (CLAUDE.md).
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const url = process.env.METADATA_URL ?? "http://localhost:8081/odata/$metadata";
const response = await fetch(url);
if (!response.ok) {
  throw new Error(`GET ${url} answered ${response.status}`);
}
writeFileSync("src/api/metadata.xml", await response.text());

execFileSync(
  "npx",
  [
    "odata2ts",
    "--source",
    "src/api/metadata.xml",
    "--output",
    "src/api/generated",
    "--mode",
    "service",
    "--emit-mode",
    "ts",
    "--service-name",
    "Invoicing",
  ],
  { stdio: "inherit" },
);
```

- [ ] **Step 3: Generate the client**

Run from `frontend-vue/`: `npm run gen:odata`
Expected: "Successfully finished!"; `src/api/generated/InvoicingService.ts` exists and `grep -c "public Invoices" src/api/generated/InvoicingService.ts` prints 3.

- [ ] **Step 4: Create `frontend-vue/src/api/odata.ts`**

```ts
import { FetchClient } from "@odata2ts/http-client-fetch";
import { InvoicingService } from "./generated";

/** The generated OData client for everything the EDM describes. Relative base
 *  path: the dev server proxies /odata to whichever backend it points at. */
export const odata = new InvoicingService(new FetchClient(), "/odata");
```

- [ ] **Step 5: Create `frontend-vue/src/api/http.ts`**

```ts
import { FetchClientError } from "@odata2ts/http-client-fetch";

/** A failed call, reduced to the backend's error code -- `{error:{code}}` --
 *  or `HTTP <status>` when the response carried none. The UI maps the code to
 *  a text (useErrorToast); client-side validation throws the same type with
 *  its own code (ItemRequired, InvalidVatRate, ...). */
export class ApiError extends Error {
  public constructor(
    public readonly code: string,
    public readonly status?: number,
    public readonly body?: unknown,
  ) {
    super(code);
    this.name = "ApiError";
  }
}

interface ErrorBody {
  error?: { code?: unknown };
}

function codeOf(body: unknown, status: number | undefined): string {
  const code = (body as ErrorBody | undefined)?.error?.code;
  return typeof code === "string" && code ? code : `HTTP ${status ?? "?"}`;
}

/** Anything thrown by a call -- our own fetches or the generated client. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  if (error instanceof FetchClientError) {
    return new ApiError(codeOf(error.responseData, error.status), error.status, error.responseData);
  }
  return new ApiError(error instanceof Error ? error.message : String(error));
}

export async function request(url: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(url, init);
  if (!response.ok) {
    const body: unknown = await response.json().catch(() => undefined);
    throw new ApiError(codeOf(body, response.status), response.status, body);
  }
  return response;
}

export async function getJson<T>(url: string): Promise<T> {
  return (await (await request(url)).json()) as T;
}

function jsonInit(payload: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  };
}

/** POSTs JSON. Answers the parsed body, or undefined for an empty one (201/204). */
export async function postJson<T = unknown>(url: string, payload: unknown): Promise<T> {
  const text = await (await request(url, jsonInit(payload))).text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function getBlob(url: string): Promise<Blob> {
  return (await request(url)).blob();
}

export async function postBlob(url: string, payload: unknown): Promise<Blob> {
  return (await request(url, jsonInit(payload))).blob();
}
```

- [ ] **Step 6: Create `frontend-vue/src/api/endpoints.ts`**

Types are copied from `frontend/src/model/api.ts`; payload casing is exactly what the UI5 app sends (ASP.NET binds case-insensitively, but keep it identical).

```ts
/**
 * Everything the EDM does not describe (D-001), and the five actions whose
 * parameters $metadata leaves out. Payloads and response shapes mirror
 * frontend/src/model/api.ts, which the backend was built against.
 */
import { type InvoiceLine, type Job, JobStatus } from "./generated";
import { ApiError, getBlob, getJson, postBlob, postJson, request } from "./http";
import { odata } from "./odata";

// ---- invoices -------------------------------------------------------------

export interface InvoiceLineInput {
  itemId: number;
  count: number;
  unitPrice: number | null;
  priceIncludesVat: boolean;
}

export async function createInvoice(recipientId: number, lines: InvoiceLineInput[]): Promise<void> {
  await postJson("/odata/CreateInvoice", { recipientId, lines });
}

export async function createRevision(
  invoiceId: number,
  recipientId: number,
  lines: InvoiceLineInput[],
): Promise<void> {
  await postJson("/odata/CreateRevision", { invoiceId, recipientId, lines });
}

/** Revises every recipient invoice of the grave and half-year the invoice
 *  belongs to. `lines` are grave-level: the full count at the full price. */
export async function createGraveInvoiceRevision(
  invoiceId: number,
  lines: InvoiceLineInput[],
): Promise<void> {
  await postJson("/odata/CreateGraveInvoiceRevision", { invoiceId, lines });
}

/** Marks an invoice paid -- the backend stamps the day -- or takes the mark back. */
export async function setInvoicePaid(invoiceId: number, paid: boolean): Promise<void> {
  await postJson("/odata/SetInvoicePaid", { invoiceId, paid });
}

export async function fetchInvoiceLines(invoiceId: number): Promise<InvoiceLine[]> {
  return (await getJson<{ value: InvoiceLine[] }>(`/odata/Invoices(${invoiceId})/Lines`)).value;
}

export type DocumentKindName = "Normal" | "Copy";

export interface DocumentRow {
  Id: number;
  FileName: string;
  Kind: DocumentKindName;
}

export async function fetchDocuments(invoiceId: number): Promise<DocumentRow[]> {
  // the PDF bytes are not part of the EDM entity; select keeps the row small
  const url = `/odata/Invoices(${invoiceId})/Documents?$select=Id,FileName,Kind`;
  return (await getJson<{ value: DocumentRow[] }>(url)).value;
}

/** The backend serves the PDF inline, so the browser displays it. */
export function documentUrl(documentId: number): string {
  return `/odata/Documents(${documentId})/$value`;
}

/** The latest-revision Normal PDFs of a half-year's grave invoices, as a ZIP. */
export function bulkDownload(kind: DocumentKindName, year: number, half: number): Promise<Blob> {
  return postBlob("/odata/BulkDownloadDocuments", { kind, year, half });
}

// ---- recipients and items -------------------------------------------------

/** POST through fetch rather than the generated client: an empty number is
 *  left out so the backend assigns one, which the generated create type (Number
 *  required) cannot express. */
export async function createRecipient(input: {
  Number?: string;
  Name: string;
  Address: string;
}): Promise<number> {
  const body = await postJson<{ Id?: number; id?: number } | undefined>("/odata/Recipients", input);
  return Number(body?.Id ?? body?.id);
}

/** The graves billing an item at one price: the link's own, or the item's
 *  default standing in for it. */
export interface ItemGraveGroup {
  UnitPrice: number | null;
  PriceIncludesVat: boolean;
  IsDefault: boolean;
  Graves: { Id: number; GraveNumber: string }[];
}

export interface ItemGraves {
  /** Whether any grave bills the item, which fixes its VAT rate. */
  Linked: boolean;
  DefaultPrice: number | null;
  Groups: ItemGraveGroup[];
}

export function getItemGraves(itemId: number): Promise<ItemGraves> {
  return getJson<ItemGraves>(`/odata/ItemGraves?itemId=${itemId}`);
}

// ---- graves ---------------------------------------------------------------

export interface GraveStatusRow {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  Invoiced: boolean;
  InvoiceCount: number;
  PaidCount: number;
}

export async function fetchGraveStatus(year: number, half: number): Promise<GraveStatusRow[]> {
  const url = `/odata/GraveInvoicingStatus?year=${year}&half=${half}`;
  return (await getJson<{ value: GraveStatusRow[] }>(url)).value;
}

export interface GraveSearchResult {
  Id: number;
  GraveNumber: string;
  RecipientNames: string[];
}

export async function searchGraves(term: string): Promise<GraveSearchResult[]> {
  const url = `/odata/SearchGraves?term=${encodeURIComponent(term)}`;
  return (await getJson<{ value: GraveSearchResult[] }>(url)).value;
}

export interface SaveGraveInput {
  graveId: number | null;
  graveNumber: string;
  startDate: string;
  endDate: string;
  items: InvoiceLineInput[];
  recipientIds: number[];
}

/** The grave and its complete item and recipient links, in one request. */
export async function saveGrave(input: SaveGraveInput): Promise<number> {
  const body = await postJson<{ Id?: number; id?: number } | undefined>("/odata/SaveGrave", input);
  return Number(body?.Id ?? body?.id);
}

export interface GraveItemLink {
  Count: number;
  /** The link's own price, stored as typed; null bills the item's default. */
  UnitPrice: number | null;
  PriceIncludesVat: boolean;
  Item: {
    Id: number;
    Number: string;
    Description: string;
    VatRate: number;
    DefaultPrice: number | null;
  };
}

export interface GraveDetails {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  CreatedAt: string;
  Items: GraveItemLink[];
  Recipients: { Recipient: { Id: number; Name: string; Address: string } }[];
}

/** Graves(id) answers hand-built JSON with the links already expanded. */
export function fetchGraveDetails(graveId: number): Promise<GraveDetails> {
  return getJson<GraveDetails>(
    `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`,
  );
}

export interface GraveInvoiceRecipient {
  Id: number;
  Name: string;
  InvoiceId: number;
  Paid: boolean;
  RootId: number;
}

export interface GraveInvoiceGroupLine {
  ItemId: number;
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
  PriceIncludesVat: boolean;
}

/** The grave, half-year and recipients a grave invoice belongs to, with the
 *  grave's lines as they were invoiced. */
export interface GraveInvoiceGroup {
  GraveId: number;
  GraveNumber: string;
  PeriodYear: number;
  PeriodHalf: number;
  Recipients: GraveInvoiceRecipient[];
  Lines: GraveInvoiceGroupLine[];
}

export function fetchGraveInvoiceGroup(invoiceId: number): Promise<GraveInvoiceGroup> {
  return getJson<GraveInvoiceGroup>(`/odata/GraveInvoiceGroup?invoiceId=${invoiceId}`);
}

export interface GraveBillingProblem {
  VatRate: number;
  Kind: "NotDivisible" | "DoesNotAddUp";
  /** What this rate comes to, in the unit its prices were entered in. */
  Amount: number;
  /** The nearest amounts that would work, in that same unit. */
  Suggestions: number[];
}

export interface GraveBillingCheck {
  Ok: boolean;
  GrossTotal: number | null;
  Problems: GraveBillingProblem[];
}

/** Whether the grave's prices split evenly across its recipients. */
export function checkGraveBilling(
  recipients: number,
  lines: InvoiceLineInput[],
): Promise<GraveBillingCheck> {
  return postJson<GraveBillingCheck>("/odata/ValidateGraveBilling", {
    Recipients: recipients,
    Lines: lines.map((line) => ({
      ItemId: line.itemId,
      Count: line.count,
      UnitPrice: line.unitPrice,
      PriceIncludesVat: line.priceIncludesVat,
    })),
  });
}

// ---- generation -----------------------------------------------------------

export interface StartedJob {
  Id: number;
  Status: string;
  ProgressPercent: number;
}

export function generateGraveInvoices(year: number, half: number): Promise<StartedJob> {
  return postJson<StartedJob>("/odata/GenerateGraveInvoices", { year, half });
}

export const JOB_TIMEOUT_MS = 10 * 60 * 1000;

/** Polls the job once a second until it succeeds or fails; throws
 *  ApiError("JobTimeout") after ten minutes. */
export async function pollJob(
  jobId: number,
  onProgress: (percent: number) => void,
  timeoutMs = JOB_TIMEOUT_MS,
): Promise<Job> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    if (Date.now() > deadline) {
      throw new ApiError("JobTimeout");
    }
    const job = (await odata.Jobs(jobId).query().execute()).data;
    if (job.Status === JobStatus.Succeeded || job.Status === JobStatus.Failed) {
      return job;
    }
    onProgress(job.ProgressPercent ?? 0);
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}

// ---- import / export ------------------------------------------------------

export interface ImportProblem {
  file: string;
  row: number | null;
  code: string;
  message: string;
}

export interface ImportSummary {
  recipients: number;
  items: number;
  graves: number;
  itemLinks: number;
  recipientLinks: number;
}

/** The archive was rejected: nothing was imported, every problem is listed. */
export class ImportRejectedError extends Error {
  public constructor(public readonly problems: ImportProblem[]) {
    super("ImportInvalid");
    this.name = "ImportRejectedError";
  }
}

export function downloadImportTemplate(): Promise<Blob> {
  return getBlob("/odata/ImportTemplate");
}

export async function importArchive(file: File): Promise<ImportSummary> {
  const body = new FormData();
  body.append("file", file);
  try {
    const response = await request("/odata/ImportArchive", { method: "POST", body });
    return ((await response.json()) as { summary: ImportSummary }).summary;
  } catch (error) {
    const problems =
      error instanceof ApiError
        ? (error.body as { problems?: ImportProblem[] } | undefined)?.problems
        : undefined;
    if (Array.isArray(problems)) {
      throw new ImportRejectedError(problems);
    }
    throw error;
  }
}
```

- [ ] **Step 7: Create `frontend-vue/src/api/queries.ts`**

```ts
import { type QueryClient, useQuery } from "@tanstack/vue-query";
import type { Ref } from "vue";
import type { Invoice, InvoiceItem, Recipient } from "./generated";
import { odata } from "./odata";

/** The backend caps $top at 200 (Program.cs, SetMaxTop). */
const PAGE_SIZE = 200;

/** Reads a whole collection in pages the server accepts. */
export async function readAll<T>(page: (skip: number, top: number) => Promise<T[]>): Promise<T[]> {
  const rows: T[] = [];
  for (let skip = 0; ; skip += PAGE_SIZE) {
    const chunk = await page(skip, PAGE_SIZE);
    rows.push(...chunk);
    if (chunk.length < PAGE_SIZE) {
      return rows;
    }
  }
}

export function useVatRatesQuery() {
  return useQuery({
    queryKey: ["vatRates"],
    queryFn: async () =>
      (
        await odata
          .VatRates()
          .query((b, q) => b.orderBy(q.Rate.asc()))
          .execute()
      ).data.value,
  });
}

/** Items, newest first; a term filters on number or description. */
export function fetchItems(term: string): Promise<InvoiceItem[]> {
  const trimmed = term.trim();
  return readAll(async (skip, top) => {
    const response = await odata
      .InvoiceItems()
      .query((b, q) => {
        if (trimmed) {
          b.filter(q.Number.contains(trimmed).or(q.Description.contains(trimmed)));
        }
        b.orderBy(q.Id.desc()).skip(skip).top(top);
      })
      .execute();
    return response.data.value;
  });
}

/** Pass `ref("")` for the whole catalogue; pickers share that cache entry. */
export function useItemsQuery(term: Ref<string>) {
  return useQuery({
    queryKey: ["items", "list", term],
    queryFn: () => fetchItems(term.value),
  });
}

/** Each item's current VAT rate. Price fields convert with the item's rate as
 *  it is now, since that is what the backend converts the saved value with. */
export async function loadItemVatRates(queryClient: QueryClient): Promise<Map<number, number>> {
  const items = await queryClient.fetchQuery({
    queryKey: ["items", "list", ""],
    queryFn: () => fetchItems(""),
  });
  return new Map(items.map((item) => [item.Id, item.VatRate]));
}

/** Recipients, newest first; a term filters on the name. */
export function fetchRecipients(term: string): Promise<Recipient[]> {
  const trimmed = term.trim();
  return readAll(async (skip, top) => {
    const response = await odata
      .Recipients()
      .query((b, q) => {
        if (trimmed) {
          b.filter(q.Name.contains(trimmed));
        }
        b.orderBy(q.Id.desc()).skip(skip).top(top);
      })
      .execute();
    return response.data.value;
  });
}

export function useRecipientsQuery(term: Ref<string>) {
  return useQuery({
    queryKey: ["recipients", "list", term],
    queryFn: () => fetchRecipients(term.value),
  });
}

export function useSettingsQuery() {
  return useQuery({
    queryKey: ["settings"],
    queryFn: async () => (await odata.Settings().query().execute()).data,
  });
}

/** An invoice chain, newest revision first, lines expanded. Takes the root id
 *  every list row carries; a revision's own id resolves through one extra read. */
export async function loadChain(rootId: number): Promise<Invoice[]> {
  const byRoot = (root: number) =>
    readAll(async (skip, top) => {
      const response = await odata
        .Invoices()
        .query((b, q) => {
          b.filter(q.RootId.eq(root))
            .orderBy(q.RevisionNumber.desc())
            .expanding("Lines", (e) => e)
            .skip(skip)
            .top(top);
        })
        .execute();
      return response.data.value;
    });

  const rows = await byRoot(rootId);
  if (rows.length) {
    return rows;
  }
  const single = (
    await odata
      .Invoices()
      .query((b, q) => b.filter(q.Id.eq(rootId)).select("RootId"))
      .execute()
  ).data.value;
  return single.length ? byRoot(single[0].RootId) : [];
}
```

If `b.orderBy(...)` inside an arrow without braces makes TypeScript complain that the query function must return `void`, wrap the body in braces (`(b, q) => { b.orderBy(q.Rate.asc()); }`).

- [ ] **Step 8: Verify**

Run from `frontend-vue/`: `npm run typecheck && npm run lint && npm run build` — Expected: exit 0 each.

Manual check against the E2E stack (read-only): `curl -s 'http://localhost:8081/odata/VatRates?$top=1'` — Expected: JSON with `"Rate":` as a number (not a string). This is why nothing in this frontend converts decimals (the UI5 model asked for strings; plain fetch gets numbers).

- [ ] **Step 9: Commit**

```bash
git add frontend-vue
git commit -m "chore(vue): generate a typed OData client and add the endpoint layer"
```

---

### Task 3: Ported pure logic (`lib/`)

**Files:**

- Create: `frontend-vue/src/lib/money.ts`, `halfYear.ts`, `format.ts`, `download.ts`, `lines.ts`, `billing.ts`, `generation.ts`, `revisions.ts`

**Interfaces:**

- Consumes: `endpoints.ts` (`fetchDocuments`, `documentUrl`, `InvoiceLineInput`, `GraveBillingProblem`), `ApiError`, generated `Invoice`/`InvoiceLine`.
- Produces:
  - `grossFromNet(net, vatRate): number`, `grossPerLine(lines: GrossLine[], recipients: number): number[]`, `priceFieldValue(net, priceIncludesVat, vatRate): number`, `lineNet(count, unitPrice): number`
  - `currentHalfYear(): [number, number]`, `halfYearOptions(back, ahead): YearHalfOption[]`, `yearOptions(back, ahead)`, `halfYearText(year, half)`, `halfYearKey(year, half): string`, `parseHalfYearKey(key): [number, number]`
  - `formatDate`, `formatAmount`, `formatVatRate`, `formatInvoiceNumber`, `formatRevisionBadge`, `formatAddressLine`, `formatShare`, `toIsoDate(date): string`, `parseIsoDate(value): Date | null`
  - `downloadBlob(blob, fileName)`, `openDocument(invoiceId, kind)`
  - `DraftLine`, `newLine(): DraftLine`, `toLineInputs(lines): InvoiceLineInput[]` (throws `ApiError("ItemRequired")`)
  - `billingProblemText(problems, recipients, t): string`
  - `generationSummary(resultJson, t): string`
  - `RevisionLineView`, `RevisionView`, `buildRevisions(chain: Invoice[]): RevisionView[]`
  - `type Translate = (key: string, list?: unknown[]) => string` (in `format.ts`)

- [ ] **Step 1: Create `src/lib/money.ts`** — copy verbatim from `frontend/src/model/api.ts`: the doc comments and bodies of `grossFromNet`, `GrossLine`, `grossPerLine`, `priceFieldValue`. Then add:

```ts
/** Count × price as the document prints it: rounded to the cent. */
export function lineNet(count: number, unitPrice: number): number {
  return Math.round(count * unitPrice * 100) / 100;
}
```

The copied functions must be character-for-character the UI5 logic (only `export` and types as there). They decide figures shown next to backend totals; a divergence shows a wrong cent.

- [ ] **Step 2: Create `src/lib/halfYear.ts`**

Copy `YearHalfOption`, `currentHalfYear`, `halfYearOptions`, `yearOptions`, `halfYearText` verbatim from `frontend/src/model/api.ts`, then add:

```ts
/** "2026-1": the key the half-year selects use. */
export function halfYearKey(year: number, half: number): string {
  return `${year}-${half}`;
}

export function parseHalfYearKey(key: string): [number, number] {
  const [year, half] = key.split("-").map(Number);
  return [year, half];
}
```

- [ ] **Step 3: Create `src/lib/format.ts`**

```ts
/** vue-i18n's `t`, narrowed to what the lib helpers need. */
export type Translate = (key: string, list?: unknown[]) => string;

export function formatDate(value: Date | string | null | undefined): string {
  if (value == null || value === "") {
    return "";
  }
  const date = value instanceof Date ? value : new Date(value);
  return date.toLocaleDateString("de-DE");
}

export function formatAmount(value: number | null | undefined): string {
  if (value == null) {
    return "";
  }
  return `${value.toLocaleString("de-DE", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`;
}

/** "19", or "7,7" where a rate has a decimal -- never "19.00". */
export function formatVatRate(value: number | null | undefined): string {
  return value == null ? "" : value.toLocaleString("de-DE", { maximumFractionDigits: 2 });
}

/** "2026-0042-R1" -> "2026-0042": the chain, without the revision marker. */
export function formatInvoiceNumber(value: string | null | undefined): string {
  if (value == null) {
    return "";
  }
  const index = value.lastIndexOf("-R");
  return index >= 0 ? value.slice(0, index) : value;
}

export function formatRevisionBadge(value: number | null | undefined): string {
  return value != null && value > 0 ? `R${value}` : "";
}

/** The first line of a multi-line address. */
export function formatAddressLine(value: string | null | undefined): string {
  return (value ?? "").split("\n")[0].trim();
}

/** "33,3 %": a recipient's share, one decimal, as the UI5 detail page shows it. */
export function formatShare(value: number | null | undefined): string {
  return value == null ? "" : `${value.toFixed(1).replace(".", ",")} %`;
}

/** yyyy-MM-dd from the local calendar fields. Not toISOString(): a DatePicker
 *  value is local midnight, which is the previous day in UTC here. */
export function toIsoDate(date: Date | null | undefined): string {
  if (!date) {
    return "";
  }
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

/** An Edm.Date ("2026-01-31") as local midnight, for a DatePicker. */
export function parseIsoDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  return new Date(year, month - 1, day);
}
```

- [ ] **Step 4: Create `src/lib/download.ts`**

```ts
import { type DocumentKindName, documentUrl, fetchDocuments } from "@/api/endpoints";

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Opens an invoice's PDF in a new tab. The tab is opened before the await:
 *  one opened afterwards no longer belongs to the click and is blocked as a
 *  popup. Throws after closing the tab when the lookup fails. */
export async function openDocument(invoiceId: number, kind: DocumentKindName): Promise<void> {
  const tab = window.open("", "_blank");
  try {
    const match = (await fetchDocuments(invoiceId)).find((document) => document.Kind === kind);
    if (!match) {
      tab?.close();
      return;
    }
    if (tab) {
      tab.location.href = documentUrl(match.Id);
    } else {
      // a browser that refused the empty tab may still allow a real URL
      window.open(documentUrl(match.Id), "_blank");
    }
  } catch (error) {
    tab?.close();
    throw error;
  }
}
```

- [ ] **Step 5: Create `src/lib/lines.ts`**

```ts
import type { InvoiceLineInput } from "@/api/endpoints";
import { ApiError } from "@/api/http";

/** One row of a line editor. `unitPrice` is what the field shows: the gross
 *  when `priceIncludesVat`, the net otherwise; null means "no price entered". */
export interface DraftLine {
  key: number;
  itemId: number | null;
  count: number | null;
  unitPrice: number | null;
  priceIncludesVat: boolean;
}

let nextKey = 1;

export function newLine(init: Partial<Omit<DraftLine, "key">> = {}): DraftLine {
  return {
    key: nextKey++,
    itemId: null,
    count: 1,
    unitPrice: null,
    priceIncludesVat: false,
    ...init,
  };
}

/** The payload for any line-taking endpoint. Every line needs an item
 *  (ItemRequired). An empty price is sent as null -- the item's default is
 *  billed -- while 0 is kept as a real price. */
export function toLineInputs(lines: DraftLine[]): InvoiceLineInput[] {
  return lines.map((line) => {
    if (line.itemId == null) {
      throw new ApiError("ItemRequired");
    }
    return {
      itemId: line.itemId,
      count: line.count ?? 0,
      unitPrice: line.unitPrice,
      priceIncludesVat: line.priceIncludesVat,
    };
  });
}
```

- [ ] **Step 6: Create `src/lib/billing.ts`** (port of `GraveEdit.controller.ts` `billingProblemText`)

```ts
import type { GraveBillingProblem } from "@/api/endpoints";
import { formatAmount, formatVatRate, type Translate } from "./format";

/** Why a grave's prices do not split evenly across its recipients, per VAT
 *  rate, with the nearest amounts that would work. */
export function billingProblemText(
  problems: GraveBillingProblem[],
  recipients: number,
  t: Translate,
): string {
  const lines = problems.map((problem) => {
    const sentence = t(recipients === 1 ? "pricesDoNotDivideLineOne" : "pricesDoNotDivideLine", [
      formatVatRate(problem.VatRate),
      formatAmount(problem.Amount),
      recipients,
    ]);
    const options = problem.Suggestions.map((value) => formatAmount(value));
    if (options.length >= 2) {
      return `${sentence} ${t("pricesNearbyValid", [options[0], options[1]])}`;
    }
    if (options.length === 1) {
      return `${sentence} ${t("pricesNearbyValidOne", [options[0]])}`;
    }
    return `${sentence} ${t("pricesAdjustAPrice")}`;
  });
  return [t("pricesDoNotDivideIntro"), "", ...lines].join("\n");
}
```

- [ ] **Step 7: Create `src/lib/generation.ts`** (port of `Graves.controller.ts` result message)

```ts
import type { Translate } from "./format";

type SkippedGrave = string | { Number: string; Reason?: string };

interface GenerationResult {
  InvoicedGraves?: number;
  SkippedGraves?: SkippedGrave[];
}

/** "Erstellt: 3 (Übersprungen: 12 (keine Empfänger), 14)" */
export function generationSummary(resultJson: string | null | undefined, t: Translate): string {
  const result = JSON.parse(resultJson ?? "{}") as GenerationResult;
  const skipped = (result.SkippedGraves ?? [])
    .map((entry) => {
      const number = typeof entry === "string" ? entry : entry.Number;
      const reason =
        typeof entry === "object" && entry.Reason ? t(`skipReason${entry.Reason}`) : "";
      return reason ? `${number} (${reason})` : number;
    })
    .join(", ");
  return (
    `${t("generationDone")}: ${result.InvoicedGraves ?? 0}` +
    (skipped ? ` (${t("skippedGraves")}: ${skipped})` : "")
  );
}
```

- [ ] **Step 8: Create `src/lib/revisions.ts`** (port of `InvoiceRevisions.controller.ts` `buildRevisionDetail`, lines part)

```ts
import type { Invoice } from "@/api/generated";
import { grossPerLine, lineNet } from "./money";

export interface RevisionLineView {
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** Count × UnitPrice as printed, rounded to the cent. */
  LineTotal: number;
  /** This line's part of what is actually charged, gross (grossPerLine). */
  GrossTotal: number;
}

export interface RevisionView {
  Id: number;
  InvoiceNumber: string;
  RevisionNumber: number;
  GeneratedAt: string;
  Lines: RevisionLineView[];
  NetTotal: number;
  VatTotal: number;
  GrossTotal: number;
}

/** Newest first, as loadChain returns them. On a grave invoice the lines are
 *  the whole grave while the totals are this recipient's share, so the
 *  recipient count is the divisor that puts the gross column in the net
 *  column's terms. */
export function buildRevisions(chain: Invoice[]): RevisionView[] {
  return chain.map((invoice) => {
    const lines = (invoice.Lines ?? [])
      .slice()
      .sort((a, b) => a.LineNumber - b.LineNumber)
      .map((line) => ({
        ItemNumber: line.ItemNumber,
        Description: line.Description,
        Count: line.Count,
        UnitPrice: line.UnitPrice,
        VatRate: line.VatRate,
        PriceIncludesVat: line.PriceIncludesVat,
        LineTotal: lineNet(line.Count, line.UnitPrice),
        GrossTotal: 0,
      }));
    const shares = grossPerLine(
      lines.map((line) => ({ NetTotal: line.LineTotal, VatRate: line.VatRate })),
      invoice.RecipientCount ?? 1,
    );
    lines.forEach((line, index) => {
      line.GrossTotal = shares[index];
    });
    return {
      Id: invoice.Id,
      InvoiceNumber: invoice.InvoiceNumber,
      RevisionNumber: invoice.RevisionNumber,
      GeneratedAt: invoice.GeneratedAt,
      Lines: lines,
      NetTotal: invoice.NetTotal,
      VatTotal: invoice.VatTotal,
      GrossTotal: invoice.GrossTotal,
    };
  });
}
```

If the generated `InvoiceLine` names a property differently from `frontend/src/model/api.ts` `InvoiceLineRow` (compare against `src/api/generated/burkard-invoicing-domain-entities/invoice-line/InvoiceLine.ts`), use the generated name.

- [ ] **Step 9: Verify**

`npm run typecheck && npm run lint && npm run build` — Expected: exit 0.

Diff check: `diff <(sed -n '/^export function grossPerLine/,/^}/p' ../frontend/src/model/api.ts) <(sed -n '/^export function grossPerLine/,/^}/p' src/lib/money.ts)` — Expected: no output. Repeat for `grossFromNet`, `priceFieldValue`, `halfYearOptions`, `yearOptions` (`src/lib/halfYear.ts`).

- [ ] **Step 10: Commit**

```bash
git add frontend-vue/src/lib
git commit -m "chore(vue): port the invoicing calculations and formatters"
```

---

### Task 4: App shell, i18n, theme and cross-cutting composables

**Files:**

- Create: `src/i18n/index.ts`, `src/i18n/de.json`, `src/i18n/en.json`, `src/i18n/prime.ts`, `src/router.ts`, `src/composables/useTheme.ts`, `useBlocking.ts`, `useMessage.ts`, `useErrorToast.ts`, `useConfirmAsync.ts`, `usePaidToggle.ts`, `useDebounced.ts`, `src/components/AppShell.vue`, `NavLinks.vue`, `PageHeader.vue`, `BlockingProgress.vue`, `MessageDialog.vue`
- Modify: `src/main.ts`, `src/App.vue` (replace the Task 1 versions)

**Interfaces:**

- Consumes: `ApiError`/`toApiError` (Task 2), `setInvoicePaid` (Task 2).
- Produces:
  - `i18n`, `locale: "de" | "en"`; message keys = the UI5 keys verbatim plus the new ones in Step 2. Placeholders `{0}` are vue-i18n list interpolation: `t("key", [a, b])`.
  - `router` (pages register their routes in later tasks, above the catch-all).
  - `useBlocking(): { run<T>(title, task: (progress: (percent: number, title?: string) => void) => Promise<T>): Promise<T> }`
  - `showMessage(title: string, text: string, severity?: "error" | "info"): void`
  - `useErrorToast(): { showError(error: unknown): void }`
  - `useConfirmAsync(): (message: string) => Promise<boolean>`
  - `usePaidToggle(): (invoiceId: number, paidOn: string | null) => Promise<boolean>`
  - `useDebounced<T>(source: Ref<T>, ms?: number): Ref<T>`
  - `useTheme(): { dark: Ref<boolean>; toggle(): void }`, `applyStoredTheme()`
  - `<PageHeader :title :crumbs="[{ label, to? }]">` with an `actions` slot.

- [ ] **Step 1: Convert the UI5 message bundles**

Run from `frontend-vue/`:

```sh
mkdir -p src/i18n
node -e '
const fs = require("fs");
for (const [source, target] of [["i18n.properties", "de.json"], ["i18n_en.properties", "en.json"]]) {
  const out = {};
  for (const line of fs.readFileSync("../frontend/src/i18n/" + source, "utf8").split("\n")) {
    const match = /^([A-Za-z0-9_]+)=(.*)$/.exec(line);
    if (match) out[match[1]] = match[2];
  }
  fs.writeFileSync("src/i18n/" + target, JSON.stringify(out, null, 2) + "\n");
}'
node -e 'const de=require("./src/i18n/de.json"),en=require("./src/i18n/en.json");const a=Object.keys(de).sort().join(),b=Object.keys(en).sort().join();console.log(Object.keys(de).length, a===b)'
```

Expected: a count around 190 and `true`. (The bundles contain no `\` escapes and no `@` or `|`, which vue-i18n would treat as syntax.)

- [ ] **Step 2: Add the keys this frontend needs**

```sh
node -e '
const fs = require("fs");
const add = {
  de: { errorTitle: "Fehler", ok: "OK", navigation: "Navigation", menu: "Menü",
        themeToggle: "Hell/Dunkel umschalten", editInvoice: "Rechnung bearbeiten",
        deleteLine: "Zeile löschen", confirmTitle: "Bitte bestätigen",
        confirmDeleteVatRate: "Soll der MwSt.-Satz {0} % gelöscht werden?",
        moreActions: "Weitere Aktionen", linesTotal: "Summe ca.: {0} netto, {1} brutto",
        selectItem: "Leistung wählen", selectRecipient: "Empfänger wählen", year: "Jahr",
        notFound: "Nichts gefunden.", loading: "Wird geladen …", noFileChosen: "Keine Datei ausgewählt" },
  en: { errorTitle: "Error", ok: "OK", navigation: "Navigation", menu: "Menu",
        themeToggle: "Toggle light/dark", editInvoice: "Edit invoice",
        deleteLine: "Delete line", confirmTitle: "Please confirm",
        confirmDeleteVatRate: "Delete the VAT rate {0} %?",
        moreActions: "More actions", linesTotal: "Total approx.: {0} net, {1} gross",
        selectItem: "Select item", selectRecipient: "Select recipient", year: "Year",
        notFound: "Nothing found.", loading: "Loading …", noFileChosen: "No file chosen" },
};
for (const lang of ["de", "en"]) {
  const file = "src/i18n/" + lang + ".json";
  const messages = JSON.parse(fs.readFileSync(file, "utf8"));
  fs.writeFileSync(file, JSON.stringify({ ...messages, ...add[lang] }, null, 2) + "\n");
}'
```

- [ ] **Step 3: Create `src/i18n/index.ts` and `src/i18n/prime.ts`**

`src/i18n/index.ts`:

```ts
import { createI18n } from "vue-i18n";
import de from "./de.json";
import en from "./en.json";

/** German unless the browser prefers English -- the rule the UI5 app follows.
 *  Numbers and dates stay de-DE either way. */
export const locale: "de" | "en" = navigator.language.toLowerCase().startsWith("en") ? "en" : "de";

export const i18n = createI18n({
  legacy: false,
  locale,
  fallbackLocale: "de",
  messages: { de, en },
});
```

`src/i18n/prime.ts`:

```ts
/** PrimeVue's own texts in German (date picker, empty lists, confirm buttons).
 *  Merged into its defaults, so the aria labels it ships stay in place. */
export const primeLocaleDe = {
  firstDayOfWeek: 1,
  dayNames: ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"],
  dayNamesShort: ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"],
  dayNamesMin: ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"],
  monthNames: [
    "Januar",
    "Februar",
    "März",
    "April",
    "Mai",
    "Juni",
    "Juli",
    "August",
    "September",
    "Oktober",
    "November",
    "Dezember",
  ],
  monthNamesShort: [
    "Jan",
    "Feb",
    "Mär",
    "Apr",
    "Mai",
    "Jun",
    "Jul",
    "Aug",
    "Sep",
    "Okt",
    "Nov",
    "Dez",
  ],
  today: "Heute",
  clear: "Leeren",
  weekHeader: "KW",
  dateFormat: "dd.mm.yy",
  emptyMessage: "Keine Daten",
  emptyFilterMessage: "Keine Treffer",
  emptySearchMessage: "Keine Treffer",
  accept: "Ja",
  reject: "Nein",
  choose: "Auswählen",
};
```

- [ ] **Step 4: Create the composables**

`src/composables/useTheme.ts`:

```ts
import { ref } from "vue";

const STORAGE_KEY = "invoicing-vue-theme";
const dark = ref(false);

function apply(isDark: boolean): void {
  dark.value = isDark;
  document.documentElement.classList.toggle("app-dark", isDark);
}

function stored(): "light" | "dark" | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

/** Before mount: the remembered choice, else the system's. */
export function applyStoredTheme(): void {
  const choice = stored();
  apply(choice ? choice === "dark" : window.matchMedia("(prefers-color-scheme: dark)").matches);
}

export function useTheme() {
  function toggle(): void {
    apply(!dark.value);
    try {
      localStorage.setItem(STORAGE_KEY, dark.value ? "dark" : "light");
    } catch {
      // storage unavailable (private mode): the choice lasts for this page only
    }
  }
  return { dark, toggle };
}
```

`src/composables/useBlocking.ts`:

```ts
import { reactive } from "vue";

/** What BlockingProgress shows. One at a time: the UI is blocked while it runs. */
export const blockingState = reactive({
  active: false,
  title: "",
  percent: null as number | null,
});

/** Runs a long backend task behind a full-screen overlay (constitution VIII).
 *  The overlay closes in `finally`, so an error thrown by the task reaches the
 *  caller -- and its message -- only after the overlay is gone. */
export function useBlocking() {
  async function run<T>(
    title: string,
    task: (progress: (percent: number, title?: string) => void) => Promise<T>,
  ): Promise<T> {
    blockingState.active = true;
    blockingState.title = title;
    blockingState.percent = null;
    try {
      return await task((percent, nextTitle) => {
        blockingState.percent = percent;
        if (nextTitle) {
          blockingState.title = nextTitle;
        }
      });
    } finally {
      blockingState.active = false;
      blockingState.percent = null;
    }
  }
  return { run };
}
```

`src/composables/useMessage.ts`:

```ts
import { reactive } from "vue";

/** A modal message that has to be read and dismissed: a refusal with an
 *  explanation, or a result summary. Transient errors are toasts instead. */
export const messageState = reactive({
  visible: false,
  title: "",
  text: "",
  severity: "error" as "error" | "info",
});

export function showMessage(
  title: string,
  text: string,
  severity: "error" | "info" = "error",
): void {
  Object.assign(messageState, { visible: true, title, text, severity });
}
```

`src/composables/useErrorToast.ts`:

```ts
import { useToast } from "primevue/usetoast";
import { useI18n } from "vue-i18n";
import { toApiError } from "@/api/http";

/** Shows a failed call or a failed client check. A known code has its own
 *  text (`error<Code>`, the keys the UI5 app's CODE_TO_I18N maps to); anything
 *  else shows the generic text with the code. */
export function useErrorToast() {
  const toast = useToast();
  const { t, te } = useI18n();

  function showError(error: unknown): void {
    const { code } = toApiError(error);
    const key = `error${code}`;
    toast.add({
      severity: "error",
      summary: t("errorTitle"),
      detail: te(key) ? t(key) : `${t("errorGeneric")} (${code})`,
      life: 8000,
    });
  }

  return { showError };
}
```

`src/composables/useConfirmAsync.ts`:

```ts
import { useConfirm } from "primevue/useconfirm";
import { useI18n } from "vue-i18n";

/** A yes/no confirmation that can be awaited. */
export function useConfirmAsync() {
  const confirm = useConfirm();
  const { t } = useI18n();

  return (message: string): Promise<boolean> =>
    new Promise((resolve) => {
      confirm.require({
        header: t("confirmTitle"),
        message,
        icon: "pi pi-exclamation-triangle",
        rejectProps: { label: t("cancel"), severity: "secondary", outlined: true },
        acceptProps: { label: t("yes") },
        accept: () => resolve(true),
        reject: () => resolve(false),
        onHide: () => resolve(false),
      });
    });
}
```

`src/composables/usePaidToggle.ts`:

```ts
import { useQueryClient } from "@tanstack/vue-query";
import { useI18n } from "vue-i18n";
import { setInvoicePaid } from "@/api/endpoints";
import { useConfirmAsync } from "./useConfirmAsync";
import { useErrorToast } from "./useErrorToast";

/** Marks an unpaid invoice paid straight away, or asks before taking a mark
 *  back -- unmarking discards the recorded day, which no screen shows.
 *  Answers whether anything changed. */
export function usePaidToggle() {
  const ask = useConfirmAsync();
  const { showError } = useErrorToast();
  const { t } = useI18n();
  const queryClient = useQueryClient();

  return async (invoiceId: number, paidOn: string | null): Promise<boolean> => {
    if (paidOn && !(await ask(t("confirmMarkUnpaid")))) {
      return false;
    }
    try {
      await setInvoicePaid(invoiceId, !paidOn);
      await queryClient.invalidateQueries();
      return true;
    } catch (error) {
      showError(error);
      return false;
    }
  };
}
```

`src/composables/useDebounced.ts`:

```ts
import { ref, type Ref, watch } from "vue";

/** `source`, but only once it has stopped changing for `ms`. */
export function useDebounced<T>(source: Ref<T>, ms = 300): Ref<T> {
  const value = ref(source.value) as Ref<T>;
  let timer: ReturnType<typeof setTimeout> | undefined;
  watch(source, (next) => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      value.value = next;
    }, ms);
  });
  return value;
}
```

- [ ] **Step 5: Create the shell components**

`src/components/NavLinks.vue`:

```vue
<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRoute } from "vue-router";

const { t } = useI18n();
const route = useRoute();

const links = computed(() => [
  { to: "/invoices", label: t("menuInvoices"), icon: "pi pi-file" },
  { to: "/graves", label: t("menuGraves"), icon: "pi pi-home" },
  { to: "/items", label: t("menuItems"), icon: "pi pi-box" },
  { to: "/recipients", label: t("menuRecipients"), icon: "pi pi-users" },
  { to: "/import-export", label: t("menuImportExport"), icon: "pi pi-upload" },
  { to: "/settings", label: t("menuSettings"), icon: "pi pi-cog" },
]);

// detail routes are siblings, not children, so RouterLink's own active class
// would not light up "Gräber" on /graves/12
function isActive(to: string): boolean {
  return route.path === to || route.path.startsWith(`${to}/`);
}
</script>

<template>
  <nav :aria-label="t('navigation')" class="flex flex-col gap-1">
    <RouterLink
      v-for="link in links"
      :key="link.to"
      :to="link.to"
      :aria-current="isActive(link.to) ? 'page' : undefined"
      class="flex items-center gap-3 rounded-lg px-3 py-2 text-color no-underline transition-colors hover:bg-emphasis"
      :class="{ 'bg-emphasis font-semibold text-primary': isActive(link.to) }"
    >
      <i :class="link.icon" aria-hidden="true" />
      <span>{{ link.label }}</span>
    </RouterLink>
  </nav>
</template>
```

`src/components/AppShell.vue`:

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useRoute } from "vue-router";
import { useTheme } from "@/composables/useTheme";
import NavLinks from "./NavLinks.vue";

const { t } = useI18n();
const route = useRoute();
const { dark, toggle } = useTheme();
const drawerOpen = ref(false);

watch(
  () => route.fullPath,
  () => {
    drawerOpen.value = false;
  },
);
</script>

<template>
  <div class="flex min-h-screen">
    <aside
      class="sticky top-0 hidden h-screen w-64 shrink-0 flex-col gap-6 border-r border-surface bg-surface-0 p-4 md:flex dark:bg-surface-900"
    >
      <div class="flex items-center gap-2 px-3 pt-2 text-lg font-semibold">
        <i class="pi pi-file-edit text-primary" aria-hidden="true" />
        {{ t("appTitle") }}
      </div>
      <NavLinks />
      <Button
        class="mt-auto"
        :icon="dark ? 'pi pi-sun' : 'pi pi-moon'"
        :label="t('themeToggle')"
        severity="secondary"
        text
        @click="toggle"
      />
    </aside>

    <Drawer v-model:visible="drawerOpen" :header="t('appTitle')">
      <NavLinks />
    </Drawer>

    <div class="flex min-w-0 flex-1 flex-col">
      <header
        class="sticky top-0 z-10 flex items-center gap-2 border-b border-surface bg-surface-0 px-3 py-2 md:hidden dark:bg-surface-900"
      >
        <Button icon="pi pi-bars" text rounded :aria-label="t('menu')" @click="drawerOpen = true" />
        <span class="flex-1 font-semibold">{{ t("appTitle") }}</span>
        <Button
          :icon="dark ? 'pi pi-sun' : 'pi pi-moon'"
          text
          rounded
          :aria-label="t('themeToggle')"
          @click="toggle"
        />
      </header>
      <main class="mx-auto w-full max-w-7xl flex-1 p-4 md:p-8">
        <slot />
      </main>
    </div>
  </div>
</template>
```

`src/components/PageHeader.vue`:

```vue
<script setup lang="ts">
import { RouterLink, type RouteLocationRaw } from "vue-router";

defineProps<{
  title: string;
  crumbs?: { label: string; to?: RouteLocationRaw }[];
}>();
</script>

<template>
  <div class="mb-6 flex flex-wrap items-end justify-between gap-3">
    <div class="min-w-0">
      <nav
        v-if="crumbs?.length"
        aria-label="Breadcrumb"
        class="mb-1 flex flex-wrap items-center gap-1 text-sm text-muted-color"
      >
        <template v-for="(crumb, index) in crumbs" :key="index">
          <RouterLink v-if="crumb.to" :to="crumb.to" class="text-muted-color hover:underline">
            {{ crumb.label }}
          </RouterLink>
          <span v-else>{{ crumb.label }}</span>
          <i
            v-if="index < crumbs.length - 1"
            class="pi pi-angle-right text-xs"
            aria-hidden="true"
          />
        </template>
      </nav>
      <h1 class="m-0 truncate text-2xl font-semibold">{{ title }}</h1>
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
```

`src/components/BlockingProgress.vue`:

```vue
<script setup lang="ts">
import ProgressBar from "primevue/progressbar";
import { blockingState } from "@/composables/useBlocking";
</script>

<template>
  <!-- above PrimeVue's overlays (dialogs and drawers stack from 1100) -->
  <div
    v-if="blockingState.active"
    class="fixed inset-0 z-[5000] flex items-center justify-center bg-black/40 p-4"
    role="alertdialog"
    aria-modal="true"
    aria-busy="true"
    :aria-label="blockingState.title"
  >
    <div class="w-full max-w-sm rounded-xl bg-surface-0 p-6 shadow-xl dark:bg-surface-900">
      <p class="mt-0 mb-4 font-medium">{{ blockingState.title }}</p>
      <ProgressBar v-if="blockingState.percent !== null" :value="blockingState.percent" />
      <ProgressBar v-else mode="indeterminate" style="height: 6px" />
    </div>
  </div>
</template>
```

`src/components/MessageDialog.vue`:

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import { useI18n } from "vue-i18n";
import { messageState } from "@/composables/useMessage";

const { t } = useI18n();
</script>

<template>
  <Dialog
    v-model:visible="messageState.visible"
    modal
    :header="messageState.title"
    :style="{ width: '34rem' }"
    :breakpoints="{ '640px': '95vw' }"
  >
    <div class="flex gap-3">
      <i
        :class="
          messageState.severity === 'error'
            ? 'pi pi-times-circle text-red-500'
            : 'pi pi-info-circle text-primary'
        "
        class="mt-0.5 text-xl"
        aria-hidden="true"
      />
      <p class="m-0 whitespace-pre-line">{{ messageState.text }}</p>
    </div>
    <template #footer>
      <Button :label="t('ok')" autofocus @click="messageState.visible = false" />
    </template>
  </Dialog>
</template>
```

- [ ] **Step 6: Create `src/router.ts`**

```ts
import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

/** The UI5 app's routes, same paths. Each page task adds its route above the
 *  catch-all. */
const routes: RouteRecordRaw[] = [
  { path: "/", redirect: "/invoices" },
  { path: "/:pathMatch(.*)*", redirect: "/invoices" },
];

export const router = createRouter({ history: createWebHistory(), routes });
```

- [ ] **Step 7: Replace `src/main.ts` and `src/App.vue`**

`src/main.ts`:

```ts
import "primeicons/primeicons.css";
import "./main.css";
import Aura from "@primeuix/themes/aura";
import { QueryClient, VueQueryPlugin } from "@tanstack/vue-query";
import PrimeVue from "primevue/config";
import ConfirmationService from "primevue/confirmationservice";
import ToastService from "primevue/toastservice";
import Tooltip from "primevue/tooltip";
import { createApp } from "vue";
import App from "./App.vue";
import { applyStoredTheme } from "./composables/useTheme";
import { i18n, locale } from "./i18n";
import { router } from "./router";

applyStoredTheme();
document.documentElement.lang = locale;
document.title = i18n.global.t("appTitle");

// Data only changes through this app or the UI5 one, so a refetch on every
// window focus would reload lists under an open editor for nothing. Instead
// every successful write invalidates all queries.
const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, retry: false } },
});

createApp(App)
  .use(PrimeVue, {
    theme: {
      preset: Aura,
      options: {
        darkModeSelector: ".app-dark",
        cssLayer: { name: "primevue", order: "theme, base, primevue" },
      },
    },
  })
  .use(ToastService)
  .use(ConfirmationService)
  .directive("tooltip", Tooltip)
  .use(VueQueryPlugin, { queryClient })
  .use(i18n)
  .use(router)
  .mount("#app");
```

`src/App.vue`:

```vue
<script setup lang="ts">
import { usePrimeVue } from "primevue/config";
import ConfirmDialog from "primevue/confirmdialog";
import Toast from "primevue/toast";
import { RouterView } from "vue-router";
import AppShell from "@/components/AppShell.vue";
import BlockingProgress from "@/components/BlockingProgress.vue";
import MessageDialog from "@/components/MessageDialog.vue";
import { locale } from "@/i18n";
import { primeLocaleDe } from "@/i18n/prime";

const primevue = usePrimeVue();
if (locale === "de" && primevue.config.locale) {
  Object.assign(primevue.config.locale, primeLocaleDe);
}
</script>

<template>
  <AppShell>
    <RouterView />
  </AppShell>
  <Toast position="top-right" />
  <ConfirmDialog />
  <MessageDialog />
  <BlockingProgress />
</template>
```

- [ ] **Step 8: Verify**

`npm run typecheck && npm run lint && npm run build` — Expected: exit 0.

Manual check: `VITE_PROXY_TARGET=http://localhost:8081 npm run dev`, open `http://localhost:8082`.

- The sidebar lists the six sections in German; the theme button switches light/dark and a reload keeps the choice.
- At 390 px width (devtools device mode) the sidebar is gone; the hamburger opens the drawer.
- Temporarily add to `App.vue` `<script setup>`: `import { useBlocking } from "@/composables/useBlocking"; void useBlocking().run("Test", () => new Promise((_, reject) => setTimeout(() => reject(new Error("x")), 1500))).catch(() => {});`. Expected (Review Focus 4): the overlay shows "Test" with an indeterminate bar, then closes although the task failed. **Remove the snippet** afterwards.

- [ ] **Step 9: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the app shell, i18n, theme and shared composables"
```

---

### Task 5: Settings page (stamp word, ending text, document language, VAT rates)

**Files:**

- Create: `src/pages/SettingsPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `odata.Settings()`, `odata.VatRates()`, `useSettingsQuery`, `useVatRatesQuery`, `useErrorToast`, `useConfirmAsync`, `PageHeader`, `formatVatRate`, `ApiError`.

Behaviour reference: `frontend/src/view/Settings.view.xml`, `frontend/src/controller/Settings.controller.ts`. Difference by spec: deleting a VAT rate asks first.

- [ ] **Step 1: Create `src/pages/SettingsPage.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Textarea from "primevue/textarea";
import { useToast } from "primevue/usetoast";
import { computed, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { ApiError } from "@/api/http";
import { odata } from "@/api/odata";
import { useSettingsQuery, useVatRatesQuery } from "@/api/queries";
import PageHeader from "@/components/PageHeader.vue";
import { useConfirmAsync } from "@/composables/useConfirmAsync";
import { useErrorToast } from "@/composables/useErrorToast";
import { formatVatRate } from "@/lib/format";

const { t } = useI18n();
const toast = useToast();
const { showError } = useErrorToast();
const ask = useConfirmAsync();
const queryClient = useQueryClient();

const settings = useSettingsQuery();
const form = reactive({ StampWord: "", EndingText: "", Language: "de" });
watch(
  () => settings.data.value,
  (value) => {
    if (value) {
      Object.assign(form, {
        StampWord: value.StampWord,
        EndingText: value.EndingText,
        Language: value.Language,
      });
    }
  },
  { immediate: true },
);

const languages = computed(() => [
  { value: "de", label: t("languageDe") },
  { value: "en", label: t("languageEn") },
]);

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  try {
    // no client check: the backend answers StampWordRequired / LanguageNotSupported
    await odata
      .Settings()
      .patch({ ...form })
      .execute();
    await queryClient.invalidateQueries();
    toast.add({ severity: "success", summary: t("saved"), life: 3000 });
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}

const vatRates = useVatRatesQuery();
const newRate = ref<number | null>(null);

async function addVatRate(): Promise<void> {
  try {
    if (newRate.value == null || !(newRate.value > 0)) {
      throw new ApiError("InvalidVatRate");
    }
    await odata.VatRates().create({ Rate: newRate.value }).execute();
    newRate.value = null;
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  }
}

async function deleteVatRate(id: number, rate: number): Promise<void> {
  if (!(await ask(t("confirmDeleteVatRate", [formatVatRate(rate)])))) {
    return;
  }
  try {
    await odata.VatRates(id).delete().execute();
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <PageHeader :title="t('menuSettings')" />

  <div class="grid gap-6 lg:grid-cols-[2fr_1fr]">
    <Card>
      <template #title>{{ t("menuSettings") }}</template>
      <template #content>
        <form class="flex flex-col gap-5" @submit.prevent="save">
          <div class="flex flex-col gap-2">
            <label for="stampWord" class="font-medium">{{ t("settingsStampWord") }}</label>
            <InputText id="stampWord" v-model="form.StampWord" class="w-full md:w-80" />
          </div>
          <div class="flex flex-col gap-2">
            <label for="endingText" class="font-medium">{{ t("settingsEndingText") }}</label>
            <Textarea id="endingText" v-model="form.EndingText" rows="6" class="w-full" />
          </div>
          <div class="flex flex-col gap-2">
            <label for="language" class="font-medium">{{ t("settingsDocumentLanguage") }}</label>
            <Select
              v-model="form.Language"
              input-id="language"
              :options="languages"
              option-label="label"
              option-value="value"
              class="w-full md:w-80"
            />
          </div>
          <div>
            <Button type="submit" :label="t('save')" icon="pi pi-check" :loading="saving" />
          </div>
        </form>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("settingsVatRates") }}</template>
      <template #content>
        <DataTable
          :value="vatRates.data.value ?? []"
          data-key="Id"
          size="small"
          :loading="vatRates.isFetching.value"
        >
          <template #empty>{{ t("noData") }}</template>
          <Column :header="t('vatRate')">
            <template #body="{ data }">{{ formatVatRate(data.Rate) }} %</template>
          </Column>
          <Column class="w-16 text-right">
            <template #body="{ data }">
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                rounded
                :aria-label="t('delete')"
                v-tooltip.left="t('delete')"
                @click="deleteVatRate(data.Id, data.Rate)"
              />
            </template>
          </Column>
        </DataTable>
        <form class="mt-4 flex flex-wrap items-end gap-2" @submit.prevent="addVatRate">
          <div class="flex flex-col gap-2">
            <label for="newRate" class="text-sm">{{ t("vatRate") }}</label>
            <InputNumber
              v-model="newRate"
              input-id="newRate"
              locale="de-DE"
              :max-fraction-digits="2"
              suffix=" %"
              :input-style="{ width: '8rem' }"
            />
          </div>
          <Button type="submit" :label="t('addVatRate')" icon="pi pi-plus" outlined />
        </form>
      </template>
    </Card>
  </div>
</template>
```

- [ ] **Step 2: Register the route** — in `src/router.ts`, above the catch-all:

```ts
  { path: "/settings", component: () => import("@/pages/SettingsPage.vue") },
```

- [ ] **Step 3: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (dev server against 8081), `/settings`:

- Stamp word, ending text and language show the stored values; change the stamp word, save → "Gespeichert." toast; reload keeps it. Empty stamp word → error toast "Das Stempelwort darf nicht leer sein."
- Add VAT rate `7` → appears; add `7` again → "Dieser MwSt.-Satz ist bereits konfiguriert."; add `0` → "Der MwSt.-Satz muss positiv sein."
- Delete asks for confirmation; Cancel keeps the row, Yes removes it.
- Compare with the UI5 app on the same stack (`http://localhost:8081/#/settings`): same values.

- [ ] **Step 4: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the settings page"
```

---

### Task 6: Items page and item dialog

**Files:**

- Create: `src/pages/ItemsPage.vue`, `src/components/ItemDialog.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `useItemsQuery`, `useVatRatesQuery`, `odata.InvoiceItems()`, `getItemGraves`, `useDebounced`, `useErrorToast`, formatters.
- Produces: `<ItemDialog v-model:visible :item="InvoiceItem | null" />`.

Behaviour reference: `frontend/src/view/Items.view.xml`, `frontend/src/view/fragments/ItemDialog.fragment.xml`, `frontend/src/controller/Items.controller.ts`. Differences by spec: the table formats VAT rate and default price.

- [ ] **Step 1: Create `src/components/ItemDialog.vue`**

```vue
<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import Select from "primevue/select";
import { computed, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { getItemGraves, type ItemGraveGroup } from "@/api/endpoints";
import type { InvoiceItem } from "@/api/generated";
import { odata } from "@/api/odata";
import { useVatRatesQuery } from "@/api/queries";
import { useErrorToast } from "@/composables/useErrorToast";
import { formatAmount, formatVatRate } from "@/lib/format";

const props = defineProps<{ item: InvoiceItem | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const vatRates = useVatRatesQuery();

const form = reactive({
  Number: "",
  Description: "",
  VatRate: null as number | null,
  DefaultPrice: null as number | null,
});

watch(visible, (open) => {
  if (open) {
    Object.assign(form, {
      Number: props.item?.Number ?? "",
      Description: props.item?.Description ?? "",
      VatRate: props.item?.VatRate ?? null,
      // empty means "no default price"; 0 is a real price
      DefaultPrice: props.item?.DefaultPrice ?? null,
    });
    void vatRates.refetch();
  }
});

const itemId = computed(() => props.item?.Id ?? null);
const graves = useQuery({
  queryKey: ["items", "graves", itemId],
  queryFn: () => getItemGraves(itemId.value!),
  enabled: computed(() => visible.value && itemId.value != null),
});
const linked = computed(() => graves.data.value?.Linked ?? false);

function groupLabel(group: ItemGraveGroup): string {
  const price =
    group.UnitPrice == null
      ? t("itemPriceNone")
      : t(group.IsDefault ? "itemPriceDefault" : "itemPriceOwn", [formatAmount(group.UnitPrice)]);
  return group.PriceIncludesVat ? `${price} (${t("priceIncludesVatShort")})` : price;
}

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  const payload = {
    Number: form.Number,
    Description: form.Description,
    VatRate: form.VatRate ?? 0,
    DefaultPrice: form.DefaultPrice,
  };
  try {
    if (props.item) {
      await odata.InvoiceItems(props.item.Id).patch(payload).execute();
    } else {
      await odata.InvoiceItems().create(payload).execute();
    }
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="item ? t('editItem') : t('createItem')"
    :style="{ width: '30rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="itemForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="itemNumber" class="font-medium">{{ t("itemNumber") }}</label>
        <InputText id="itemNumber" v-model="form.Number" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemDescription" class="font-medium">{{ t("itemDescription") }}</label>
        <InputText id="itemDescription" v-model="form.Description" />
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemVatRate" class="font-medium">{{ t("vatRate") }}</label>
        <Select
          v-model="form.VatRate"
          input-id="itemVatRate"
          :options="vatRates.data.value ?? []"
          option-value="Rate"
          :option-label="(rate: { Rate: number }) => `${formatVatRate(rate.Rate)} %`"
          :disabled="linked"
        />
        <small v-if="linked" class="text-muted-color">{{ t("itemVatRateFixed") }}</small>
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemDefaultPrice" class="font-medium">{{ t("itemDefaultPrice") }}</label>
        <InputNumber
          v-model="form.DefaultPrice"
          input-id="itemDefaultPrice"
          locale="de-DE"
          :min-fraction-digits="2"
          :max-fraction-digits="2"
          suffix=" €"
        />
      </div>

      <section v-if="item" class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("itemGravesTitle") }}</h3>
        <Message
          v-if="graves.data.value && !graves.data.value.Linked"
          severity="secondary"
          size="small"
        >
          {{ t("itemGravesNone") }}
        </Message>
        <div
          v-for="(group, index) in graves.data.value?.Groups ?? []"
          :key="index"
          class="flex flex-col gap-1"
        >
          <span class="text-sm font-medium">{{ groupLabel(group) }}</span>
          <div class="flex flex-wrap gap-x-3 gap-y-1">
            <RouterLink
              v-for="grave in group.Graves"
              :key="grave.Id"
              :to="`/graves/${grave.Id}`"
              class="text-primary hover:underline"
              @click="visible = false"
            >
              {{ grave.GraveNumber }}
            </RouterLink>
          </div>
        </div>
      </section>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="itemForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
```

Payload note: `DefaultPrice: form.DefaultPrice` sends `null` for an empty field and `0` for zero (Review Focus 2). If the generated `EditableInvoiceItem` type rejects `DefaultPrice: null` or `VatRate: number` in `patch`, keep the payload object and adjust only the TypeScript annotation — do not change the values sent.

- [ ] **Step 2: Create `src/pages/ItemsPage.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import type { InvoiceItem } from "@/api/generated";
import { useItemsQuery } from "@/api/queries";
import ItemDialog from "@/components/ItemDialog.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useDebounced } from "@/composables/useDebounced";
import { formatAmount, formatVatRate } from "@/lib/format";

const { t } = useI18n();
const search = ref("");
const items = useItemsQuery(useDebounced(search));

const dialogOpen = ref(false);
const editing = ref<InvoiceItem | null>(null);

function open(item: InvoiceItem | null): void {
  editing.value = item;
  dialogOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuItems')">
    <template #actions>
      <Button :label="t('createItem')" icon="pi pi-plus" @click="open(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4">
        <IconField class="w-full md:w-80">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchByNumber')"
            :aria-label="t('searchByNumber')"
            class="w-full"
          />
        </IconField>
      </div>
      <DataTable
        :value="items.data.value ?? []"
        data-key="Id"
        :loading="items.isFetching.value"
        striped-rows
      >
        <template #empty>{{ t("noData") }}</template>
        <Column field="Number" :header="t('itemNumber')" />
        <Column field="Description" :header="t('itemDescription')" />
        <Column
          :header="t('vatRate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell"
        >
          <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
        </Column>
        <Column
          :header="t('itemDefaultPrice')"
          header-class="hidden sm:table-cell"
          body-class="hidden text-right tabular-nums sm:table-cell"
        >
          <template #body="{ data }">{{ formatAmount(data.DefaultPrice) }}</template>
        </Column>
        <Column class="w-16 text-right">
          <template #body="{ data }">
            <Button
              icon="pi pi-pencil"
              text
              rounded
              :aria-label="t('edit')"
              v-tooltip.left="t('edit')"
              @click="open(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <ItemDialog v-model:visible="dialogOpen" :item="editing" />
</template>
```

- [ ] **Step 3: Register the route** — `{ path: "/items", component: () => import("@/pages/ItemsPage.vue") },`

- [ ] **Step 4: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081), `/items` (Settings has at least one VAT rate):

- Create item `L1`/"Pflege"/19 %/empty price → row shows no price. Create `L2` with price `0` → row shows "0,00 €". Reopen `L1`: price field still empty; reopen `L2`: shows 0,00 (Review Focus 2).
- Duplicate number → "Diese Leistungsnummer existiert bereits."
- Search "pfl" narrows after a short pause; clearing it shows all.
- Editing an item shows "Kein Grab verwendet diese Leistung." (a linked item is checked again in Task 12).
- At 390 px width the VAT and price columns are hidden and the table does not scroll sideways.

- [ ] **Step 5: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the items page"
```

---

### Task 7: Recipients page, recipient dialogs and recipient picker

**Files:**

- Create: `src/pages/RecipientsPage.vue`, `src/components/RecipientDialog.vue`, `src/components/RecipientQuickCreate.vue`, `src/components/RecipientPicker.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `useRecipientsQuery`, `createRecipient`, `odata.Recipients(id).patch`, `useDebounced`, `useErrorToast`, `formatAddressLine`.
- Produces:
  - `<RecipientDialog v-model:visible :recipient="Recipient | null" />`
  - `<RecipientQuickCreate v-model:visible @created="(id: number) => …" />`
  - `<RecipientPicker v-model="recipientId: number | null" input-id="…" :default-to-newest="boolean" />` — Select over all recipients (newest first) with a "Neuer Empfänger" button; when `defaultToNewest` and the value is null it selects the newest recipient once the list has loaded.

Behaviour reference: `frontend/src/view/Recipients.view.xml`, fragments `RecipientDialog` and `RecipientQuickCreate`, `frontend/src/controller/Recipients.controller.ts`.

- [ ] **Step 1: Create `src/components/RecipientDialog.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createRecipient } from "@/api/endpoints";
import type { Recipient } from "@/api/generated";
import { odata } from "@/api/odata";
import { useErrorToast } from "@/composables/useErrorToast";

const props = defineProps<{ recipient: Recipient | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const form = reactive({ Number: "", Name: "", Address: "" });

watch(visible, (open) => {
  if (open) {
    Object.assign(form, {
      Number: props.recipient?.Number ?? "",
      Name: props.recipient?.Name ?? "",
      Address: props.recipient?.Address ?? "",
    });
  }
});

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  const number = form.Number.trim();
  try {
    if (props.recipient) {
      await odata
        .Recipients(props.recipient.Id)
        .patch({ Number: number, Name: form.Name, Address: form.Address })
        .execute();
    } else {
      // an empty number is left out: the backend assigns the next one
      await createRecipient({
        Name: form.Name,
        Address: form.Address,
        ...(number ? { Number: number } : {}),
      });
    }
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="recipient ? t('editRecipient') : t('createRecipient')"
    :style="{ width: '28rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="recipientForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="recipientNumber" class="font-medium">{{ t("recipientNumber") }}</label>
        <InputText
          id="recipientNumber"
          v-model="form.Number"
          :placeholder="t('recipientNumberHint')"
        />
      </div>
      <div class="flex flex-col gap-2">
        <label for="recipientName" class="font-medium">{{ t("recipientName") }}</label>
        <InputText id="recipientName" v-model="form.Name" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="recipientAddress" class="font-medium">{{ t("recipientAddress") }}</label>
        <Textarea id="recipientAddress" v-model="form.Address" rows="4" />
      </div>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="recipientForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
```

- [ ] **Step 2: Create `src/components/RecipientQuickCreate.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createRecipient } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";

const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ created: [id: number] }>();

const { t } = useI18n();
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const form = reactive({ Name: "", Address: "" });

watch(visible, (open) => {
  if (open) {
    Object.assign(form, { Name: "", Address: "" });
  }
});

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  try {
    const id = await createRecipient({ Name: form.Name, Address: form.Address });
    await queryClient.invalidateQueries({ queryKey: ["recipients"] });
    emit("created", id);
    visible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('newRecipient')"
    :style="{ width: '26rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="quickRecipientForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="quickName" class="font-medium">{{ t("recipientName") }}</label>
        <InputText id="quickName" v-model="form.Name" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="quickAddress" class="font-medium">{{ t("recipientAddress") }}</label>
        <Textarea id="quickAddress" v-model="form.Address" rows="4" />
      </div>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="quickRecipientForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
```

- [ ] **Step 3: Create `src/components/RecipientPicker.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Select from "primevue/select";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useRecipientsQuery } from "@/api/queries";
import { formatAddressLine } from "@/lib/format";
import RecipientQuickCreate from "./RecipientQuickCreate.vue";

const props = defineProps<{ inputId?: string; defaultToNewest?: boolean }>();
const model = defineModel<number | null>({ required: true });

const { t } = useI18n();
const recipients = useRecipientsQuery(ref(""));
const quickOpen = ref(false);

// A fresh draft starts on the newest recipient, as in the UI5 app. Taken from
// the same list the Select shows, so the default is always one it can display.
watch(
  () => recipients.data.value,
  (list) => {
    if (props.defaultToNewest && model.value == null && list?.length) {
      model.value = list[0].Id;
    }
  },
  { immediate: true },
);

function onCreated(id: number): void {
  model.value = id;
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <Select
      v-model="model"
      :input-id="inputId"
      :options="recipients.data.value ?? []"
      option-value="Id"
      option-label="Name"
      filter
      :filter-fields="['Name', 'Address', 'Number']"
      :placeholder="t('selectRecipient')"
      :loading="recipients.isFetching.value"
      class="min-w-0 flex-1 basis-64"
    >
      <template #option="{ option }">
        <div class="flex flex-col">
          <span>{{ option.Name }}</span>
          <small class="text-muted-color">{{ formatAddressLine(option.Address) }}</small>
        </div>
      </template>
    </Select>
    <Button :label="t('newRecipient')" icon="pi pi-user-plus" outlined @click="quickOpen = true" />
    <RecipientQuickCreate v-model:visible="quickOpen" @created="onCreated" />
  </div>
</template>
```

- [ ] **Step 4: Create `src/pages/RecipientsPage.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import type { Recipient } from "@/api/generated";
import { useRecipientsQuery } from "@/api/queries";
import PageHeader from "@/components/PageHeader.vue";
import RecipientDialog from "@/components/RecipientDialog.vue";
import { useDebounced } from "@/composables/useDebounced";

const { t } = useI18n();
const search = ref("");
const recipients = useRecipientsQuery(useDebounced(search));

const dialogOpen = ref(false);
const editing = ref<Recipient | null>(null);

function open(recipient: Recipient | null): void {
  editing.value = recipient;
  dialogOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuRecipients')">
    <template #actions>
      <Button :label="t('createRecipient')" icon="pi pi-plus" @click="open(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4">
        <IconField class="w-full md:w-80">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchByName')"
            :aria-label="t('searchByName')"
            class="w-full"
          />
        </IconField>
      </div>
      <DataTable
        :value="recipients.data.value ?? []"
        data-key="Id"
        :loading="recipients.isFetching.value"
        striped-rows
      >
        <template #empty>{{ t("noData") }}</template>
        <Column field="Number" :header="t('recipientNumber')" />
        <Column field="Name" :header="t('recipientName')" />
        <Column
          :header="t('recipientAddress')"
          header-class="hidden md:table-cell"
          body-class="hidden whitespace-pre-line md:table-cell"
        >
          <template #body="{ data }">{{ data.Address }}</template>
        </Column>
        <Column class="w-16 text-right">
          <template #body="{ data }">
            <Button
              icon="pi pi-pencil"
              text
              rounded
              :aria-label="t('edit')"
              v-tooltip.left="t('edit')"
              @click="open(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <RecipientDialog v-model:visible="dialogOpen" :recipient="editing" />
</template>
```

- [ ] **Step 5: Register the route** — `{ path: "/recipients", component: () => import("@/pages/RecipientsPage.vue") },`

- [ ] **Step 6: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081), `/recipients`:

- Create with an empty number → a number is assigned; create with an existing number → "Diese Empfängernummer existiert bereits."; empty name → "Der Name des Empfängers fehlt."
- A two-line address shows on two lines in the table; editing keeps both lines.
- Search by part of a name narrows the list.
- `RecipientPicker` is checked in Task 9.

- [ ] **Step 7: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the recipients page and recipient pickers"
```

---

### Task 8: Line editor and invoice row components

**Files:**

- Create: `src/components/LineEditor.vue`, `src/components/PaidStatusTag.vue`, `src/components/InvoiceRowActions.vue`

**Interfaces:**

- Consumes: `DraftLine`, `newLine` (Task 3), `useItemsQuery`, `openDocument`, `usePaidToggle`, `useErrorToast`, `formatAmount`.
- Produces:
  - `<LineEditor v-model="DraftLine[]" />` — one row per line: item Select (filterable on number and description), description, count, price (up to 4 decimals, placeholder = the item's default price), "inkl. MwSt." checkbox, delete; "Zeile hinzufügen"; an approximate net/gross footer.
  - `<PaidStatusTag :paid-on="string | null" />`
  - `<InvoiceRowActions :invoice-id :paid-on :details-to @edit />` — details, PDF, copy, edit (disabled + tooltip when paid), mark paid/unpaid; below `md` everything except details goes into a "⋯" menu.

- [ ] **Step 1: Create `src/components/LineEditor.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Checkbox from "primevue/checkbox";
import InputNumber from "primevue/inputnumber";
import Select from "primevue/select";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { InvoiceItem } from "@/api/generated";
import { useItemsQuery } from "@/api/queries";
import { formatAmount } from "@/lib/format";
import { type DraftLine, newLine } from "@/lib/lines";

const lines = defineModel<DraftLine[]>({ required: true });

const { t } = useI18n();
const items = useItemsQuery(ref(""));
const byId = computed(() => new Map((items.data.value ?? []).map((item) => [item.Id, item])));

function item(line: DraftLine): InvoiceItem | undefined {
  return line.itemId == null ? undefined : byId.value.get(line.itemId);
}

/** A picked item fills in its default price, or clears the field when it has
 *  none -- the same as the UI5 dialogs. */
function onItemPicked(line: DraftLine): void {
  line.unitPrice = item(line)?.DefaultPrice ?? null;
}

function addLine(): void {
  lines.value = [...lines.value, newLine()];
}

function removeLine(key: number): void {
  lines.value = lines.value.filter((line) => line.key !== key);
}

/** An estimate while typing; the invoice's own totals come from the backend. */
const totals = computed(() => {
  let net = 0;
  let gross = 0;
  for (const line of lines.value) {
    const rate = item(line)?.VatRate;
    const price = line.unitPrice ?? item(line)?.DefaultPrice ?? null;
    if (rate == null || price == null || line.count == null) {
      continue;
    }
    const amount = line.count * price;
    if (line.priceIncludesVat) {
      gross += amount;
      net += amount / (1 + rate / 100);
    } else {
      net += amount;
      gross += amount * (1 + rate / 100);
    }
  }
  return { net: Math.round(net * 100) / 100, gross: Math.round(gross * 100) / 100 };
});
</script>

<template>
  <div class="flex flex-col gap-3">
    <div
      class="hidden gap-3 px-1 text-sm font-medium text-muted-color md:grid md:grid-cols-[minmax(14rem,1fr)_6rem_10rem_8rem_2.5rem]"
      aria-hidden="true"
    >
      <span>{{ t("itemNumber") }}</span>
      <span>{{ t("itemCount") }}</span>
      <span>{{ t("itemPrice") }}</span>
      <span>{{ t("priceIncludesVatShort") }}</span>
      <span />
    </div>

    <div
      v-for="(line, index) in lines"
      :key="line.key"
      class="grid grid-cols-2 gap-3 rounded-lg border border-surface p-3 md:grid-cols-[minmax(14rem,1fr)_6rem_10rem_8rem_2.5rem] md:items-start md:border-0 md:p-1"
    >
      <div class="col-span-2 flex flex-col gap-1 md:col-span-1">
        <Select
          v-model="line.itemId"
          :options="items.data.value ?? []"
          option-value="Id"
          option-label="Number"
          filter
          :filter-fields="['Number', 'Description']"
          :placeholder="t('selectItem')"
          :aria-label="`${t('itemNumber')} ${index + 1}`"
          fluid
          @change="onItemPicked(line)"
        >
          <template #option="{ option }">
            <span class="font-medium">{{ option.Number }}</span>
            <span class="ml-2 text-muted-color">{{ option.Description }}</span>
          </template>
        </Select>
        <small v-if="item(line)" class="px-1 text-muted-color">{{ item(line)!.Description }}</small>
      </div>
      <InputNumber
        v-model="line.count"
        :min="0"
        :use-grouping="false"
        :aria-label="`${t('itemCount')} ${index + 1}`"
        fluid
      />
      <!-- Up to four decimals: a grave invoice reopens net prices kept at three
           on purpose, and rounding them here would change an untouched line. -->
      <InputNumber
        v-model="line.unitPrice"
        locale="de-DE"
        :min-fraction-digits="2"
        :max-fraction-digits="4"
        :placeholder="
          item(line)?.DefaultPrice != null ? formatAmount(item(line)!.DefaultPrice) : ''
        "
        :aria-label="`${t('itemPrice')} ${index + 1}`"
        fluid
      />
      <label class="flex items-center gap-2 md:pt-2">
        <Checkbox v-model="line.priceIncludesVat" binary />
        <span class="text-sm md:sr-only">{{ t("priceIncludesVatShort") }}</span>
      </label>
      <Button
        icon="pi pi-trash"
        severity="danger"
        text
        rounded
        class="justify-self-end"
        :aria-label="t('deleteLine')"
        v-tooltip.left="t('deleteLine')"
        @click="removeLine(line.key)"
      />
    </div>

    <div class="flex flex-wrap items-center justify-between gap-3">
      <Button :label="t('addLine')" icon="pi pi-plus" outlined size="small" @click="addLine" />
      <span class="text-sm text-muted-color tabular-nums">
        {{ t("linesTotal", [formatAmount(totals.net), formatAmount(totals.gross)]) }}
      </span>
    </div>
  </div>
</template>
```

`lines` rows are edited in place (`line.itemId = …`); that mutates the parent's draft objects, which is intended — the parent owns the draft and reads it on save.

- [ ] **Step 2: Create `src/components/PaidStatusTag.vue`**

```vue
<script setup lang="ts">
import Tag from "primevue/tag";
import { useI18n } from "vue-i18n";

defineProps<{ paidOn: string | null }>();
const { t } = useI18n();
</script>

<template>
  <!-- the date is recorded, never shown: set means paid, null unpaid -->
  <Tag
    :value="paidOn ? t('statusPaid') : t('statusUnpaid')"
    :severity="paidOn ? 'success' : 'warn'"
    :aria-label="paidOn ? t('statusPaidAnnouncement') : t('statusUnpaidAnnouncement')"
  />
</template>
```

- [ ] **Step 3: Create `src/components/InvoiceRowActions.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Menu from "primevue/menu";
import type { MenuItem } from "primevue/menuitem";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { type RouteLocationRaw, useRouter } from "vue-router";
import type { DocumentKindName } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { openDocument } from "@/lib/download";

const props = defineProps<{
  invoiceId: number;
  paidOn: string | null;
  detailsTo: RouteLocationRaw;
}>();
const emit = defineEmits<{ edit: [] }>();

const { t } = useI18n();
const router = useRouter();
const { showError } = useErrorToast();
const togglePaid = usePaidToggle();
const menu = ref<InstanceType<typeof Menu>>();

function open(kind: DocumentKindName): void {
  // not awaited before window.open: openDocument opens the tab synchronously
  openDocument(props.invoiceId, kind).catch(showError);
}

const menuItems = computed<MenuItem[]>(() => [
  { label: t("actionDownloadNormal"), icon: "pi pi-file-pdf", command: () => open("Normal") },
  { label: t("actionDownloadCopy"), icon: "pi pi-copy", command: () => open("Copy") },
  {
    label: props.paidOn ? t("editBlockedPaid") : t("actionEdit"),
    icon: "pi pi-pencil",
    disabled: !!props.paidOn,
    command: () => emit("edit"),
  },
  {
    label: props.paidOn ? t("actionMarkUnpaid") : t("actionMarkPaid"),
    icon: props.paidOn ? "pi pi-undo" : "pi pi-wallet",
    command: () => void togglePaid(props.invoiceId, props.paidOn),
  },
]);
</script>

<template>
  <div class="flex items-center justify-end gap-0.5">
    <Button
      icon="pi pi-eye"
      text
      rounded
      :aria-label="t('actionDetails')"
      v-tooltip.top="t('actionDetails')"
      @click="router.push(detailsTo)"
    />
    <div class="hidden items-center gap-0.5 md:flex">
      <Button
        icon="pi pi-file-pdf"
        text
        rounded
        :aria-label="t('actionDownloadNormal')"
        v-tooltip.top="t('actionDownloadNormal')"
        @click="open('Normal')"
      />
      <Button
        icon="pi pi-copy"
        text
        rounded
        :aria-label="t('actionDownloadCopy')"
        v-tooltip.top="t('actionDownloadCopy')"
        @click="open('Copy')"
      />
      <!-- a disabled button fires no hover, so the tooltip sits on a wrapper -->
      <span v-tooltip.top="paidOn ? t('editBlockedPaid') : t('actionEdit')">
        <Button
          icon="pi pi-pencil"
          text
          rounded
          :disabled="!!paidOn"
          :aria-label="t('actionEdit')"
          @click="emit('edit')"
        />
      </span>
      <Button
        :icon="paidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        text
        rounded
        :severity="paidOn ? 'secondary' : 'success'"
        :aria-label="paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        v-tooltip.top="paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        @click="togglePaid(invoiceId, paidOn)"
      />
    </div>
    <Button
      class="md:hidden"
      icon="pi pi-ellipsis-v"
      text
      rounded
      :aria-label="t('moreActions')"
      aria-haspopup="true"
      @click="menu?.toggle($event)"
    />
    <Menu ref="menu" :model="menuItems" popup />
  </div>
</template>
```

- [ ] **Step 4: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0. (These components are exercised from Task 9 on.)

- [ ] **Step 5: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the line editor and invoice row components"
```

---

### Task 9: Invoices list and invoice editor

**Files:**

- Create: `src/components/InvoiceEditor.vue`, `src/pages/InvoicesPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `odata.Invoices()`, `createInvoice`, `createRevision`, `fetchInvoiceLines`, `loadItemVatRates`, `priceFieldValue`, `newLine`, `toLineInputs`, `LineEditor`, `RecipientPicker`, `PaidStatusTag`, `InvoiceRowActions`, `useBlocking`, `useErrorToast`, `useDebounced`.
- Produces: `<InvoiceEditor v-model:visible :target="{ invoiceId: number; recipientId: number } | null" />` — `null` creates an invoice, a target revises that invoice.

Behaviour reference: `frontend/src/view/Invoices.view.xml`, `frontend/src/view/fragments/InvoiceDialog.fragment.xml`, `frontend/src/controller/Invoices.controller.ts`, `InvoiceEdit.controller.ts`. Differences by spec: title "Rechnung bearbeiten" when revising; lines can be deleted; server-side paging, 50 per page.

- [ ] **Step 1: Create `src/components/InvoiceEditor.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import ProgressSpinner from "primevue/progressspinner";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  createInvoice,
  createRevision,
  fetchInvoiceLines,
  type InvoiceLineInput,
} from "@/api/endpoints";
import { loadItemVatRates } from "@/api/queries";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import { priceFieldValue } from "@/lib/money";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ target: { invoiceId: number; recipientId: number } | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const recipientId = ref<number | null>(null);
const lines = ref<DraftLine[]>([]);
const loading = ref(false);

watch(visible, async (open) => {
  if (!open) {
    return;
  }
  recipientId.value = props.target?.recipientId ?? null;
  lines.value = [newLine()];
  if (!props.target) {
    return;
  }
  loading.value = true;
  try {
    const [existing, rates] = await Promise.all([
      fetchInvoiceLines(props.target.invoiceId),
      loadItemVatRates(queryClient),
    ]);
    lines.value = existing
      .slice()
      .sort((a, b) => a.LineNumber - b.LineNumber)
      .map((line) =>
        newLine({
          itemId: line.ItemId,
          count: line.Count,
          // the line stores a net; one entered VAT-included is edited as its
          // gross, converted with the item's current rate (what the backend uses)
          unitPrice: priceFieldValue(
            line.UnitPrice,
            line.PriceIncludesVat,
            (line.ItemId != null ? rates.get(line.ItemId) : undefined) ?? line.VatRate,
          ),
          priceIncludesVat: line.PriceIncludesVat,
        }),
      );
  } catch (error) {
    showError(error);
    visible.value = false;
  } finally {
    loading.value = false;
  }
});

async function save(): Promise<void> {
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  const target = props.target;
  const recipient = recipientId.value ?? 0;
  try {
    await run(t("invoiceRunning"), () =>
      target
        ? createRevision(target.invoiceId, recipient, inputs)
        : createInvoice(recipient, inputs),
    );
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    :header="target ? t('editInvoice') : t('createInvoice')"
    :style="{ width: 'min(100vw, 60rem)' }"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form v-else id="invoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="invoiceRecipient" class="font-medium">{{ t("menuRecipients") }}</label>
        <RecipientPicker
          v-model="recipientId"
          input-id="invoiceRecipient"
          :default-to-newest="!target"
        />
      </div>
      <div class="flex flex-col gap-2">
        <span class="font-medium">{{ t("menuItems") }}</span>
        <LineEditor v-model="lines" />
      </div>
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
        <Button
          type="submit"
          form="invoiceForm"
          :label="t('save')"
          icon="pi pi-check"
          :disabled="loading"
        />
      </div>
    </template>
  </Drawer>
</template>
```

- [ ] **Step 2: Create `src/pages/InvoicesPage.vue`**

```vue
<script setup lang="ts">
import { keepPreviousData, useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Tag from "primevue/tag";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Invoice } from "@/api/generated";
import { odata } from "@/api/odata";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import InvoiceRowActions from "@/components/InvoiceRowActions.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import { useDebounced } from "@/composables/useDebounced";
import { formatDate, formatInvoiceNumber, formatRevisionBadge } from "@/lib/format";

const PAGE_SIZE = 50;

const { t } = useI18n();
const search = ref("");
const term = useDebounced(search);
const payment = ref<"all" | "unpaid" | "paid">("all");
const first = ref(0);

// a new search or filter starts on page one (Review Focus 5)
watch([term, payment], () => {
  first.value = 0;
});

const paymentOptions = computed(() => [
  { value: "all", label: t("paymentFilterAll") },
  { value: "unpaid", label: t("paymentFilterUnpaid") },
  { value: "paid", label: t("paymentFilterPaid") },
]);

const invoices = useQuery({
  queryKey: ["invoices", "list", term, payment, first],
  queryFn: async () => {
    const searchTerm = term.value.trim();
    const response = await odata
      .Invoices()
      .query((b, q) => {
        // ordinary invoices only; grave invoices are reached through their grave
        let filter = q.GraveId.isNull().and(q.IsLatestRevision.isTrue());
        if (payment.value === "unpaid") {
          filter = filter.and(q.PaidOn.isNull());
        } else if (payment.value === "paid") {
          filter = filter.and(q.PaidOn.isNotNull());
        }
        b.filter(filter).orderBy(q.Id.desc()).skip(first.value).top(PAGE_SIZE).count();
      })
      // `search` is the backend's own query option: number, recipient, date, total
      .execute(searchTerm ? { params: { search: searchTerm } } : undefined);
    return { rows: response.data.value, total: response.data["@odata.count"] ?? 0 };
  },
  placeholderData: keepPreviousData,
});

const editorOpen = ref(false);
const editorTarget = ref<{ invoiceId: number; recipientId: number } | null>(null);

function openEditor(invoice: Invoice | null): void {
  editorTarget.value = invoice ? { invoiceId: invoice.Id, recipientId: invoice.RecipientId } : null;
  editorOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuInvoices')">
    <template #actions>
      <Button :label="t('createInvoice')" icon="pi pi-plus" @click="openEditor(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <IconField class="w-full md:w-80">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchInvoice')"
            :aria-label="t('searchInvoice')"
            class="w-full"
          />
        </IconField>
        <Select
          v-model="payment"
          :options="paymentOptions"
          option-label="label"
          option-value="value"
          :aria-label="t('paymentFilter')"
          class="w-full sm:w-56"
        />
      </div>

      <DataTable
        :value="invoices.data.value?.rows ?? []"
        data-key="Id"
        lazy
        paginator
        :rows="PAGE_SIZE"
        :first="first"
        :total-records="invoices.data.value?.total ?? 0"
        :loading="invoices.isFetching.value"
        striped-rows
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('invoiceNumber')">
          <template #body="{ data }">
            <span class="font-medium tabular-nums">{{
              formatInvoiceNumber(data.InvoiceNumber)
            }}</span>
            <Tag
              v-if="data.RevisionNumber > 0"
              :value="formatRevisionBadge(data.RevisionNumber)"
              severity="secondary"
              class="ml-2"
              v-tooltip.top="t('revision')"
            />
          </template>
        </Column>
        <Column field="RecipientName" :header="t('recipientName')" />
        <Column
          :header="t('invoiceDate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.GeneratedAt) }}</template>
        </Column>
        <Column :header="t('paidStatus')">
          <template #body="{ data }"><PaidStatusTag :paid-on="data.PaidOn" /></template>
        </Column>
        <Column class="w-0 whitespace-nowrap">
          <template #body="{ data }">
            <InvoiceRowActions
              :invoice-id="data.Id"
              :paid-on="data.PaidOn"
              :details-to="`/invoices/${data.RootId}`"
              @edit="openEditor(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" />
</template>
```

- [ ] **Step 3: Register the route** — `{ path: "/invoices", component: () => import("@/pages/InvoicesPage.vue") },`

- [ ] **Step 4: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081; items and recipients from Tasks 6–7 exist):

- "Rechnung erstellen": the recipient preselects the newest recipient. "Neuer Empfänger" creates one and selects it; the drawer stays open.
- A line with no item → "Bitte wählen Sie in jeder Zeile eine Leistung aus.", no request sent.
- Picking `L1` (a default price) fills the price; picking an item without one leaves it empty and the placeholder is blank. Save → the blocking overlay "Rechnung wird erstellt …" shows, the drawer closes, the invoice is on top of the list.
- A line with an empty price for an item without a default → the overlay closes, then "Zu dieser Leistung fehlt ein Preis." (Review Focus 4).
- PDF and copy open in new tabs (allow popups for localhost:8082 if the browser asks once; no blocked-popup warning afterwards).
- Mark paid → green tag; the edit button is disabled with the "bezahlt" tooltip; take the mark back → confirmation first.
- Edit an unpaid invoice → title "Rechnung bearbeiten", existing lines loaded; a line entered "inkl. MwSt." shows its gross; save → the row shows the R1 badge.
- Create 51 invoices: `for i in $(seq 1 51); do curl -s -X POST http://localhost:8081/odata/CreateInvoice -H 'Content-Type: application/json' -d '{"recipientId":<id>,"lines":[{"itemId":<id>,"count":1,"unitPrice":10,"priceIncludesVat":false}]}' >/dev/null; done` (ids from `curl -s 'http://localhost:8081/odata/Recipients?$top=1'` and `.../InvoiceItems?$top=1`). The paginator shows 2 pages; go to page 2, then type a search → back on page 1 and the total reflects the search; switch the payment filter → page 1 (Review Focus 5).
- At 390 px width: the date column is hidden; row actions collapse into "⋯" with PDF, copy, edit, paid.

- [ ] **Step 5: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the invoice list and invoice editor"
```

---

### Task 10: Invoice detail page with the revision history

**Files:**

- Create: `src/components/RevisionPanel.vue`, `src/components/RevisionList.vue`, `src/pages/InvoiceDetailPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `loadChain`, `buildRevisions`, `RevisionView`, `openDocument`, `usePaidToggle`, `InvoiceEditor`, `PaidStatusTag`, `PageHeader`, formatters.
- Produces: `<RevisionList :chain="Invoice[]" />` (Accordion, newest open) — reused by Task 14.

Behaviour reference: `frontend/src/view/InvoiceDetail.view.xml`, `frontend/src/view/fragments/RevisionPanel.fragment.xml`, `InvoiceDetail.controller.ts`, `InvoiceRevisions.controller.ts`.

- [ ] **Step 1: Create `src/components/RevisionPanel.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { useI18n } from "vue-i18n";
import type { DocumentKindName } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";
import { openDocument } from "@/lib/download";
import { formatAmount, formatVatRate } from "@/lib/format";
import type { RevisionView } from "@/lib/revisions";

const props = defineProps<{ revision: RevisionView }>();
const { t } = useI18n();
const { showError } = useErrorToast();

function open(kind: DocumentKindName): void {
  openDocument(props.revision.Id, kind).catch(showError);
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap gap-2">
      <Button
        :label="t('actionDownloadNormal')"
        icon="pi pi-file-pdf"
        size="small"
        outlined
        @click="open('Normal')"
      />
      <Button
        :label="t('actionDownloadCopy')"
        icon="pi pi-copy"
        size="small"
        outlined
        @click="open('Copy')"
      />
    </div>

    <DataTable :value="revision.Lines" size="small">
      <Column field="ItemNumber" :header="t('itemNumber')" />
      <Column
        field="Description"
        :header="t('itemDescription')"
        header-class="hidden sm:table-cell"
        body-class="hidden sm:table-cell"
      />
      <Column field="Count" :header="t('itemCount')" body-class="text-right tabular-nums" />
      <Column :header="t('itemPrice')" body-class="text-right tabular-nums">
        <template #body="{ data }">{{ formatAmount(data.UnitPrice) }}</template>
      </Column>
      <Column :header="t('netAmount')" body-class="text-right tabular-nums">
        <template #body="{ data }">{{ formatAmount(data.LineTotal) }}</template>
      </Column>
      <Column :header="t('grossAmount')" body-class="text-right tabular-nums">
        <template #body="{ data }">{{ formatAmount(data.GrossTotal) }}</template>
      </Column>
      <Column
        :header="t('vatRate')"
        header-class="hidden md:table-cell"
        body-class="hidden md:table-cell text-right"
      >
        <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
      </Column>
      <Column
        :header="t('priceIncludesVatColumn')"
        header-class="hidden md:table-cell"
        body-class="hidden md:table-cell"
      >
        <template #body="{ data }">{{ data.PriceIncludesVat ? t("yes") : t("no") }}</template>
      </Column>
    </DataTable>

    <dl class="ml-auto grid w-full max-w-xs grid-cols-[1fr_auto] gap-x-6 gap-y-1 tabular-nums">
      <dt class="text-muted-color">{{ t("netTotal") }}</dt>
      <dd class="m-0 text-right">{{ formatAmount(revision.NetTotal) }}</dd>
      <dt class="text-muted-color">{{ t("vatTotal") }}</dt>
      <dd class="m-0 text-right">{{ formatAmount(revision.VatTotal) }}</dd>
      <dt class="font-semibold">{{ t("grossTotal") }}</dt>
      <dd class="m-0 text-right font-semibold">{{ formatAmount(revision.GrossTotal) }}</dd>
    </dl>
  </div>
</template>
```

- [ ] **Step 2: Create `src/components/RevisionList.vue`**

```vue
<script setup lang="ts">
import Accordion from "primevue/accordion";
import AccordionContent from "primevue/accordioncontent";
import AccordionHeader from "primevue/accordionheader";
import AccordionPanel from "primevue/accordionpanel";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import type { Invoice } from "@/api/generated";
import { formatDate } from "@/lib/format";
import { buildRevisions } from "@/lib/revisions";
import RevisionPanel from "./RevisionPanel.vue";

const props = defineProps<{ chain: Invoice[] }>();
const { t } = useI18n();

const revisions = computed(() => buildRevisions(props.chain));
// the current revision opens; the history stays collapsed
const opened = computed(() => (revisions.value.length ? [String(revisions.value[0].Id)] : []));
</script>

<template>
  <Accordion :value="opened" multiple>
    <AccordionPanel v-for="revision in revisions" :key="revision.Id" :value="String(revision.Id)">
      <AccordionHeader>
        <span class="flex flex-wrap items-baseline gap-x-3">
          <span class="font-semibold">
            {{
              revision.RevisionNumber === 0
                ? t("revisionOriginal")
                : t("revisionLabel", [revision.RevisionNumber])
            }}
          </span>
          <span class="text-sm text-muted-color tabular-nums">
            {{ revision.InvoiceNumber }} · {{ formatDate(revision.GeneratedAt) }}
          </span>
        </span>
      </AccordionHeader>
      <AccordionContent>
        <RevisionPanel :revision="revision" />
      </AccordionContent>
    </AccordionPanel>
  </Accordion>
</template>
```

- [ ] **Step 3: Create `src/pages/InvoiceDetailPage.vue`**

```vue
<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, ref, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { loadChain } from "@/api/queries";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { formatInvoiceNumber } from "@/lib/format";

const props = defineProps<{ rootId: number }>();
const { t } = useI18n();
const togglePaid = usePaidToggle();

const chain = useQuery({
  queryKey: ["invoices", "chain", toRef(props, "rootId")],
  queryFn: () => loadChain(props.rootId),
});
const latest = computed(() => chain.data.value?.[0]);
const number = computed(() => formatInvoiceNumber(latest.value?.InvoiceNumber));

const editorOpen = ref(false);
</script>

<template>
  <PageHeader
    :title="latest ? number : t('invoiceDetailTitle')"
    :crumbs="[
      { label: t('menuInvoices'), to: '/invoices' },
      { label: number || t('invoiceDetailTitle') },
    ]"
  >
    <template v-if="latest" #actions>
      <PaidStatusTag :paid-on="latest.PaidOn" />
      <Button
        :icon="latest.PaidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        :label="latest.PaidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        severity="secondary"
        outlined
        @click="togglePaid(latest.Id, latest.PaidOn)"
      />
      <span v-tooltip.bottom="latest.PaidOn ? t('editBlockedPaid') : undefined">
        <Button
          :label="t('actionEdit')"
          icon="pi pi-pencil"
          :disabled="!!latest.PaidOn"
          @click="editorOpen = true"
        />
      </span>
    </template>
  </PageHeader>

  <div v-if="chain.isPending.value" class="flex justify-center p-10"><ProgressSpinner /></div>
  <Message v-else-if="!latest" severity="warn">{{ t("notFound") }}</Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <div class="text-lg font-semibold">{{ latest.RecipientName }}</div>
        <div class="whitespace-pre-line text-muted-color">{{ latest.RecipientAddress }}</div>
      </template>
    </Card>
    <RevisionList :chain="chain.data.value ?? []" />
  </div>

  <InvoiceEditor
    v-if="latest"
    v-model:visible="editorOpen"
    :target="{ invoiceId: latest.Id, recipientId: latest.RecipientId }"
  />
</template>
```

- [ ] **Step 4: Register the route**

```ts
  {
    path: "/invoices/:rootId",
    component: () => import("@/pages/InvoiceDetailPage.vue"),
    props: (route) => ({ rootId: Number(route.params.rootId) }),
  },
```

- [ ] **Step 5: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081), using the revised invoice from Task 9:

- Details from the list opens `/invoices/<rootId>`: "Revision 1" open, "Original" collapsed; both panels' PDF and copy open the right documents.
- Netto and Brutto per line, and the totals, equal the UI5 page `http://localhost:8081/#/invoices/<rootId>` for the same invoice, to the cent. Add a line at 7 % and one at 19 % to see the per-rate split.
- Typing a revision's own id into the URL shows the same chain; an unknown id shows "Nichts gefunden."
- Edit from the header → drawer; save → a third panel appears, newest open. Paid toggle updates the tag.

- [ ] **Step 6: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the invoice detail page"
```

---

### Task 11: Grave editor

**Files:**

- Create: `src/components/GraveEditor.vue`

**Interfaces:**

- Consumes: `fetchGraveDetails`, `saveGrave`, `checkGraveBilling`, `billingProblemText`, `showMessage`, `toIsoDate`, `parseIsoDate`, `newLine`, `toLineInputs`, `LineEditor`, `RecipientPicker`, `useRecipientsQuery`, `ApiError`.
- Produces: `<GraveEditor v-model:visible :grave-id="number | null" />`.

Behaviour reference: `frontend/src/view/fragments/GraveDialog.fragment.xml`, `frontend/src/controller/GraveEdit.controller.ts` (`openGraveDialog`, `onGraveDialogSave`, `checkBilling`). A grave save is not a long-running task in the UI5 app (no busy dialog); the save button shows a spinner instead.

- [ ] **Step 1: Create `src/components/GraveEditor.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import DatePicker from "primevue/datepicker";
import Drawer from "primevue/drawer";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  checkGraveBilling,
  fetchGraveDetails,
  type GraveBillingCheck,
  type InvoiceLineInput,
  saveGrave,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import { useRecipientsQuery } from "@/api/queries";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { billingProblemText } from "@/lib/billing";
import { parseIsoDate, toIsoDate, type Translate } from "@/lib/format";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ graveId: number | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const allRecipients = useRecipientsQuery(ref(""));

const form = reactive({
  GraveNumber: "",
  StartDate: null as Date | null,
  EndDate: null as Date | null,
});
const lines = ref<DraftLine[]>([]);
const recipients = ref<{ id: number; name: string }[]>([]);
const linkRecipientId = ref<number | null>(null);
const loading = ref(false);
const saving = ref(false);

watch(visible, async (open) => {
  if (!open) {
    return;
  }
  Object.assign(form, { GraveNumber: "", StartDate: null, EndDate: null });
  lines.value = [];
  recipients.value = [];
  linkRecipientId.value = null;
  if (props.graveId == null) {
    return;
  }
  loading.value = true;
  try {
    const grave = await fetchGraveDetails(props.graveId);
    Object.assign(form, {
      GraveNumber: grave.GraveNumber,
      StartDate: parseIsoDate(grave.StartDate),
      EndDate: parseIsoDate(grave.EndDate),
    });
    // a link price is shown as stored -- already the gross on a VAT-included line
    lines.value = grave.Items.map((link) =>
      newLine({
        itemId: link.Item.Id,
        count: link.Count,
        unitPrice: link.UnitPrice,
        priceIncludesVat: link.PriceIncludesVat,
      }),
    );
    recipients.value = grave.Recipients.map((link) => ({
      id: link.Recipient.Id,
      name: link.Recipient.Name,
    }));
  } catch (error) {
    showError(error);
    visible.value = false;
  } finally {
    loading.value = false;
  }
});

function addRecipient(): void {
  const id = linkRecipientId.value;
  if (id == null) {
    return;
  }
  if (recipients.value.some((recipient) => recipient.id === id)) {
    showError(new ApiError("GraveRecipientLinkExists"));
    return;
  }
  const name =
    allRecipients.data.value?.find((recipient) => recipient.Id === id)?.Name ?? String(id);
  recipients.value = [...recipients.value, { id, name }];
}

function removeRecipient(id: number): void {
  recipients.value = recipients.value.filter((recipient) => recipient.id !== id);
}

/** The bill is split equally, so each VAT rate has to divide. Asking before
 *  the save turns a refusal into a message that names the rate and what would
 *  work. A check that cannot be reached does not block: the save validates
 *  again. */
async function billingProblems(inputs: InvoiceLineInput[]): Promise<string | null> {
  const count = recipients.value.length;
  if (count === 0 || inputs.length === 0) {
    return null;
  }
  let check: GraveBillingCheck;
  try {
    check = await checkGraveBilling(count, inputs);
  } catch {
    return null;
  }
  return check.Ok ? null : billingProblemText(check.Problems, count, translate);
}

async function save(): Promise<void> {
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  saving.value = true;
  try {
    const problems = await billingProblems(inputs);
    if (problems) {
      showMessage(t("pricesDoNotDivideTitle"), problems);
      return;
    }
    await saveGrave({
      graveId: props.graveId,
      graveNumber: form.GraveNumber,
      // local calendar day, not toISOString (Review Focus 3)
      startDate: toIsoDate(form.StartDate),
      endDate: toIsoDate(form.EndDate),
      items: inputs,
      recipientIds: recipients.value.map((recipient) => recipient.id),
    });
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    :header="graveId == null ? t('createGrave') : t('editGrave')"
    :style="{ width: 'min(100vw, 60rem)' }"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form v-else id="graveForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="graveId != null" severity="warn">{{ t("graveEditWarning") }}</Message>

      <div class="grid gap-4 sm:grid-cols-3">
        <div class="flex flex-col gap-2">
          <label for="graveNumber" class="font-medium">{{ t("graveNumber") }}</label>
          <InputText id="graveNumber" v-model="form.GraveNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveStart" class="font-medium">{{ t("startDate") }}</label>
          <DatePicker
            v-model="form.StartDate"
            input-id="graveStart"
            date-format="dd.mm.yy"
            show-icon
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveEnd" class="font-medium">{{ t("endDate") }}</label>
          <DatePicker
            v-model="form.EndDate"
            input-id="graveEnd"
            date-format="dd.mm.yy"
            show-icon
            fluid
          />
        </div>
      </div>

      <section class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("graveItems") }}</h3>
        <LineEditor v-model="lines" />
      </section>

      <section class="flex flex-col gap-3">
        <h3 class="m-0 text-base font-semibold">{{ t("graveRecipients") }}</h3>
        <Message v-if="graveId != null" severity="info" size="small">{{
          t("graveRecipientsFixed")
        }}</Message>
        <ul class="m-0 flex list-none flex-col gap-1 p-0">
          <li
            v-for="recipient in recipients"
            :key="recipient.id"
            class="flex items-center justify-between rounded-lg border border-surface px-3 py-1.5"
          >
            <span>{{ recipient.name }}</span>
            <Button
              v-if="graveId == null"
              icon="pi pi-times"
              text
              rounded
              severity="danger"
              :aria-label="t('delete')"
              @click="removeRecipient(recipient.id)"
            />
          </li>
        </ul>
        <div v-if="graveId == null" class="flex flex-wrap items-center gap-2">
          <RecipientPicker
            v-model="linkRecipientId"
            input-id="graveRecipient"
            default-to-newest
            class="min-w-0 flex-1"
          />
          <Button :label="t('add')" icon="pi pi-plus" outlined @click="addRecipient" />
        </div>
      </section>
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
        <Button
          type="submit"
          form="graveForm"
          :label="t('save')"
          icon="pi pi-check"
          :loading="saving"
          :disabled="loading"
        />
      </div>
    </template>
  </Drawer>
</template>
```

- [ ] **Step 2: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0. (Exercised in Task 12.)

- [ ] **Step 3: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the grave editor"
```

---

### Task 12: Graves overview with generation and ZIP download

**Files:**

- Create: `src/components/GenerateDialog.vue`, `src/pages/GravesPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `fetchGraveStatus`, `searchGraves`, `bulkDownload`, `generateGraveInvoices`, `pollJob`, generated `JobStatus`, `generationSummary`, `downloadBlob`, half-year helpers, `useBlocking`, `showMessage`, `GraveEditor`.
- Produces: `<GenerateDialog v-model:visible :initial-year :initial-half @generate="(year, half) => …" />`.

Behaviour reference: `frontend/src/view/Graves.view.xml`, `frontend/src/view/fragments/GenerateDialog.fragment.xml`, `frontend/src/controller/Graves.controller.ts`. Difference by spec: the generate dialog starts on the half-year selected in the toolbar (so its year list covers the toolbar's range).

- [ ] **Step 1: Create `src/components/GenerateDialog.vue`**

```vue
<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import Select from "primevue/select";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { yearOptions } from "@/lib/halfYear";

const props = defineProps<{ initialYear: number; initialHalf: number }>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ generate: [year: number, half: number] }>();

const { t } = useI18n();
// the same range as the overview's half-year select, which it starts on
const years = yearOptions(2, 1).map((option) => ({
  value: Number(option.key),
  label: option.text,
}));
const halves = [
  { value: 1, label: "H1" },
  { value: 2, label: "H2" },
];
const year = ref(props.initialYear);
const half = ref(props.initialHalf);

watch(visible, (open) => {
  if (open) {
    year.value = props.initialYear;
    half.value = props.initialHalf;
  }
});
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('generateInvoices')"
    :style="{ width: '22rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <div class="grid grid-cols-2 gap-4">
      <div class="flex flex-col gap-2">
        <label for="generateYear" class="font-medium">{{ t("year") }}</label>
        <Select
          v-model="year"
          input-id="generateYear"
          :options="years"
          option-label="label"
          option-value="value"
        />
      </div>
      <div class="flex flex-col gap-2">
        <label for="generateHalf" class="font-medium">{{ t("halfYear") }}</label>
        <Select
          v-model="half"
          input-id="generateHalf"
          :options="halves"
          option-label="label"
          option-value="value"
        />
      </div>
    </div>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button :label="t('generateNow')" icon="pi pi-bolt" @click="emit('generate', year, half)" />
    </template>
  </Dialog>
</template>
```

- [ ] **Step 2: Create `src/pages/GravesPage.vue`**

```vue
<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Tag from "primevue/tag";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import {
  bulkDownload,
  fetchGraveStatus,
  generateGraveInvoices,
  type GraveStatusRow,
  pollJob,
  searchGraves,
} from "@/api/endpoints";
import { type Job, JobStatus } from "@/api/generated";
import GenerateDialog from "@/components/GenerateDialog.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useDebounced } from "@/composables/useDebounced";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";
import { formatDate, type Translate } from "@/lib/format";
import { generationSummary } from "@/lib/generation";
import { currentHalfYear, halfYearKey, halfYearOptions, parseHalfYearKey } from "@/lib/halfYear";

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const router = useRouter();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const halfYear = ref(halfYearKey(...currentHalfYear()));
const period = computed(() => parseHalfYearKey(halfYear.value));
const halfYearChoices = halfYearOptions(2, 1);
const payment = ref<"all" | "unpaid">("all");
const paymentOptions = computed(() => [
  { value: "all", label: t("paymentFilterAll") },
  { value: "unpaid", label: t("paymentFilterUnpaid") },
]);

const search = ref("");
const term = useDebounced(search);

// not-yet-invoiced graves first -- the server's order
const status = useQuery({
  queryKey: ["graves", "status", halfYear],
  queryFn: () => fetchGraveStatus(period.value[0], period.value[1]),
});
const searchResult = useQuery({
  queryKey: ["graves", "search", term],
  queryFn: () => searchGraves(term.value.trim()),
  enabled: computed(() => term.value.trim() !== ""),
});

const rows = computed(() => {
  let list = status.data.value ?? [];
  if (term.value.trim()) {
    const ids = new Set((searchResult.data.value ?? []).map((result) => result.Id));
    list = list.filter((row) => ids.has(row.Id));
  }
  if (payment.value === "unpaid") {
    list = list.filter((row) => row.InvoiceCount > 0 && row.PaidCount < row.InvoiceCount);
  }
  return list;
});

function paymentTag(
  row: GraveStatusRow,
): { value: string; severity: "success" | "warn"; label: string } | null {
  if (row.InvoiceCount === 0) {
    return null;
  }
  if (row.PaidCount === row.InvoiceCount) {
    return { value: t("statusPaid"), severity: "success", label: t("paymentPaidAnnouncement") };
  }
  if (row.PaidCount === 0) {
    return { value: t("statusUnpaid"), severity: "warn", label: t("paymentUnpaidAnnouncement") };
  }
  return {
    value: t("statusPartlyPaid", [row.PaidCount, row.InvoiceCount]),
    severity: "warn",
    label: t("paymentPartlyPaidAnnouncement"),
  };
}

const zipping = ref(false);
async function downloadZip(): Promise<void> {
  zipping.value = true;
  try {
    downloadBlob(await bulkDownload("Normal", period.value[0], period.value[1]), "rechnungen.zip");
  } catch (error) {
    showError(error);
  } finally {
    zipping.value = false;
  }
}

const generateOpen = ref(false);
async function generate(year: number, half: number): Promise<void> {
  generateOpen.value = false;
  let job: Job;
  try {
    job = await run(t("generationRunning"), async (progress) => {
      const started = await generateGraveInvoices(year, half);
      return pollJob(started.Id, (percent) =>
        progress(percent, t("generationProgress", [percent])),
      );
    });
  } catch (error) {
    showError(error);
    return;
  }
  await queryClient.invalidateQueries();
  if (job.Status === JobStatus.Failed) {
    showMessage(t("generateInvoices"), t("generationFailed"));
    return;
  }
  showMessage(t("generateInvoices"), generationSummary(job.ResultJson, translate), "info");
}

const editorOpen = ref(false);
const editorGraveId = ref<number | null>(null);
function openEditor(graveId: number | null): void {
  editorGraveId.value = graveId;
  editorOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuGraves')">
    <template #actions>
      <Button
        :label="t('bulkGraveInvoices')"
        icon="pi pi-download"
        severity="secondary"
        outlined
        :loading="zipping"
        @click="downloadZip"
      />
      <Button
        :label="t('generateInvoices')"
        icon="pi pi-bolt"
        severity="secondary"
        outlined
        @click="generateOpen = true"
      />
      <Button :label="t('createGrave')" icon="pi pi-plus" @click="openEditor(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <IconField class="w-full md:w-96">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchGrave')"
            :aria-label="t('searchGrave')"
            class="w-full"
          />
        </IconField>
        <Select
          v-model="halfYear"
          :options="halfYearChoices"
          option-label="text"
          option-value="key"
          :aria-label="t('halfYear')"
          class="w-full sm:w-40"
        />
        <Select
          v-model="payment"
          :options="paymentOptions"
          option-label="label"
          option-value="value"
          :aria-label="t('paymentFilter')"
          class="w-full sm:w-56"
        />
      </div>

      <DataTable :value="rows" data-key="Id" :loading="status.isFetching.value" striped-rows>
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('graveNumber')">
          <template #body="{ data }">
            <RouterLink :to="`/graves/${data.Id}`" class="font-medium text-primary hover:underline">
              {{ data.GraveNumber }}
            </RouterLink>
          </template>
        </Column>
        <Column
          :header="t('startDate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.StartDate) }}</template>
        </Column>
        <Column
          :header="t('endDate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.EndDate) }}</template>
        </Column>
        <Column :header="t('invoicingStatus')">
          <template #body="{ data }">
            <Tag
              :value="data.Invoiced ? t('statusInvoiced') : t('statusOpen')"
              :severity="data.Invoiced ? 'success' : 'danger'"
              :icon="data.Invoiced ? 'pi pi-check' : 'pi pi-exclamation-circle'"
              :aria-label="
                data.Invoiced ? t('statusInvoicedAnnouncement') : t('statusOpenAnnouncement')
              "
            />
          </template>
        </Column>
        <Column :header="t('paidStatus')">
          <template #body="{ data }">
            <Tag
              v-if="paymentTag(data)"
              :value="paymentTag(data)!.value"
              :severity="paymentTag(data)!.severity"
              :aria-label="paymentTag(data)!.label"
            />
          </template>
        </Column>
        <Column class="w-0 whitespace-nowrap">
          <template #body="{ data }">
            <div class="flex justify-end">
              <Button
                icon="pi pi-eye"
                text
                rounded
                :aria-label="t('actionDetails')"
                v-tooltip.top="t('actionDetails')"
                @click="router.push(`/graves/${data.Id}`)"
              />
              <Button
                icon="pi pi-pencil"
                text
                rounded
                :aria-label="t('edit')"
                v-tooltip.top="t('edit')"
                @click="openEditor(data.Id)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <GenerateDialog
    v-model:visible="generateOpen"
    :initial-year="period[0]"
    :initial-half="period[1]"
    @generate="generate"
  />
  <GraveEditor v-model:visible="editorOpen" :grave-id="editorGraveId" />
</template>
```

`halfYearKey(...currentHalfYear())`: if TypeScript rejects the tuple spread, write `const [year, half] = currentHalfYear();` and pass both.

- [ ] **Step 3: Register the route** — `{ path: "/graves", component: () => import("@/pages/GravesPage.vue") },`

- [ ] **Step 4: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081):

- **Grave editor, create:** number `G-1`, start **01.01.2026**, end **31.12.2030**, line `L1` × 1 at 10,00, recipients: the newest (preselected) → "Hinzufügen", then a second one; adding the same one twice → "Dieser Empfänger ist dem Grab bereits zugeordnet." Save → `G-1` appears as "offen".
  Reopen `G-1`: start still shows **01.01.2026** and `curl -s 'http://localhost:8081/odata/Graves?$filter=GraveNumber eq %27G-1%27'` shows `"StartDate":"2026-01-01"` (Review Focus 3).
- **Billing check:** new grave `G-2` with 2 recipients and one line at 10,01 € (19 %) → the modal "Preise gehen nicht auf" names 19 % and two nearby amounts; nothing is saved. Fix the price to 10,00 → saves.
- **Edit:** the warning and "Empfänger … nicht mehr geändert" note show; the recipient list has no remove buttons and no picker.
- **Item link:** open `L1` on `/items` → "Gräber mit dieser Leistung" lists G-1 and G-2 under "Standardpreis …" or "Eigener Preis …"; the VAT select is disabled; a grave link navigates to the grave.
- **Generate:** "Rechnungen erzeugen" defaults to the toolbar half-year → the overlay shows percent progress → a summary "Erstellt: 2 …"; both graves turn "erstellt" with "unbezahlt". Generating the same half-year again → "Für dieses Halbjahr wurden bereits Rechnungen erstellt." after the overlay closes.
- **Search** "G-2" and a recipient's name narrow the list; "Nur unbezahlte" keeps only graves with unpaid invoices. Switch the half-year → the status reloads.
- **ZIP:** "Rechnungen (ZIP)" downloads `rechnungen.zip` containing one PDF per recipient invoice.
- 390 px: start/end hidden, filters stack, no sideways scroll.

- [ ] **Step 5: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the graves overview with generation"
```

---

### Task 13: Grave detail page and grave-invoice editing

**Files:**

- Create: `src/composables/useGraveInvoiceEdit.ts`, `src/components/GraveInvoiceEditor.vue`, `src/pages/GraveDetailPage.vue`
- Modify: `src/api/queries.ts` (add `loadGraveDetail`), `src/router.ts`

**Interfaces:**

- Consumes: `fetchGraveDetails`, `fetchGraveInvoiceGroup`, `createGraveInvoiceRevision`, `odata.GraveInvoiceRuns()`, `odata.Invoices()`, `readAll`, `loadItemVatRates`, `priceFieldValue`, half-year helpers, `showMessage`, `useBlocking`, `LineEditor`, `InvoiceRowActions`, `GraveEditor`.
- Produces:
  - `loadGraveDetail(graveId): Promise<GraveDetailView>` with `GraveDetailView = { grave: GraveDetails; currentInvoiced: boolean; lines: GraveLineView[]; invoices: Invoice[] }` and `GraveLineView = { Number; Description; Count; Price; VatRate; PriceIncludesVat }`.
  - `useGraveInvoiceEdit(): { visible: Ref<boolean>; draft: Ref<GraveInvoiceDraft | null>; open(invoiceId: number): Promise<void> }`, `GraveInvoiceDraft = { invoiceId: number; warning: string; lines: DraftLine[] }`.
  - `<GraveInvoiceEditor v-model:visible :draft="GraveInvoiceDraft | null" />`.

Behaviour reference: `frontend/src/view/GraveDetail.view.xml`, `frontend/src/controller/GraveDetail.controller.ts` (`refresh`), `GraveEdit.controller.ts` (`openGraveInvoiceDialog`, grave-invoice save), `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`.

- [ ] **Step 1: Add `loadGraveDetail` to `src/api/queries.ts`**

Add these imports at the top: `import { currentHalfYear } from "@/lib/halfYear";`, `import { priceFieldValue } from "@/lib/money";`, `import { fetchGraveDetails, fetchGraveInvoiceGroup, type GraveDetails } from "./endpoints";`. Then append:

```ts
export interface GraveLineView {
  Number: string;
  Description: string;
  Count: number;
  Price: number;
  VatRate: number;
  PriceIncludesVat: boolean;
}

export interface GraveDetailView {
  grave: GraveDetails;
  /** Whether the current half-year has been invoiced for this grave. */
  currentInvoiced: boolean;
  lines: GraveLineView[];
  /** Latest revisions, newest half-year first, then by recipient. */
  invoices: Invoice[];
}

/** Port of GraveDetail.controller.ts refresh(). The lines are the current
 *  half-year's as invoiced when it has been, else the grave's configuration
 *  (link price, else the item's default, else 0 -- shown as stored). */
export async function loadGraveDetail(graveId: number): Promise<GraveDetailView> {
  const [grave, runs, invoices] = await Promise.all([
    fetchGraveDetails(graveId),
    readAll(async (skip, top) => {
      const response = await odata
        .GraveInvoiceRuns()
        .query((b, q) => {
          b.filter(q.GraveId.eq(graveId)).skip(skip).top(top);
        })
        .execute();
      return response.data.value;
    }),
    readAll(async (skip, top) => {
      const response = await odata
        .Invoices()
        .query((b, q) => {
          b.filter(q.GraveId.eq(graveId).and(q.IsLatestRevision.isTrue()))
            .orderBy(q.PeriodYear.desc(), q.PeriodHalf.desc(), q.RecipientName.asc())
            .skip(skip)
            .top(top);
        })
        .execute();
      return response.data.value;
    }),
  ]);

  const [year, half] = currentHalfYear();
  const currentInvoiced = runs.some((run) => run.Year === year && run.Half === half);
  const current = invoices.find(
    (invoice) => invoice.PeriodYear === year && invoice.PeriodHalf === half,
  );

  const lines: GraveLineView[] = current
    ? (await fetchGraveInvoiceGroup(current.Id)).Lines.map((line) => ({
        Number: line.ItemNumber,
        Description: line.Description,
        Count: line.Count,
        Price: priceFieldValue(line.UnitPrice, line.PriceIncludesVat, line.VatRate),
        VatRate: line.VatRate,
        PriceIncludesVat: line.PriceIncludesVat,
      }))
    : grave.Items.map((link) => ({
        Number: link.Item.Number,
        Description: link.Item.Description,
        Count: link.Count,
        Price: link.UnitPrice ?? link.Item.DefaultPrice ?? 0,
        VatRate: link.Item.VatRate,
        PriceIncludesVat: link.PriceIncludesVat,
      }));

  return { grave, currentInvoiced, lines, invoices };
}
```

If `orderBy` is not variadic in the generated builder, chain it: `.orderBy(q.PeriodYear.desc()).orderBy(q.PeriodHalf.desc()).orderBy(q.RecipientName.asc())`, and check the URL reads `$orderby=PeriodYear desc,PeriodHalf desc,RecipientName asc` (`.getUrl()` on the command).

- [ ] **Step 2: Create `src/composables/useGraveInvoiceEdit.ts`**

```ts
import { useQueryClient } from "@tanstack/vue-query";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { fetchGraveInvoiceGroup } from "@/api/endpoints";
import { loadItemVatRates } from "@/api/queries";
import { halfYearText } from "@/lib/halfYear";
import { type DraftLine, newLine } from "@/lib/lines";
import { priceFieldValue } from "@/lib/money";
import { useErrorToast } from "./useErrorToast";
import { showMessage } from "./useMessage";

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
}

/** Opens the grave-invoice editor -- or explains why not. One edit revises
 *  every recipient of the grave's half-year, so one paid recipient blocks all
 *  of them, and the group names who they are. */
export function useGraveInvoiceEdit() {
  const { t } = useI18n();
  const { showError } = useErrorToast();
  const queryClient = useQueryClient();
  const visible = ref(false);
  const draft = ref<GraveInvoiceDraft | null>(null);

  async function open(invoiceId: number): Promise<void> {
    try {
      const [group, rates] = await Promise.all([
        fetchGraveInvoiceGroup(invoiceId),
        loadItemVatRates(queryClient),
      ]);
      const paid = group.Recipients.filter((recipient) => recipient.Paid);
      if (paid.length) {
        showMessage(
          t("graveInvoiceEditTitle"),
          t("errorGraveInvoicePaidNames", [paid.map((recipient) => recipient.Name).join(", ")]),
        );
        return;
      }
      draft.value = {
        invoiceId,
        warning: t("graveInvoiceEditWarning", [
          halfYearText(group.PeriodYear, group.PeriodHalf),
          group.Recipients.map((recipient) => recipient.Name).join(", "),
        ]),
        // Grave-level lines: the full count at the full price. A net price is
        // not rounded -- an old line keeps three decimals on purpose -- and only
        // a VAT-included one is converted (GraveEdit.controller.ts).
        lines: group.Lines.map((line) =>
          newLine({
            itemId: line.ItemId,
            count: line.Count,
            unitPrice: priceFieldValue(
              line.UnitPrice,
              line.PriceIncludesVat,
              rates.get(line.ItemId) ?? 0,
            ),
            priceIncludesVat: line.PriceIncludesVat,
          }),
        ),
      };
      visible.value = true;
    } catch (error) {
      showError(error);
    }
  }

  return { visible, draft, open };
}
```

- [ ] **Step 3: Create `src/components/GraveInvoiceEditor.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import Message from "primevue/message";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createGraveInvoiceRevision, type InvoiceLineInput } from "@/api/endpoints";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import type { GraveInvoiceDraft } from "@/composables/useGraveInvoiceEdit";
import { type DraftLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";

const props = defineProps<{ draft: GraveInvoiceDraft | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();
const lines = ref<DraftLine[]>([]);

watch(visible, (open) => {
  if (open) {
    lines.value = props.draft?.lines ?? [];
  }
});

// No billing pre-check here, as in the UI5 app: the backend validates the revision.
async function save(): Promise<void> {
  const draft = props.draft;
  if (!draft) {
    return;
  }
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  try {
    await run(t("invoiceRunning"), () => createGraveInvoiceRevision(draft.invoiceId, inputs));
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    :header="t('graveInvoiceEditTitle')"
    :style="{ width: 'min(100vw, 60rem)' }"
  >
    <form id="graveInvoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="draft" severity="warn">{{ draft.warning }}</Message>
      <LineEditor v-model="lines" />
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
        <Button type="submit" form="graveInvoiceForm" :label="t('save')" icon="pi pi-check" />
      </div>
    </template>
  </Drawer>
</template>
```

- [ ] **Step 4: Create `src/pages/GraveDetailPage.vue`**

```vue
<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Tag from "primevue/tag";
import { computed, ref, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { loadGraveDetail } from "@/api/queries";
import GraveEditor from "@/components/GraveEditor.vue";
import GraveInvoiceEditor from "@/components/GraveInvoiceEditor.vue";
import InvoiceRowActions from "@/components/InvoiceRowActions.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import { useGraveInvoiceEdit } from "@/composables/useGraveInvoiceEdit";
import {
  formatAmount,
  formatDate,
  formatInvoiceNumber,
  formatRevisionBadge,
  formatVatRate,
} from "@/lib/format";
import { currentHalfYear, halfYearText } from "@/lib/halfYear";

const props = defineProps<{ graveId: number }>();
const { t } = useI18n();

const detail = useQuery({
  queryKey: ["graves", "detail", toRef(props, "graveId")],
  queryFn: () => loadGraveDetail(props.graveId),
});
const grave = computed(() => detail.data.value?.grave);
const currentLabel = t("currentHalfYearStatus", [halfYearText(...currentHalfYear())]);

const editorOpen = ref(false);
const graveInvoiceEdit = useGraveInvoiceEdit();
</script>

<template>
  <PageHeader
    :title="grave?.GraveNumber ?? t('graveDetailTitle')"
    :crumbs="[
      { label: t('menuGraves'), to: '/graves' },
      { label: grave?.GraveNumber ?? t('graveDetailTitle') },
    ]"
  >
    <template v-if="grave" #actions>
      <Button :label="t('editGrave')" icon="pi pi-pencil" @click="editorOpen = true" />
    </template>
  </PageHeader>

  <div v-if="detail.isPending.value" class="flex justify-center p-10"><ProgressSpinner /></div>
  <Message v-else-if="detail.isError.value || !grave" severity="warn">{{ t("notFound") }}</Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <dl class="m-0 grid grid-cols-2 gap-4 md:grid-cols-4">
          <div>
            <dt class="text-sm text-muted-color">{{ t("startDate") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatDate(grave.StartDate) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("endDate") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatDate(grave.EndDate) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("graveCreatedAt") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatDate(grave.CreatedAt) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ currentLabel }}</dt>
            <dd class="m-0">
              <Tag
                :value="detail.data.value!.currentInvoiced ? t('statusInvoiced') : t('statusOpen')"
                :severity="detail.data.value!.currentInvoiced ? 'success' : 'danger'"
                :aria-label="
                  detail.data.value!.currentInvoiced
                    ? t('statusInvoicedAnnouncement')
                    : t('statusOpenAnnouncement')
                "
              />
            </dd>
          </div>
        </dl>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveItems") }}</template>
      <template #content>
        <DataTable :value="detail.data.value!.lines" size="small">
          <template #empty>{{ t("noData") }}</template>
          <Column field="Number" :header="t('itemNumber')" />
          <Column
            field="Description"
            :header="t('itemDescription')"
            header-class="hidden sm:table-cell"
            body-class="hidden sm:table-cell"
          />
          <Column field="Count" :header="t('itemCount')" body-class="text-right tabular-nums" />
          <Column :header="t('itemPrice')" body-class="text-right tabular-nums">
            <template #body="{ data }">{{ formatAmount(data.Price) }}</template>
          </Column>
          <Column
            :header="t('vatRate')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell text-right"
          >
            <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
          </Column>
          <Column
            :header="t('priceIncludesVatColumn')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell"
          >
            <template #body="{ data }">{{ data.PriceIncludesVat ? t("yes") : t("no") }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveRecipients") }}</template>
      <template #content>
        <DataTable :value="grave.Recipients.map((link) => link.Recipient)" size="small">
          <Column field="Name" :header="t('recipientName')" />
          <Column :header="t('recipientAddress')" body-class="whitespace-pre-line">
            <template #body="{ data }">{{ data.Address }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveInvoices") }}</template>
      <template #content>
        <DataTable :value="detail.data.value!.invoices" data-key="Id" size="small">
          <template #empty>{{ t("noGraveInvoices") }}</template>
          <Column :header="t('halfYear')">
            <template #body="{ data }">{{
              halfYearText(data.PeriodYear, data.PeriodHalf)
            }}</template>
          </Column>
          <Column field="RecipientName" :header="t('recipientName')" />
          <Column
            :header="t('invoiceNumber')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell tabular-nums"
          >
            <template #body="{ data }">
              {{ formatInvoiceNumber(data.InvoiceNumber) }}
              <Tag
                v-if="data.RevisionNumber > 0"
                :value="formatRevisionBadge(data.RevisionNumber)"
                severity="secondary"
                class="ml-1"
              />
            </template>
          </Column>
          <Column
            :header="t('invoiceDate')"
            header-class="hidden lg:table-cell"
            body-class="hidden lg:table-cell tabular-nums"
          >
            <template #body="{ data }">{{ formatDate(data.GeneratedAt) }}</template>
          </Column>
          <Column :header="t('grossTotal')" body-class="text-right tabular-nums">
            <template #body="{ data }">{{ formatAmount(data.GrossTotal) }}</template>
          </Column>
          <Column :header="t('paidStatus')">
            <template #body="{ data }"><PaidStatusTag :paid-on="data.PaidOn" /></template>
          </Column>
          <Column class="w-0 whitespace-nowrap">
            <template #body="{ data }">
              <InvoiceRowActions
                :invoice-id="data.Id"
                :paid-on="data.PaidOn"
                :details-to="`/graves/${graveId}/invoices/${data.RootId}`"
                @edit="graveInvoiceEdit.open(data.Id)"
              />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>

  <GraveEditor v-model:visible="editorOpen" :grave-id="graveId" />
  <GraveInvoiceEditor
    v-model:visible="graveInvoiceEdit.visible.value"
    :draft="graveInvoiceEdit.draft.value"
  />
</template>
```

If `halfYearText(...currentHalfYear())` fails to type-check, destructure first. If `v-model:visible="graveInvoiceEdit.visible.value"` is rejected by vue-tsc, destructure in the script (`const { visible: graveInvoiceOpen, draft: graveInvoiceDraft, open: openGraveInvoice } = useGraveInvoiceEdit();`) and bind those refs.

- [ ] **Step 5: Register the route**

```ts
  {
    path: "/graves/:graveId",
    component: () => import("@/pages/GraveDetailPage.vue"),
    props: (route) => ({ graveId: Number(route.params.graveId) }),
  },
```

- [ ] **Step 6: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081, graves from Task 12):

- `/graves/<G-1 id>`: summary dates, "Angelegt am", "Halbjahr H? 2026 · erstellt". Leistungen shows the invoiced lines; Empfänger both recipients with multi-line addresses; Rechnungen one row per recipient with total and "unbezahlt". Compare every figure with the UI5 page `http://localhost:8081/#/graves/<id>`.
- A grave not yet invoiced (create G-3 without generating) shows its configured lines: link price, else the item default, else 0,00.
- Row edit → drawer with the warning naming the half-year and both recipients, lines at grave level. Change the count and save → overlay "Rechnung wird erstellt …", both recipients' rows show R1.
- Mark one recipient's invoice paid, then edit the other's → no drawer; a modal "Bezahlt sind bereits die Rechnungen von <name> …".
- "Grab bearbeiten" opens the grave editor for this grave.

- [ ] **Step 7: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the grave detail page and grave-invoice editing"
```

---

### Task 14: Grave invoice detail page

**Files:**

- Create: `src/pages/GraveInvoiceDetailPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `loadChain`, `fetchGraveInvoiceGroup`, `RevisionList`, `useGraveInvoiceEdit`, `GraveInvoiceEditor`, `usePaidToggle`, `PaidStatusTag`, `formatShare`, `halfYearText`.

Behaviour reference: `frontend/src/view/GraveInvoiceDetail.view.xml`, `frontend/src/controller/GraveInvoiceDetail.controller.ts`.

- [ ] **Step 1: Create `src/pages/GraveInvoiceDetailPage.vue`**

```vue
<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { fetchGraveInvoiceGroup } from "@/api/endpoints";
import { loadChain } from "@/api/queries";
import GraveInvoiceEditor from "@/components/GraveInvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { useGraveInvoiceEdit } from "@/composables/useGraveInvoiceEdit";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { formatInvoiceNumber, formatShare } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";

const props = defineProps<{ graveId: number; rootId: number }>();
const { t } = useI18n();
const togglePaid = usePaidToggle();
const { visible: editorOpen, draft, open: openEditor } = useGraveInvoiceEdit();

const chain = useQuery({
  queryKey: ["invoices", "chain", toRef(props, "rootId")],
  queryFn: () => loadChain(props.rootId),
});
const latest = computed(() => chain.data.value?.[0]);
const number = computed(() => formatInvoiceNumber(latest.value?.InvoiceNumber));

const latestId = computed(() => latest.value?.Id);
const group = useQuery({
  queryKey: ["graves", "invoice-group", latestId],
  queryFn: () => fetchGraveInvoiceGroup(latestId.value!),
  enabled: computed(() => latestId.value != null),
});
// the siblings of this half-year, minus the invoice shown
const others = computed(() =>
  (group.data.value?.Recipients ?? []).filter(
    (recipient) => recipient.InvoiceId !== latestId.value,
  ),
);
const halfYear = computed(() =>
  latest.value?.PeriodYear != null && latest.value.PeriodHalf != null
    ? halfYearText(latest.value.PeriodYear, latest.value.PeriodHalf)
    : "",
);
</script>

<template>
  <PageHeader
    :title="latest ? number : t('graveInvoiceDetailTitle')"
    :crumbs="[
      { label: t('menuGraves'), to: '/graves' },
      { label: group.data.value?.GraveNumber ?? t('graveDetailTitle'), to: `/graves/${graveId}` },
      { label: number || t('graveInvoiceDetailTitle') },
    ]"
  >
    <template v-if="latest" #actions>
      <PaidStatusTag :paid-on="latest.PaidOn" />
      <Button
        :icon="latest.PaidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        :label="latest.PaidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        severity="secondary"
        outlined
        @click="togglePaid(latest.Id, latest.PaidOn)"
      />
      <span v-tooltip.bottom="latest.PaidOn ? t('editBlockedPaid') : undefined">
        <Button
          :label="t('actionEdit')"
          icon="pi pi-pencil"
          :disabled="!!latest.PaidOn"
          @click="openEditor(latest.Id)"
        />
      </span>
    </template>
  </PageHeader>

  <div v-if="chain.isPending.value" class="flex justify-center p-10"><ProgressSpinner /></div>
  <Message v-else-if="!latest" severity="warn">{{ t("notFound") }}</Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <div class="flex flex-wrap justify-between gap-6">
          <div>
            <div class="text-lg font-semibold">{{ latest.RecipientName }}</div>
            <div class="whitespace-pre-line text-muted-color">{{ latest.RecipientAddress }}</div>
          </div>
          <dl class="m-0 grid grid-cols-3 gap-6">
            <div>
              <dt class="text-sm text-muted-color">{{ t("graveNumber") }}</dt>
              <dd class="m-0">{{ group.data.value?.GraveNumber }}</dd>
            </div>
            <div>
              <dt class="text-sm text-muted-color">{{ t("halfYear") }}</dt>
              <dd class="m-0">{{ halfYear }}</dd>
            </div>
            <div>
              <dt class="text-sm text-muted-color">{{ t("sharePercent") }}</dt>
              <dd class="m-0 tabular-nums">{{ formatShare(latest.SharePercent) }}</dd>
            </div>
          </dl>
        </div>
      </template>
    </Card>

    <Card v-if="others.length">
      <template #title>{{ t("otherRecipients") }}</template>
      <template #content>
        <ul class="m-0 flex list-none flex-col gap-1 p-0">
          <li v-for="recipient in others" :key="recipient.InvoiceId">
            <RouterLink
              :to="`/graves/${graveId}/invoices/${recipient.RootId}`"
              class="flex items-center justify-between rounded-lg px-3 py-2 text-color no-underline hover:bg-emphasis"
            >
              <span>{{ recipient.Name }}</span>
              <i class="pi pi-angle-right" aria-hidden="true" />
            </RouterLink>
          </li>
        </ul>
      </template>
    </Card>

    <RevisionList :chain="chain.data.value ?? []" />
  </div>

  <GraveInvoiceEditor v-model:visible="editorOpen" :draft="draft" />
</template>
```

- [ ] **Step 2: Register the route** (before `/graves/:graveId` is fine; paths do not overlap)

```ts
  {
    path: "/graves/:graveId/invoices/:rootId",
    component: () => import("@/pages/GraveInvoiceDetailPage.vue"),
    props: (route) => ({ graveId: Number(route.params.graveId), rootId: Number(route.params.rootId) }),
  },
```

- [ ] **Step 3: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081):

- From the grave detail, a row's details → header shows grave number, "H? 2026" and the share ("50,0 %" for two recipients). "Weitere Empfänger" lists the other recipient; clicking it switches to that invoice.
- Revision panels: Brutto per line uses the recipient count as divisor — the column adds up to the grave's gross, while the totals block shows this recipient's share. Compare with `http://localhost:8081/#/graves/<id>/invoices/<rootId>` to the cent.
- **Review Focus 1 (decimals):** create a grave `G-4` with 3 recipients and a line at 10,00 € net; generate a half-year that has no invoices for it yet (choose H2 of next year). Open a recipient invoice → "Bearbeiten" → the price shows 10,00; save without changes → R1 lines equal R0's. Then in the UI5 app open the same grave invoice's edit dialog and note its price value; the Vue field must show the same digits (for an old 3-decimal line the UI5 field shows e.g. 3,333, and so must Vue — no rounding to 3,33).
- Paid toggle and the paid-sibling refusal work as on the grave page.

- [ ] **Step 4: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the grave invoice detail page"
```

---

### Task 15: Import/Export page

**Files:**

- Create: `src/pages/ImportExportPage.vue`
- Modify: `src/router.ts`

**Interfaces:**

- Consumes: `downloadImportTemplate`, `importArchive`, `ImportRejectedError`, `ImportProblem`, `downloadBlob`, `useBlocking`, `showMessage`, `useErrorToast`, `ApiError`.

Behaviour reference: `frontend/src/view/ImportExport.view.xml`, `frontend/src/controller/ImportExport.controller.ts`.

- [ ] **Step 1: Create `src/pages/ImportExportPage.vue`**

```vue
<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  downloadImportTemplate,
  importArchive,
  type ImportProblem,
  ImportRejectedError,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";

const { t } = useI18n();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const fileInput = ref<HTMLInputElement>();
const file = ref<File | null>(null);
const problems = ref<ImportProblem[]>([]);
const downloading = ref(false);

function onFileChosen(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null;
  problems.value = [];
}

async function downloadTemplate(): Promise<void> {
  downloading.value = true;
  try {
    downloadBlob(await downloadImportTemplate(), "import-vorlage.zip");
  } catch (error) {
    showError(error);
  } finally {
    downloading.value = false;
  }
}

async function startImport(): Promise<void> {
  const chosen = file.value;
  if (!chosen) {
    showError(new ApiError("ArchiveRequired"));
    return;
  }
  try {
    const summary = await run(t("importRunning"), () => importArchive(chosen));
    file.value = null;
    if (fileInput.value) {
      fileInput.value.value = "";
    }
    await queryClient.invalidateQueries();
    showMessage(
      t("importDone"),
      t("importSummary", [
        summary.recipients,
        summary.items,
        summary.graves,
        summary.itemLinks,
        summary.recipientLinks,
      ]),
      "info",
    );
  } catch (error) {
    if (error instanceof ImportRejectedError) {
      problems.value = error.problems;
      showMessage(t("importArchive"), t("importRejected"));
      return;
    }
    showError(error);
  }
}
</script>

<template>
  <PageHeader :title="t('importExportTitle')" />

  <div class="flex flex-col gap-6">
    <Card>
      <template #title>{{ t("importTemplateHeader") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("importTemplateHint") }}</p>
        <Button
          :label="t('importTemplateDownload')"
          icon="pi pi-download"
          outlined
          :loading="downloading"
          @click="downloadTemplate"
        />
      </template>
    </Card>

    <Card>
      <template #title>{{ t("importUploadHeader") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("importUploadHint") }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <input
            ref="fileInput"
            type="file"
            accept=".zip,application/zip"
            class="sr-only"
            :aria-label="t('importSelectFile')"
            @change="onFileChosen"
          />
          <Button
            :label="t('importSelectFile')"
            icon="pi pi-folder-open"
            severity="secondary"
            outlined
            @click="fileInput?.click()"
          />
          <span class="text-sm" :class="file ? '' : 'text-muted-color'">{{
            file?.name ?? t("noFileChosen")
          }}</span>
          <Button :label="t('importStart')" icon="pi pi-upload" @click="startImport" />
        </div>
      </template>
    </Card>

    <Card v-if="problems.length">
      <template #title>{{ t("importProblemsHeader") }}</template>
      <template #content>
        <DataTable :value="problems" size="small">
          <Column field="file" :header="t('importProblemFile')" />
          <Column field="row" :header="t('importProblemRow')" class="w-24" />
          <Column field="message" :header="t('importProblemMessage')" />
        </DataTable>
      </template>
    </Card>
  </div>
</template>
```

- [ ] **Step 2: Register the route** — `{ path: "/import-export", component: () => import("@/pages/ImportExportPage.vue") },`

- [ ] **Step 3: Verify**

`npm run typecheck && npm run lint && npm run build` — exit 0.

Manual check (against 8081):

- "Vorlage herunterladen" saves `import-vorlage.zip` with four CSV files.
- "Importieren" without a file → "Bitte wählen Sie zuerst eine ZIP-Datei aus."
- Unzip the template, add one recipient row with an empty name, zip it, import → the overlay closes, the rejection modal shows, and "Gefundene Probleme" lists file, row and message. Choosing another file hides the problem table.
- Import the template with valid rows (one recipient, one item, one grave with both links) → summary modal with the five counts; the rows appear on their pages.

- [ ] **Step 4: Commit**

```bash
git add frontend-vue/src
git commit -m "chore(vue): add the import/export page"
```

---

### Task 16: Governance docs and the final click-through

**Files:**

- Modify: `docs/deviations.md`, `CLAUDE.md`

- [ ] **Step 1: Record the deviation** — append to `docs/deviations.md`:

```markdown
## D-003: A second frontend in Vue, for evaluation only

**Principle**: VI — OpenUI5 Frontend; and the Technology Constraints line
"consumed by the UI5 frontend's OData V4 model".

**Deviation**: `frontend-vue/` is a second frontend, built with Vue 3 and
PrimeVue 4 (MIT). It covers the same features as the OpenUI5 frontend and talks
to the same backend through a client generated from `$metadata` (odata2ts),
plus a hand-typed module for the D-001 endpoints and the actions whose
parameters `$metadata` leaves out.

**Why**: To decide whether to replace the OpenUI5 frontend on the strength of a
like-for-like build rather than a sketch. Replacing it would amend Principle VI;
this entry does not.

**Containment**: It runs in development only (`docker-compose.yml`, port 8082)
and is not part of the release image, which serves the OpenUI5 frontend alone.
It uses the documented OData interface unchanged, keeps the UI multilingual
(VII) and blocks the UI during long-running tasks (VIII). It has no automated
tests by decision; CI type-checks, lints and builds it.
```

- [ ] **Step 2: Document how to run it** — in `CLAUDE.md`, under "Running the parts", after the "Frontend checks" bullet:

```markdown
- **Vue evaluation frontend** (`frontend-vue/`, D-003): the dev stack also
  serves it on 8082, against the dev backend. For probing, run it on the host
  against the E2E stack instead:
  `cd frontend-vue && VITE_PROXY_TARGET=http://localhost:8081 npm run dev`.
  Checks: `npm run typecheck`, `npm run lint`, `npm run build`. After an EDM
  change, `npm run gen:odata` (reads `$metadata` from the E2E stack) and commit
  `src/api/metadata.xml` together with `src/api/generated/`.
```

- [ ] **Step 3: Full verification**

From `frontend-vue/`: `npm run typecheck && npm run lint && npm run build` — exit 0.
From the repo root: `npx prettier --check .` — clean.

- [ ] **Step 4: Final click-through** (against 8081, desktop 1440 px and phone 390 px, light and dark)

Go through every page next to the UI5 app on the same stack (`http://localhost:8081`) and tick each:

- [ ] Settings: values, save, VAT add/delete with confirmation.
- [ ] Items: create/edit, search, graves-with-item groups and links, VAT locked when linked.
- [ ] Recipients: create/edit, auto number, search.
- [ ] Invoices: create (with quick-create recipient), revise, PDF/copy, paid toggle with confirmation, payment filter, search, paging.
- [ ] Invoice detail: revisions, per-line gross, totals equal to UI5.
- [ ] Graves: create/edit, billing refusal text, generation with progress and summary, duplicate generation refused, search, unpaid filter, ZIP.
- [ ] Grave detail and grave invoice detail: lines, recipients, invoices, grave-invoice revision, paid-sibling refusal, share, other recipients.
- [ ] Import/Export: template, rejection with problem table, successful import summary.
- [ ] Review Focus 1–5 each re-checked once.
- [ ] Phone width: navigation drawer, no sideways page scroll, row actions in "⋯", editors full width.
- [ ] Dark mode: every page legible; the choice survives a reload.
- [ ] Browser console: no errors or Vue warnings on any page.

Anything that fails: fix it in the owning file, re-run Step 3, and commit it as `chore(vue): …` (nothing here is released).

- [ ] **Step 5: Stop the E2E stack** — `cd e2e && npm run stack:down`

- [ ] **Step 6: Commit**

```bash
git add docs/deviations.md CLAUDE.md
git commit -m "docs: record the Vue evaluation frontend as deviation D-003"
```
