# View State in Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the frontend's view state out of UI5 controls into `TypedJSONModel` models bound from XML, and adopt the official UI5 TypeScript ESLint configuration plus `@ui5/linter`.

**Architecture:** Every dialog gets a `<thing>Draft` model and every page a `view` model, both `TypedJSONModel<T>` with interfaces in a new `src/model/state.ts`. XML binds two-way to those models, so controllers read state from the model instead of reaching into controls with `this.byId(...)`. Two `byId` sites survive on purpose: a list binding's `changeParameters` and `FileUploader.clear()`.

**Tech Stack:** OpenUI5 1.151.0, TypeScript 5.5 (strict), `ui5-tooling-transpile`, `typescript-eslint`, `@ui5/linter`, Playwright.

**Spec:** `docs/superpowers/specs/2026-08-29-ui5-json-model-bindings-design.md`

---

## Global Constraints

- **OpenUI5 only.** No SAPUI5 runtime dependency may enter the delivery (Constitution VI). `@ui5/linter` pulls `@sapui5/types` as a dev-tool transitive dependency; that is allowed, and nothing may import from it.
- **English source, i18n UI** (Constitution VII). No user-visible string moves out of `src/i18n/*.properties`. Any new key goes into **both** `i18n.properties` and `i18n_en.properties`.
- **Reads stay on the OData model** (deviation D-002). Settings is filled via `bindContext("/Settings").requestObject()`, never `fetch`.
- **Never run E2E against the dev stack.** `cd e2e && npm test` only. `E2E_BASE_URL=http://localhost:8080` is forbidden — the dev database holds the user's data.
- **Control `id` attributes must not be removed.** `graves.spec.ts:210` matches `.sapMSlt[id$='linkRecipient']`; ~36 `getByLabel(...)` locators resolve through `labelFor`. Only `byId` *calls* go.
- **Every commit must pass `npm run typecheck`.** ESLint goes from 633 errors to 0 across the branch; only the final task gates on zero.
- Model names already in use and not to be renamed: `odata`, `i18n`, `device`, `status`, `import`, `invoiceDraft`, `graveDraft`, `graveInvoiceDraft`.

## Testing approach — read this before Task 1

**There is no frontend unit-test harness in this repository, and this plan does not add one.** Constitution Principle III is explicit: unit tests are not mandated, and test effort goes to integration and E2E tests that exercise user-visible behaviour.

So the usual red-green TDD loop does not apply here. This is a behaviour-preserving refactor whose tests already exist: the Playwright suite in `e2e/tests/` already drives every dialog being changed. The loop for each task is:

1. Make the change.
2. `npm run typecheck` — must pass.
3. `npm run lint` — that file's error count must reach 0.
4. Run the E2E spec named in the task — must pass, **unchanged**. Editing a spec to make it pass is a plan failure; if a spec fails, the refactor broke behaviour.
5. Commit.

**Do not modify anything under `e2e/`.** The specs are the control group.

### Baseline, measured 2026-08-29

`npx ui5lint` — **clean, 0 findings** (verified real: a deliberate `Core.getConfiguration()` probe was correctly flagged).

`npx eslint src` — **633 errors**:

| Rule | Count |
|---|---|
| `no-unsafe-member-access` | 262 |
| `no-unsafe-call` | 131 |
| `no-explicit-any` | 112 |
| `no-unsafe-assignment` | 81 |
| `no-unnecessary-type-assertion` | 21 |
| `no-unsafe-argument` | 8 |
| `await-thenable` | 8 |
| `no-misused-promises` | 3 |
| `unbound-method` | 3 |
| `no-unsafe-return` | 2 |
| `no-floating-promises` | 1 |
| `require-await` | 1 |

Per file — this is each task's target:

| File | Errors | Task |
|---|---|---|
| `controller/GraveEdit.controller.ts` | 172 | 9 |
| `controller/GraveDetail.controller.ts` | 79 | 11 |
| `controller/InvoiceEdit.controller.ts` | 71 | 8 |
| `controller/Graves.controller.ts` | 59 | 7 |
| `controller/InvoiceRevisions.controller.ts` | 41 | 12 |
| `controller/Items.controller.ts` | 41 | 4 |
| `controller/Recipients.controller.ts` | 41 | 5 |
| `controller/Invoices.controller.ts` | 29 | 10 |
| `controller/GraveInvoiceDetail.controller.ts` | 26 | 11 |
| `model/api.ts` | 26 | 3 |
| `controller/InvoiceDetail.controller.ts` | 16 | 12 |
| `controller/Settings.controller.ts` | 15 | 6 |
| `controller/Base.controller.ts` | 14 | 2 |
| `controller/ImportExport.controller.ts` | 2 | 10 |
| `Component.ts` | 1 | 12 |

## File structure

**Created:**

- `frontend/eslint.config.mjs` — lint configuration, mirrors the official UI5 TS sample.
- `frontend/src/model/state.ts` — every view/draft interface. One responsibility: describing what the models hold. No behaviour, no imports from controllers.

**Modified — controllers:** all fifteen under `frontend/src/controller/`, plus `frontend/src/Component.ts` and `frontend/src/model/api.ts`.

