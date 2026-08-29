# View state in models, not in controls

**Date**: 2026-08-29
**Branch**: `refactor/ui5-json-models`

## Problem

Fifty-three `this.byId(...)` calls across eight controllers reach into controls to
read and write what the user typed. A controller that does

```ts
Number: (this.byId("itemNumber") as Input).getValue(),
Description: (this.byId("itemDescription") as Input).getValue(),
```

has put its state in the control tree. The cast is the tell: every one of these
sites needs `as Input`, `as Select`, `as any`, because `byId` returns the base
type and the controller is reaching past the framework's own binding layer to
get at a value the framework would happily have handed it.

The cost is not theoretical. `InvoiceEdit.onInvoiceDialogSave` reads the
recipient with `getSelectedKey()` and carries this comment:

> The dialog is built once and reused, so its select keeps the selection of the
> previous open while the draft of this open starts out empty — reading the
> draft would send recipient 0 and fail with `RecipientNotFound`.

That is a bug worked around at the call site. The select is bound `OneWay`
precisely so the control can hold state the draft does not. Binding it `TwoWay`
removes the divergence and the workaround together.

The repository already has the right pattern in `invoiceDraft`, `graveDraft` and
`graveInvoiceDraft`. This design does not invent a convention; it finishes
applying the one that is already here.

## Scope

Three changes, one branch:

1. Move view state out of controls into `TypedJSONModel` models bound from XML.
2. Adopt the official UI5 TypeScript ESLint configuration and `@ui5/linter`,
   and fix what they report — including the 116 `any` usages in `src/`.
3. Leave `byId` in place at the two sites where no binding expresses the intent.

Out of scope: backend, E2E specs (they must pass unchanged — that is the point),
and the single-container-image work in flight on `main`.

## Constitution

Principle VI (OpenUI5, no SAPUI5) and Principle VII (English codebase, i18n UI)
both hold — no SAPUI5 dependency is added, and no user-visible string moves out
of the resource bundles.

Deviation **D-002** ("every read stays on the OData model") constrains the
Settings page: its form must be filled from the OData model, not from `fetch`.
The design uses `bindContext("/Settings").requestObject()` for that.

`@ui5/linter` depends on `@sapui5/types` internally. That is a transitive
dependency of a development tool, not a frontend delivery dependency, so
Principle VI is unaffected — the shipped application still contains only
OpenUI5.

## Convention

Interfaces live in a new `src/model/state.ts`. Two kinds of model:

| Kind | Name | Holds |
|---|---|---|
| Page state | `view` | Search terms, toolbar selections — one per page |
| Dialog state | `<thing>Draft` | Everything a dialog collects until it is saved |

Both are `TypedJSONModel<T>`, available since UI5 1.140 and present in the
pinned 1.151.0. It checks path literals against the interface, so
`setProperty("/lines/0/unitPrce", …)` fails to compile rather than silently
writing a new property.

**Control `id` attributes stay.** This refactor removes `byId` *calls*, not
element IDs. `graves.spec.ts:210` matches `.sapMSlt[id$='linkRecipient']`, and
roughly thirty-six `getByLabel(...)` locators across the suite resolve through
`labelFor`. Removing an ID breaks tests that have nothing to do with this work.

## Per-screen changes

### Items

- `view` model `{ searchTerm: string | null }`. The table declares
  `boundFilters` over `Number` and `Description` with `and: false`;
  `onSearch` writes `term || null`. A `null` value makes the filter *neutral*
  (`Filter.isNeutral`), so clearing the field drops the filter with no
  `if (!term)` branch.
- `itemDraft` model `{ id, number, description, vatRate, defaultPrice }`,
  two-way bound in `ItemDialog.fragment.xml`. Removes twelve `byId` sites.
- Dialog title becomes an expression binding, mirroring `GraveDialog`:
  `title="{= ${itemDraft>/id} === null ? ${i18n>createItem} : ${i18n>editItem} }"`.
- VAT-rate list refresh and post-save table refresh become `this.odata().refresh()`.

### Recipients

Same shape: `view>/searchTerm` with a single `boundFilters` entry on `Name`,
a `recipientDraft` model, an expression-bound title, `odata().refresh()`.

### Settings

- `settingsDraft` model `{ stampWord, endingText, language }`, filled in
  `onInit` from `this.odata().bindContext("/Settings").requestObject()`.
  The form binds two-way to it, replacing three `mode: 'OneWay'` OData bindings
  that the controller then read back through `byId`.
- The new-VAT-rate input binds to `view>/newVatRate`.

### Graves

- `view` model `{ halfYearOptions, halfYear }`. The toolbar `Select` binds
  `items="{view>/halfYearOptions}"` and `selectedKey="{view>/halfYear}"`,
  deleting the `removeAllItems()` / `addItem(new Item(...))` loop in `onInit`
  and the three `getSelectedKey()` reads.
- `generateDraft` model `{ yearOptions, year, half }` for the generate dialog.
- Grave search is unchanged: it calls a backend endpoint (D-001
  `SearchGraves`), not a list binding, so no filter applies.

### InvoiceEdit

- The recipient `Select` binds `selectedKey` **two-way** to
  `invoiceDraft>/recipientId`. This deletes the `byId` read *and* the
  reused-dialog workaround described under Problem. The draft holds the key as
  a string, matching the existing `itemId` convention, and converts on save.
- `quickRecipientDraft` model `{ name, address }` for the shared quick-create
  fragment.
- After a quick create: `odata().refresh()`, then set `invoiceDraft>/recipientId`.

### GraveEdit