**Modified — views and fragments:** `Items.view.xml`, `Recipients.view.xml`, `Settings.view.xml`, `Graves.view.xml`, `ImportExport.view.xml`, `ItemDialog.fragment.xml`, `RecipientDialog.fragment.xml`, `RecipientQuickCreate.fragment.xml`, `GenerateDialog.fragment.xml`, `GraveDialog.fragment.xml`, `InvoiceDialog.fragment.xml`.

**Modified — config:** `frontend/package.json`, `CLAUDE.md`.

### Two verified facts the whole plan rests on

Both were checked against the pinned 1.151.0 sources and with `tsc`, not assumed.

**1. Typed row paths need template literals, not concatenation.**

```ts
// does NOT compile — `path + "/itemNumber"` widens to string
model.setProperty(rowContext.getPath() + "/itemNumber", value);

// compiles — type is `/lines/${number}/itemNumber`
const index = Number(rowContext.getPath().split("/").pop());
model.setProperty(`/lines/${index}/itemNumber`, value);
```

Verified with four negative controls: a misspelt absolute path, a misspelt row property, a wrong value type and a wrong read type each fail to compile.

**2. Bound filters work inside an OR multi-filter.** `_createBoundFilter` recurses into `aFilters`; `Filter.removeAllNeutrals` recurses and returns `undefined` when every child is neutral. A `null` value makes a filter neutral. So a cleared search field drops the whole OR, with no `if (!term)` branch in the controller.

---

### Task 1: Lint tooling

**Files:**
- Create: `frontend/eslint.config.mjs`
- Modify: `frontend/package.json`

**Interfaces:**
- Consumes: nothing.
- Produces: `npm run lint` and `npm run ui5lint` in `frontend/`. Every later task uses both.

- [ ] **Step 1: Add the dev dependencies**

```bash
cd frontend && npm install -D --no-audit --no-fund eslint globals typescript-eslint @ui5/linter
```

- [ ] **Step 2: Write `frontend/eslint.config.mjs`**

```js
import globals from "globals";
import tseslint from "typescript-eslint";

export default tseslint.config(
  tseslint.configs.eslintRecommended,
  ...tseslint.configs.recommended,
  ...tseslint.configs.recommendedTypeChecked,
  {
    languageOptions: {
      globals: {
        ...globals.browser,
        sap: "readonly",
      },
      ecmaVersion: 2023,
      parserOptions: {
        project: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },
  {
    // dist/ holds the whole OpenUI5 runtime
    ignores: ["dist/", "eslint.config.mjs"],
  },
);
```

- [ ] **Step 3: Add the scripts to `frontend/package.json`**

```json
"lint": "eslint src",
"ui5lint": "ui5lint"
```

- [ ] **Step 4: Confirm `ui5lint` is clean and `eslint` reports the baseline**

Run: `cd frontend && npm run ui5lint && npm run lint`
Expected: `ui5lint` prints "No findings detected"; `eslint` exits non-zero with **633 errors**. That number is the baseline, not a failure.

- [ ] **Step 5: Commit**

```bash
git add frontend/eslint.config.mjs frontend/package.json frontend/package-lock.json
git commit -m "build: add eslint and the ui5 linter to the frontend"
```

---

### Task 2: State interfaces and the typed-model helper

**Files:**
- Create: `frontend/src/model/state.ts`
- Modify: `frontend/src/controller/Base.controller.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: every interface below, plus two `BaseController` members used by Tasks 4–10:
  - `protected setState<T extends object>(name: string, data: T): TypedJSONModel<T>` — creates the model, sets it on the view, returns it.
  - `protected state<T extends object>(name: string): TypedJSONModel<T>` — reads it back.
  - `protected readList<T>(path: string, parameters: Record<string, unknown>): Promise<T[]>` — now generic; callers pass the row type.

- [ ] **Step 1: Write `frontend/src/model/state.ts`**

Every draft mirrors what its dialog collects. Select and ComboBox keys are strings — that is the existing `itemId` convention and the reason `recipientId` is a string here even though the API takes a number.

```ts
import type { YearHalfOption } from "./api";

/** A line in the invoice, grave and grave-invoice dialogs — all three collect the same shape. */
export interface DraftLine {
  itemId: string;
  itemNumber: string;
  description: string;
  count: number;
  unitPrice: number | null;
  defaultPrice: number | null;
}

export interface ItemsView {
  searchTerm: string | null;
}

export interface ItemDraft {
  id: number | null;
  number: string;
  description: string;
  vatRate: string;
  defaultPrice: string;
}

export interface RecipientsView {
  searchTerm: string | null;
}

export interface RecipientDraft {
  id: number | null;
  number: string;
  name: string;
  address: string;
}

export interface SettingsView {
  newVatRate: string;
}

export interface SettingsDraft {
  stampWord: string;
  endingText: string;
  language: string;
}

export interface GravesView {
  halfYearOptions: YearHalfOption[];
  halfYear: string;
}

export interface GenerateDraft {
  yearOptions: YearHalfOption[];
  year: string;
  half: string;
}

/** Shared by InvoiceEdit and GraveEdit — they load the same fragment. */
export interface QuickRecipientDraft {
  name: string;
  address: string;
}

export interface InvoiceDraft {
  invoiceId: number | null;
  recipientId: string;
  lines: DraftLine[];
}

export interface GraveRecipientRow {
  recipientId: string;
  name: string;
}

export interface GraveDraft {
  Id: number | null;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  Items: DraftLine[];
  Recipients: GraveRecipientRow[];
  linkRecipientId: string;
  linkRecipientName: string;
}

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
}
```

- [ ] **Step 2: Add the helpers to `Base.controller.ts` and make `readList` generic**

Replace `readList`'s `Promise<any[]>` with `Promise<T[]>` and its two internal `any[]`/`any` uses with `T[]`/`T`. Add:

```ts
protected setState<T extends object>(name: string, data: T): TypedJSONModel<T> {
  const model = new TypedJSONModel<T>(data);
  this.getView()!.setModel(model, name);
  return model;
}

protected state<T extends object>(name: string): TypedJSONModel<T> {
  return this.getView()!.getModel(name) as TypedJSONModel<T>;
}
```

Also fix `i18nText`: `(this.getView()!.getModel("i18n") as any)` becomes `ResourceModel`, with `getResourceBundle()` cast to `ResourceBundle`. That is the same shape `api.showError` already uses.

- [ ] **Step 3: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/Base.controller.ts`
Expected: typecheck passes; `Base.controller.ts` reports **0** errors (down from 14).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/model/state.ts frontend/src/controller/Base.controller.ts
git commit -m "refactor: describe view state as types and add typed-model helpers"
```

---

### Task 3: Type `model/api.ts`

**Files:**
- Modify: `frontend/src/model/api.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: types Tasks 7–11 depend on:
  - `GraveDetails`, `GraveItemLink`, `GraveRecipientLink` — the shape `fetchGraveDetails` returns.
  - `GenerationResult { InvoicedGraves: number; SkippedGraves: SkippedGrave[] }`, `SkippedGrave { Number: string; Reason?: string }` — the parsed `JobDto.ResultJson`.
  - `fetchGraveDetails(graveId: number): Promise<GraveDetails>` (was `Promise<any>`).
  - `toJson(response: Response): Promise<unknown>` (was `Promise<any>`), private.

- [ ] **Step 1: Replace the `any` escape hatches**

`toJson` returns `Promise<unknown>`. `ensureOk` narrows before reaching in:

```ts
async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }
  const body = (await toJson(response)) as { error?: { code?: string } };
  throw new Error(body.error?.code ?? `HTTP ${response.status}`);
}
```

- [ ] **Step 2: Give `fetchGraveDetails` a real return type**

Model it on what `GraveEdit.loadGraveDraft` actually reads — `Id`, `GraveNumber`, `StartDate`, `EndDate`, `Items[].Item.{Id,Number,Description,DefaultPrice}`, `Items[].{Count,UnitPrice}`, `Recipients[].Recipient.{Id,Name}`.

- [ ] **Step 3: Type the generation result**

Add `GenerationResult` and `SkippedGrave` so `Graves.onGenerateDialogSave` can drop its `as any` on `JSON.parse`. `SkippedGraves` entries are either a bare string or `{ Number, Reason }` — keep that union: `type SkippedGrave = string | { Number: string; Reason?: string }`.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/model/api.ts`
Expected: typecheck passes; `api.ts` reports **0** errors (down from 26).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/model/api.ts
git commit -m "refactor: give the api module real types instead of any"
```

---

### Task 4: Items — the reference implementation

This is the pattern every later screen follows. Do it first and do it carefully.

**Files:**
- Modify: `frontend/src/controller/Items.controller.ts`, `frontend/src/view/Items.view.xml`, `frontend/src/view/fragments/ItemDialog.fragment.xml`
- Test: `e2e/tests/masterdata.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `setState`, `state`, `ItemsView`, `ItemDraft`.
- Produces: the pattern — nothing later imports from here.

- [ ] **Step 1: Bind the search field and table filter declaratively**

In `Items.view.xml`, the table's `items` binding gains `boundFilters`. Note `and: false` for OR, and that no `filters` key is needed:

```xml
items="{
    path: 'odata>/InvoiceItems',
    parameters: {$orderby: 'Id desc'},
    boundFilters: {
        and: false,
        filters: [
            {path: 'Number', operator: 'Contains', value1: '{view>/searchTerm}'},
            {path: 'Description', operator: 'Contains', value1: '{view>/searchTerm}'}
        ]
    }
}"
```

- [ ] **Step 2: Bind the dialog fields two-way**

In `ItemDialog.fragment.xml`, keep every `id` and `labelFor` exactly as they are — `masterdata.spec.ts` finds these fields by label. Add values:

```xml
<Input id="itemNumber" width="100%" value="{itemDraft>/number}" />
<Input id="itemDescription" width="100%" value="{itemDraft>/description}" />
<Select id="itemVatRate" width="100%" forceSelection="false"
    selectedKey="{itemDraft>/vatRate}"
    items="{path: 'odata>/VatRates', parameters: {$orderby: 'Rate asc'}}">
    <core:Item key="{odata>Rate}" text="{odata>Rate}" />