- The `linkRecipient` `Select` binds `selectedKey` two-way to
  `graveDraft>/linkRecipientId`. The display name for the added row comes from
  the `change` event's `selectedItem`, not from a control lookup.
- Shares `quickRecipientDraft` with `InvoiceEdit` — the fragment is shared, so
  the model name must be too.

### ImportExport

- The problems panel binds `visible="{= ${import>/problems}.length > 0}"`,
  removing the `setVisible` call and half of `showProblems`.

### Invoices

- `afterInvoiceSaved` becomes `odata().refresh()`.

### What keeps `byId`

Two sites, each with a comment saying why:

| Site | Why |
|---|---|
| `Invoices` — `invoicesTable` `.changeParameters({ search })` | A custom OData query option on a list binding. Not view state; no binding syntax expresses it. |
| `ImportExport` — `archiveUpload.clear()` | `FileUploader.clear()` resets the native file input. The `value` property is bindable but does not do this. |

Reaching a list binding through its control is ordinary UI5. The anti-pattern is
storing *state* in controls, and after this change none is.

## Typing

`recommendedTypeChecked` makes the 116 `any` usages errors. The refactor deletes
many of them by construction; the rest are typed:

- `_dialog: any` → `Dialog`
- `event: any` → the `Control$XEvent` types the UI5 guidelines require
  (`Button$PressEvent`, `SearchField$SearchEvent`, `Select$ChangeEvent`, …)
- `api.fetchGraveDetails(): Promise<any>` → a `GraveDetails` interface
- `api.toJson(): Promise<any>` → `Promise<unknown>`
- `Base.readList` → generic `readList<T>(…): Promise<T[]>`
- `getModel("i18n") as any` → `ResourceModel`
- The generate job's `ResultJson` → a typed shape

This reaches the seven controllers with no `byId` at all — `GraveDetail`,
`GraveInvoiceDetail`, `InvoiceDetail`, `InvoiceRevisions`, `Home`, `App`,
`Base`.

## Tooling

`frontend/eslint.config.mjs`, mirroring `SAP-samples/ui5-typescript-helloworld`:
`typescript-eslint` `recommended` plus `recommendedTypeChecked`, browser globals
with `sap: "readonly"`, `parserOptions.project`. There is no official UI5 ESLint
plugin; the UI5 team points at TypeScript defaults and ships UI5-specific
checking as `@ui5/linter`.

New dev dependencies: `eslint`, `globals`, `typescript-eslint`, `@ui5/linter`.
New scripts: `"lint": "eslint src"`, `"ui5lint": "ui5lint"`.

Both tools are scoped to `src`. `frontend/dist/` contains the entire OpenUI5
runtime and must be excluded from both, or the lint run is meaningless and slow.

`ui5lint` findings (deprecated APIs, accessibility, manifest) are fixed in the
same branch.

## Verification

In order, all must pass:

1. `cd frontend && npm run typecheck`
2. `cd frontend && npm run lint`
3. `cd frontend && npm run ui5lint`
4. `cd e2e && npm test`

Step 4 is the real safety net. The suite already drives every dialog this
refactor touches, and it runs against its own stack on port 8081 — never the dev
stack, whose database holds the user's data.

## Risks

**Bound filters inside an OR multi-filter — resolved.** Confirmed against the
pinned 1.151.0 sources rather than assumed:
`ManagedObjectBindingSupport._createBoundFilter` recurses into
`oFilter.aFilters` for a multi-filter, and `Filter.removeAllNeutrals` recurses
likewise and returns `undefined` when every child is neutral. So an OR of two
bound `Contains` filters whose value is `null` collapses to no filter at all,
which is exactly the cleared-search behaviour Items needs. No fallback required.

**`TypedJSONModel` row paths — resolved.** The row updates in
`onLineItemSelected` build a path at runtime. String concatenation
(`path + "/itemNumber"`) does not satisfy the typed path parameter, but a
template literal does: `` `/lines/${index}/itemNumber` `` has type
`` `/lines/${number}/itemNumber` ``, which is a member of
`AbsoluteBindingPath<InvoiceDraft>`. Verified with `tsc --noEmit`, including
four negative controls — a misspelt path, a misspelt row property, a wrong
value type and a wrong read type all fail to compile.

**`ODataModel.refresh()` returns `void` — resolved.** The quick-create flow
(refresh the recipient list, then select the new recipient) works: the `Select`
re-applies its bound `selectedKey` when the refreshed items arrive.
`invoices.spec.ts` Story 3.2 passes.

**What the two-way binding did not fix, and how it was.** Binding the recipient
`selectedKey` two-way was necessary but not sufficient. A reopened dialog starts
on an empty draft, and the select's `forceSelection` then picks its first item —
a control-side fallback UI5 deliberately does not write into the bound model, so
the save still sent recipient 0. "The second invoice goes to the recipient the
reopened dialog shows" caught this.

The fix is not to read the control again. It is to make the default explicit:
`BaseController.newestRecipientId()` chooses it, newest first, which is the
order the selects list recipients in. The application now decides what is
preselected instead of inheriting a control's fallback, which is the same
principle as the rest of this change — the model is what says what is on screen.
The grave dialog's recipient-link select uses the same helper.

**Settings changes binding direction.** Moving from `OneWay` OData bindings to a
JSON draft changes when the form is populated. `masterdata.spec.ts` covers save
and reload.

## Branch divergence

`main` carries uncommitted work consolidating the two container images into one
(root `Dockerfile`, `docker-compose.e2e.yml`, constitution 3.0.0, `e2e/stack.ts`).
This branch is cut from `HEAD` and does not include it. The two do not overlap —
this branch touches `frontend/src` and frontend tooling only — but the E2E runs
here build the committed two-image stack.