</Select>
<Input id="itemDefaultPrice" type="Number" width="100%" value="{itemDraft>/defaultPrice}" />
```

And the title, replacing the controller's `setTitle` — this mirrors `GraveDialog.fragment.xml`:

```xml
<Dialog title="{= ${itemDraft>/id} === null ? ${i18n>createItem} : ${i18n>editItem} }"
        stretch="{device>/system/phone}" contentWidth="20rem">
```

- [ ] **Step 3: Rewrite the controller**

`onInit` calls `this.bindODataRoot()` (which `Base` already provides — the current hand-rolled copy goes) and `this.setState<ItemsView>("view", { searchTerm: null })`.

`onSearch` becomes three lines, with the `if (!term)` branch gone because a `null` value is neutral:

```ts
public onSearch(event: SearchField$SearchEvent): void {
  const term = event.getParameter("query") ?? "";
  this.state<ItemsView>("view").setProperty("/searchTerm", term || null);
}
```

`openItemDialog` sets a draft instead of poking four controls:

```ts
private async openItemDialog(context: Context | null): Promise<void> {
  if (!this._dialog) {
    this._dialog = (await this.loadFragment({
      name: "burkard.invoicing.view.fragments.ItemDialog",
    })) as Dialog;
  }

  // the fragment is reused, so force the VAT list to re-read from the service;
  // otherwise rates added or deleted in the settings stay stale
  this.odata().refresh();

  this._dialog.setModel(
    new TypedJSONModel<ItemDraft>(
      context
        ? {
            id: context.getProperty("Id") as number,
            number: context.getProperty("Number") as string,
            description: context.getProperty("Description") as string,
            vatRate: String(context.getProperty("VatRate")),
            defaultPrice:
              context.getProperty("DefaultPrice") != null
                ? String(context.getProperty("DefaultPrice"))
                : "",
          }
        : { id: null, number: "", description: "", vatRate: "", defaultPrice: "" },
    ),
    "itemDraft",
  );
  this._dialog.open();
}
```

`onDialogSave` reads the draft, not the controls. Keep the "empty means no default price, 0 is a real value" rule exactly:

```ts
public async onDialogSave(): Promise<void> {
  const draft = (this._dialog.getModel("itemDraft") as TypedJSONModel<ItemDraft>).getData();
  const fields = {
    Number: draft.number,
    Description: draft.description,
    VatRate: Number(draft.vatRate),
    // an empty input means "no default price"; a price of 0 is a real value
    DefaultPrice: draft.defaultPrice === "" ? null : Number(draft.defaultPrice),
  };
  try {
    if (draft.id != null) {
      await api.updateItem(draft.id, fields);
    } else {
      await api.createItem(fields);
    }
    this._dialog.close();
    this.odata().refresh();
  } catch (error) {
    await api.showError(this, error);
  }
}
```

The `_editId` field goes — the draft holds the id. `onDialogCancel` becomes `this._dialog.close()`. `_dialog` is typed `Dialog`.

- [ ] **Step 4: Verify types and lint**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/Items.controller.ts && npm run ui5lint`
Expected: typecheck passes; Items reports **0** errors (down from 41); ui5lint clean.

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop masterdata.spec.ts`
Expected: PASS. This exercises create, edit and the VAT-rate select. A failure means the refactor changed behaviour — fix the controller, never the spec.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/Items.controller.ts frontend/src/view/Items.view.xml frontend/src/view/fragments/ItemDialog.fragment.xml
git commit -m "refactor: hold the item dialog's state in a model, not in its controls"
```

---

### Task 5: Recipients

**Files:**
- Modify: `frontend/src/controller/Recipients.controller.ts`, `frontend/src/view/Recipients.view.xml`, `frontend/src/view/fragments/RecipientDialog.fragment.xml`
- Test: `e2e/tests/masterdata.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `setState`, `state`, `RecipientsView`, `RecipientDraft`.
- Produces: nothing.

- [ ] **Step 1: Bind the table filter**

One bound filter, not an OR — Recipients searches `Name` only:

```xml
items="{
    path: 'odata>/Recipients',
    parameters: {$orderby: 'Id desc'},
    boundFilters: {path: 'Name', operator: 'Contains', value1: '{view>/searchTerm}'}
}"
```

- [ ] **Step 2: Bind the dialog fields**

```xml
<Input id="recipientNumber" width="100%" placeholder="{i18n>recipientNumberHint}" value="{recipientDraft>/number}" />
<Input id="recipientName" width="100%" value="{recipientDraft>/name}" />
<TextArea id="recipientAddress" rows="4" width="100%" value="{recipientDraft>/address}" />
```

Title: `title="{= ${recipientDraft>/id} === null ? ${i18n>createRecipient} : ${i18n>editRecipient} }"`.

- [ ] **Step 3: Rewrite the controller**

`onInit` → `this.bindODataRoot()` plus `this.setState<RecipientsView>("view", { searchTerm: null })`. `onSearch` writes `term || null`. `openRecipientDialog` sets a `RecipientDraft`. `onDialogSave` reads it, preserving the "an empty number means assign the next free one" rule:

```ts
const number = draft.number.trim();
if (draft.id != null) {
  await api.updateRecipient(draft.id, { Name: draft.name, Address: draft.address, Number: number });
} else {
  // an empty number means "assign the next free one"
  const fields = { Name: draft.name, Address: draft.address };
  await api.createRecipient(number ? { ...fields, Number: number } : fields);
}
```

Then `this._dialog.close()` and `this.odata().refresh()`. Drop `_editId`; type `_dialog` as `Dialog`.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/Recipients.controller.ts`
Expected: typecheck passes; **0** errors (down from 41).

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop masterdata.spec.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/Recipients.controller.ts frontend/src/view/Recipients.view.xml frontend/src/view/fragments/RecipientDialog.fragment.xml
git commit -m "refactor: hold the recipient dialog's state in a model"
```

---

### Task 6: Settings

The one screen that changes *where its data comes from*, so read the D-002 constraint before starting: the form must be filled from the OData model, not `fetch`.

**Files:**
- Modify: `frontend/src/controller/Settings.controller.ts`, `frontend/src/view/Settings.view.xml`
- Test: `e2e/tests/masterdata.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `setState`, `state`, `SettingsDraft`, `SettingsView`.
- Produces: nothing.

- [ ] **Step 1: Fill a draft from the OData singleton in `onInit`**

```ts
public onInit(): void {
  this.setState<SettingsView>("view", { newVatRate: "" });
  this.setState<SettingsDraft>("settingsDraft", {
    stampWord: "",
    endingText: "",
    language: "",
  });
  void this.loadSettings();
}

/** The read stays on the OData model (D-002); only the write goes through fetch. */
private async loadSettings(): Promise<void> {
  try {
    const context = this.odata().bindContext("/Settings").getBoundContext();
    const settings = (await context.requestObject()) as {
      StampWord: string;
      EndingText: string;
      Language: string;
    };
    this.state<SettingsDraft>("settingsDraft").setData({
      stampWord: settings.StampWord ?? "",
      endingText: settings.EndingText ?? "",
      language: settings.Language ?? "",
    });
  } catch (error) {
    await api.showError(this, error);
  }
}
```

The view keeps its `/Settings` binding context for the VAT table's sibling bindings.

- [ ] **Step 2: Rebind the form**

Replace the three `mode: 'OneWay'` OData bindings with two-way draft bindings, keeping every `id` and `labelFor`:

```xml
<Input id="stampWord" value="{settingsDraft>/stampWord}" width="100%" />
<TextArea id="endingText" value="{settingsDraft>/endingText}" rows="6" width="100%" />
<Select id="documentLanguage" width="100%" selectedKey="{settingsDraft>/language}">
```

And the new-rate input: `<Input id="newVatRate" type="Number" width="8rem" value="{view>/newVatRate}" class="sapUiSmallMarginEnd" />`.

- [ ] **Step 3: Rewrite the handlers**

`onSave` reads the draft. `onAddVatRate` reads `view>/newVatRate`, clears it by setting `""`, and refreshes with `this.odata().refresh()`. `onDeleteVatRate` takes `Button$PressEvent`. `refreshVatRates` collapses into `this.odata().refresh()` and the method goes.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/Settings.controller.ts`
Expected: typecheck passes; **0** errors (down from 15).

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop masterdata.spec.ts`
Expected: PASS. `masterdata.spec.ts:38-43` fills the ending text, saves, reloads and asserts the value came back — that is the check that the draft is populated from OData correctly.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/Settings.controller.ts frontend/src/view/Settings.view.xml
git commit -m "refactor: edit settings through a draft model"
```

---

### Task 7: Graves toolbar and generate dialog

**Files:**
- Modify: `frontend/src/controller/Graves.controller.ts`, `frontend/src/view/Graves.view.xml`, `frontend/src/view/fragments/GenerateDialog.fragment.xml`
- Test: `e2e/tests/graves.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `setState`, `state`, `GravesView`, `GenerateDraft`, `api.GenerationResult`.
- Produces: nothing.

- [ ] **Step 1: Bind the half-year select**

This deletes the `removeAllItems()` / `addItem(new Item(...))` loop in `onInit`. Keep `id="halfYearSelect"` and the `OverflowToolbarLayoutData`:

```xml
<Select id="halfYearSelect" width="10rem" change=".onHalfYearChange"
    items="{view>/halfYearOptions}" selectedKey="{view>/halfYear}">
    <core:Item key="{view>key}" text="{view>text}" />
</Select>
```

`Graves.view.xml` needs `xmlns:core="sap.ui.core"` on the root if it is not already declared.

- [ ] **Step 2: Bind the generate dialog**

```xml
<Select id="genYear" width="100%" items="{generateDraft>/yearOptions}" selectedKey="{generateDraft>/year}">
    <core:Item key="{generateDraft>key}" text="{generateDraft>text}" />
</Select>
<Select id="genHalf" width="100%" selectedKey="{generateDraft>/half}">
    <core:Item key="1" text="H1" />
    <core:Item key="2" text="H2" />
</Select>
```

- [ ] **Step 3: Rewrite `onInit` and the readers**

```ts
public onInit(): void {
  this.bindODataRoot();
  this.getView()!.setModel(new JSONModel({ graves: [] }), "status");
  // half-year options are generated at runtime; default to the current
  // year's first half, matching the generate dialog's default half
  this.setState<GravesView>("view", {
    halfYearOptions: api.halfYearOptions(2, 1),
    halfYear: `${new Date().getFullYear()}-1`,
  });
  void this.refreshStatus();
}

/** "2026-1" -> [2026, 1] */
private selectedHalfYear(): [number, number] {
  const [year, half] = this.state<GravesView>("view")
    .getProperty("/halfYear")
    .split("-")
    .map(Number);
  return [year, half];
}
```

`refreshStatus` and `onBulkGraveInvoices` both call `selectedHalfYear()`. In `onGenerateInvoices`, replace the `yearSelect` loop with a `generateDraft` model on the dialog:

```ts
this._generateDialog.setModel(
  new TypedJSONModel<GenerateDraft>({
    // year options are generated at runtime; default to the current year
    yearOptions: api.yearOptions(1, 1),
    year: String(new Date().getFullYear()),
    half: "1",
  }),
  "generateDraft",
);
```

`onGenerateDialogSave` reads `year`/`half` from that model. `JSON.parse(finished.ResultJson ?? "{}") as api.GenerationResult` replaces the `as any`. Type the event handlers and `pollJob`'s fetch result.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/Graves.controller.ts`
Expected: typecheck passes; **0** errors (down from 59).

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS. The half-year select and the generate dialog are both driven there.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/Graves.controller.ts frontend/src/view/Graves.view.xml frontend/src/view/fragments/GenerateDialog.fragment.xml
git commit -m "refactor: bind the half-year and generate selects to a model"
```

---

### Task 8: InvoiceEdit and the shared quick-recipient dialog

This task deletes a real bug workaround. Read the spec's Problem section first.

**Files:**
- Modify: `frontend/src/controller/InvoiceEdit.controller.ts`, `frontend/src/view/fragments/InvoiceDialog.fragment.xml`, `frontend/src/view/fragments/RecipientQuickCreate.fragment.xml`
- Test: `e2e/tests/invoices.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `InvoiceDraft`, `DraftLine`, `QuickRecipientDraft`.
- Produces: `QuickRecipientDraft` wiring on the shared fragment. **Task 9 reuses this fragment and must use the same model name `quickRecipientDraft`.**

- [ ] **Step 1: Make the recipient select two-way**

In `InvoiceDialog.fragment.xml`, drop `mode: 'OneWay'` and the comment above it:

```xml
<Select id="recipient" width="20rem"
    items="{path: 'odata>/Recipients', parameters: {$orderby: 'Id desc'}}"
    selectedKey="{invoiceDraft>/recipientId}">
    <core:Item key="{odata>Id}" text="{odata>Name}" />
</Select>
```

- [ ] **Step 2: Bind the quick-create fragment**

```xml
<Input id="quickName" width="100%" value="{quickRecipientDraft>/name}" />
<TextArea id="quickAddress" rows="4" width="100%" value="{quickRecipientDraft>/address}" />
```

- [ ] **Step 3: Rewrite the controller**

`openInvoiceDialog` builds an `InvoiceDraft` with `recipientId` as a string (`recipientId != null ? String(recipientId) : ""`). `onInvoiceDialogSave` reads the recipient from the draft — the `byId` read and its five-line comment both go:

```ts
const recipientId = Number(draft.recipientId);
```

`onLineItemSelected` uses template-literal paths:

```ts
const index = Number(rowContext.getPath().split("/").pop());
const model = this._invoiceDialog.getModel("invoiceDraft") as TypedJSONModel<InvoiceDraft>;
model.setProperty(`/lines/${index}/itemNumber`, itemContext.getProperty("Number") as string);
model.setProperty(`/lines/${index}/description`, itemContext.getProperty("Description") as string);
// the item's default price is pre-filled and stays overridable; an item
// without one leaves the field empty so a price has to be typed
const defaultPrice = itemContext.getProperty("DefaultPrice") as number | null;
model.setProperty(`/lines/${index}/defaultPrice`, defaultPrice);
model.setProperty(`/lines/${index}/unitPrice`, defaultPrice != null ? Number(defaultPrice) : null);
```

`onNewRecipient` sets a fresh `quickRecipientDraft` instead of clearing two inputs. `onQuickRecipientSave` reads it, then:

```ts
const id = await api.createRecipient({ Name: draft.name, Address: draft.address });
// refresh the recipient list so the new row is included, then select it in
// the parent invoice dialog, which stays open
this.odata().refresh();
(this._invoiceDialog.getModel("invoiceDraft") as TypedJSONModel<InvoiceDraft>)
  .setProperty("/recipientId", String(id));
```

Type `_invoiceDialog` and `_quickDialog` as `Dialog`.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/InvoiceEdit.controller.ts`
Expected: typecheck passes; **0** errors (down from 71).

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop invoices.spec.ts`
Expected: PASS. Two tests matter most here: *"create invoice with an on-the-fly recipient (Story 3.2)"* covers the refresh-then-select flow, and *"the second invoice goes to the recipient the reopened dialog shows"* is the regression test for the workaround being deleted. If the latter fails, the two-way binding is not holding the selection — do not paper over it by restoring `getSelectedKey()`; find out why.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/InvoiceEdit.controller.ts frontend/src/view/fragments/InvoiceDialog.fragment.xml frontend/src/view/fragments/RecipientQuickCreate.fragment.xml
git commit -m "fix: bind the invoice dialog's recipient two-way instead of reading the control"
```

---

### Task 9: GraveEdit

The largest file (172 errors) and the last dialog. It shares `RecipientQuickCreate.fragment.xml` with Task 8, so the `quickRecipientDraft` model name is fixed.

**Files:**
- Modify: `frontend/src/controller/GraveEdit.controller.ts`, `frontend/src/view/fragments/GraveDialog.fragment.xml`, `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`
- Test: `e2e/tests/graves.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `GraveDraft`, `GraveInvoiceDraft`, `DraftLine`, `GraveRecipientRow`, `QuickRecipientDraft`, `api.GraveDetails`.
- Produces: nothing.

- [ ] **Step 1: Bind the recipient-link select**

Keep `id="linkRecipient"` — `graves.spec.ts:210` matches on it. Capture the display name from the `change` event rather than a control lookup:

```xml
<Select id="linkRecipient" width="12rem"
    items="{path: 'odata>/Recipients', parameters: {$orderby: 'Id desc'}}"
    selectedKey="{graveDraft>/linkRecipientId}" change=".onLinkRecipientChange">
    <core:Item key="{odata>Id}" text="{odata>Name}" />
</Select>
```

- [ ] **Step 2: Rewrite the link handlers**

```ts
public onLinkRecipientChange(event: Select$ChangeEvent): void {
  // the select writes its key into the draft; keep the name it shows alongside it
  const selected = event.getParameter("selectedItem");
  this.state<GraveDraft>("graveDraft").setProperty(
    "/linkRecipientName",
    selected?.getText() ?? "",
  );
}
```

`onAddRecipientLink` reads `linkRecipientId` / `linkRecipientName` from the draft, keeping the duplicate check and its `GraveRecipientLinkExists` error.

Because the dialog's model is set on the dialog rather than the view, `state<GraveDraft>("graveDraft")` will not find it — read it as `this._graveDialog.getModel("graveDraft") as TypedJSONModel<GraveDraft>` and add a small private accessor `graveDraft()` so the cast appears once.

- [ ] **Step 3: Convert the remaining draft plumbing**

`loadGraveDraft` returns `GraveDraft` (adding `linkRecipientId: ""`, `linkRecipientName: ""`). `onGraveItemSelected`, `onGraveInvoiceItemSelected`, `removeDraftRow`, `onDeleteGraveInvoiceLine`, `onAddItemLink` and `onAddGraveInvoiceLine` all switch to template-literal paths and typed rows. Keep the comment explaining why grave-invoice prices are not rounded — it documents a real migration constraint.

`onNewRecipient` / `onQuickRecipientSave` mirror Task 8, but select into `graveDraft>/linkRecipientId` and also set `/linkRecipientName`.

Type `_graveDialog`, `_graveInvoiceDialog`, `_quickDialog` as `Dialog`.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/GraveEdit.controller.ts`
Expected: typecheck passes; **0** errors (down from 172).

- [ ] **Step 5: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/GraveEdit.controller.ts frontend/src/view/fragments/GraveDialog.fragment.xml frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml
git commit -m "refactor: hold the grave dialogs' state in typed models"
```

---

### Task 10: ImportExport and Invoices

The two screens that keep a `byId` call. Both keepers get a comment.

**Files:**
- Modify: `frontend/src/controller/ImportExport.controller.ts`, `frontend/src/view/ImportExport.view.xml`, `frontend/src/controller/Invoices.controller.ts`
- Test: `e2e/tests/import-export.spec.ts`, `e2e/tests/invoices.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `setState`, `state`.
- Produces: nothing.

- [ ] **Step 1: Bind the problems panel**

```xml
<Panel id="problemsPanel" headerText="{i18n>importProblemsHeader}"
       visible="{= ${import>/problems}.length > 0 }">
```

`showProblems` loses its `setVisible` line and becomes a single `setProperty`.

- [ ] **Step 2: Comment the two survivors**

```ts
// FileUploader.clear() resets the native file input; its bindable `value`
// property does not, so this one needs the control itself.
(this.byId("archiveUpload") as FileUploader).clear();
```

```ts
// `search` is a custom OData query option on the list binding, not view
// state — no binding syntax expresses it, so reach the binding directly.
((this.byId("invoicesTable") as Table).getBinding("items") as ODataListBinding)
  .changeParameters({ search: term });
```

Note `as Table`, not `as any` — `no-explicit-any` forbids the old cast.

- [ ] **Step 3: Finish Invoices**

`afterInvoiceSaved` becomes `this.odata().refresh()`. Every `event: any` gets its `Control$XEvent` type.

- [ ] **Step 4: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/ImportExport.controller.ts src/controller/Invoices.controller.ts`
Expected: typecheck passes; **0** errors (down from 2 and 29).

- [ ] **Step 5: Run both E2E specs, unedited**

Run: `cd e2e && npm test -- --project=desktop import-export.spec.ts invoices.spec.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/controller/ImportExport.controller.ts frontend/src/view/ImportExport.view.xml frontend/src/controller/Invoices.controller.ts
git commit -m "refactor: bind the import problems panel and type the invoice list"
```

---

### Task 11: GraveDetail and GraveInvoiceDetail

Pure typing — neither controller has a `byId` call. 105 errors between them.

**Files:**
- Modify: `frontend/src/controller/GraveDetail.controller.ts`, `frontend/src/controller/GraveInvoiceDetail.controller.ts`
- Test: `e2e/tests/graves.spec.ts` (do not edit)

**Interfaces:**
- Consumes: `api.GraveDetails`, generic `readList<T>`.
- Produces: nothing.

- [ ] **Step 1: Type the route handlers and the model reads**

Replace `event: any` with the routing event type, `readList(...)` calls with `readList<RowType>(...)`, and every `as any` on a JSON model with the concrete model type. Where a detail page computes totals per revision, give the row shape a named interface next to the code that builds it.

- [ ] **Step 2: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src/controller/GraveDetail.controller.ts src/controller/GraveInvoiceDetail.controller.ts`
Expected: typecheck passes; **0** errors (down from 79 and 26).

- [ ] **Step 3: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop graves.spec.ts`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/controller/GraveDetail.controller.ts frontend/src/controller/GraveInvoiceDetail.controller.ts
git commit -m "refactor: type the grave detail pages"
```

---

### Task 12: InvoiceRevisions, InvoiceDetail and Component

The last 58 errors.

**Files:**
- Modify: `frontend/src/controller/InvoiceRevisions.controller.ts`, `frontend/src/controller/InvoiceDetail.controller.ts`, `frontend/src/Component.ts`
- Test: `e2e/tests/invoice-detail.spec.ts` (do not edit)

**Interfaces:**
- Consumes: generic `readList<T>`.
- Produces: nothing.

- [ ] **Step 1: Type them**

Same treatment as Task 11. `Component.ts`'s single error is the `getResourceBundle()` chain — it already casts to `ResourceModel`, so the fix is narrowing the bundle rather than adding a cast.

- [ ] **Step 2: Verify**

Run: `cd frontend && npm run typecheck && npx eslint src`
Expected: typecheck passes and **eslint reports 0 errors across the whole of `src`**. This is the first point at which the branch is fully green.

- [ ] **Step 3: Run the E2E spec, unedited**

Run: `cd e2e && npm test -- --project=desktop invoice-detail.spec.ts`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/controller/InvoiceRevisions.controller.ts frontend/src/controller/InvoiceDetail.controller.ts frontend/src/Component.ts
git commit -m "refactor: type the invoice detail and revision pages"
```

---

### Task 13: Full gate and documentation

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything.
- Produces: a branch ready to merge.

- [ ] **Step 1: Confirm no unintended `byId` survives**

Run: `cd frontend && grep -rn "this\.byId(" src --include="*.ts"`
Expected: exactly **two** call sites — `archiveUpload` in `ImportExport.controller.ts` and `invoicesTable` in `Invoices.controller.ts` — each preceded by the comment from Task 10. Any other hit is unfinished work. (Four comment mentions of `this.byId(...)` in the old dialog-loading comments should be gone; if one remains, the comment it explains is gone and the comment must go too.)

- [ ] **Step 2: Run the whole frontend gate**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three pass; eslint reports 0 problems.

- [ ] **Step 3: Run the whole E2E suite on its own stack**

Run: `cd e2e && npm test`
Expected: PASS, all projects. This builds and starts `docker-compose.e2e.yml` on port 8081. It must not be pointed at the dev stack.

- [ ] **Step 4: Document the new commands in `CLAUDE.md`**

Under "Running the parts", extend the frontend line:

```markdown
- **Frontend checks**: `cd frontend && npm run typecheck`, `npm run lint`
  (ESLint, type-aware rules), `npm run ui5lint` (UI5 linter: deprecated APIs,
  accessibility, manifest).
```

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: record the frontend lint commands"
```

---

## Self-review

**Spec coverage.** Every spec section maps to a task: convention → 2; Items → 4; Recipients → 5; Settings → 6; Graves → 7; InvoiceEdit → 8; GraveEdit → 9; ImportExport and Invoices → 10; "what keeps byId" → 10 (comments) and 13 (verification); typing → 2, 3, 11, 12; tooling → 1 and 13; verification → each task plus 13.

**Type consistency.** `setState` / `state` / `readList<T>` are defined in Task 2 and used with those exact names throughout. `quickRecipientDraft` is named identically in Tasks 8 and 9 because the fragment is shared — this is the plan's one cross-task coupling and both tasks call it out. `DraftLine` is shared by `InvoiceDraft`, `GraveDraft.Items` and `GraveInvoiceDraft.lines`; the three dialogs collect the same fields, so one interface serves all three.

**Known open question.** Task 8's `odata().refresh()` cannot be awaited, so selecting the newly created recipient relies on the `Select` re-applying its bound `selectedKey` when refreshed items arrive. `invoices.spec.ts` Story 3.2 is the check. If it fails, the fallback is the list binding's own awaitable `refresh()` via `byId` — a third documented survivor, not a silent revert.
