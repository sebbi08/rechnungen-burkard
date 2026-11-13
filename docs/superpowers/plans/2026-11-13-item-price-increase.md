# Item Price Increase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise an item's default price and the graves' own prices for it by a percentage, from the item edit dialog, with per-price rows, adjustable end prices, live split checks and all-or-nothing saving (issue #67).

**Architecture:** A new backend service `ItemPriceIncreaseService` resolves the rows to grave links, recomputes each affected grave's rounding corrections with the existing `GraveBillingCheck.Evaluate`, and either reports (preview) or writes everything in one transaction (apply). Two plain POST routes expose it, like `PreviewGraveBilling` and `SaveGrave` (not on the EDM, so no `gen:odata`). The frontend adds a popover dialog opened from `ItemDialog.vue`, which proposes prices client-side, previews through the server on a 300 ms debounce, and applies the increase on the dialog's save.

**Tech Stack:** .NET 10, ASP.NET Core OData controllers, EF Core SQLite, xUnit; Vue 3, PrimeVue, TanStack Query, vue-i18n; Playwright.

**Spec:** `docs/superpowers/specs/2026-11-13-item-price-increase-design.md`. Read it with this plan. Its section 7 settles the edge cases referred to below as "spec 7.n".

## Global Constraints

- No migration and no EDM change. Only `InvoiceItems.DefaultPrice`, `GraveItems.UnitPrice` and `GraveBillingCorrections` rows change.
- Prices are whole cents. A new price must be ≥ 0,01 with at most two decimals (`GraveBilling.IsWholeCents`).
- Proposed price = old × (1 + p/100), rounded to the cent, midpoint away from zero, computed in integer cents (no floating-point midpoint drift).
- "Billing the default" compares the number only (spec 7.1). A gross link keeps `PriceIncludesVat` when raised.
- Corrections for an affected grave are exactly what `SaveGrave` would store for it with the new prices (spec 7.3). The current half-year comes from `IClock` (spec 7.6).
- Deleted graves are never changed. Cooperative graves are raised, never evaluated, and keep no corrections.
- Invoices, quotes and grave invoices already generated are never touched.
- User-facing text is German. Error texts live in `frontend/src/i18n/de.json` as `error<Code>` / `error<Code><argCount>` (see `useErrorToast.ts`).
- Backend tests: `scripts/dotnet.sh test tests/Invoicing.IntegrationTests` (paths relative to `backend/`; this worktree has its own `backend/obj`, so the dev container need not be stopped). Frontend: `cd frontend && npm run typecheck && npm run lint && npm run build`. E2E: `cd e2e && npm test -- tests/price-increase.spec.ts` (own stack on 8081, never the dev stack).
- Conventional Commits, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. No body line may start with `word:`.

## Review Focus

1. **A raised grave that bills other items too.** Its check must include the other items' unchanged lines, not only the raised one, or a correction is missed. Pinned in Task 1 (`A_raised_grave_is_checked_with_its_other_items`). A grave cannot link the same item twice (`GraveItemLinkExists`), so a grave sits in at most one row.
2. **An own price that after raising equals the new default.** Apply must succeed, and `ItemGraves` must then list the grave under the default. Pinned in Task 2 (`An_own_price_raised_onto_the_new_default_is_listed_under_the_default`).
3. **The percentage changed after an end price was overwritten.** All proposals are recomputed and the manual edit is gone (spec 3.2). Pinned in Task 3's E2E step.
4. **Saving the dialog twice after an apply failure.** The item fields are saved and the increase stays pending. A second save must not double-raise, and the server's `PricesChanged`/`CorrectionsChanged` guard holds. Pinned in Task 4 (`A_second_apply_of_the_same_increase_is_refused`).
5. **Proposals at a half cent**, e.g. 10,10 € + 5 % = 10,605 → 10,61 (float math gives 10,60). Pinned in Task 3 by the E2E case 10,10 → 10,61.

---

### Task 1: Spec text, shared correction builder, preview endpoint

**Files:**

- Modify: `docs/spec.md` (FR-009b around line 623, new FR-009c after it, acceptance scenario under the invoice-item user story near lines 75–85, edge case near line 526)
- Create: `backend/src/Invoicing.Api/Services/GraveCorrections.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs` (the loop that adds `GraveBillingCorrection`s after the links, ~line 225)
- Create: `backend/src/Invoicing.Api/Services/ItemPriceIncreaseService.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/ItemPriceIncreaseController.cs`
- Modify: `backend/src/Invoicing.Api/Program.cs` (register the service next to `GraveService`, line ~51)
- Test: `backend/tests/Invoicing.IntegrationTests/ItemPriceIncreaseTests.cs`

**Interfaces:**

- Produces (`GraveCorrections.cs`, static class `GraveCorrections`):
  - `public static List<GraveBillingCorrection> Build(long graveId, GraveBillingEvaluation evaluation)`. This is the mapping `GraveService` does today (Kind, Year, Half from `invoice.Example`, the rest from each `BillingCorrection`). `GraveService` switches to it.
- Produces (`ItemPriceIncreaseService.cs`):
  - `public sealed class PriceIncreaseRowInput { bool IsDefault; decimal OldPrice; bool PriceIncludesVat; decimal NewPrice; List<long>? GraveIds; }`
  - `public sealed class PriceIncreaseCorrectionInput { long GraveId; string Kind; int? Year; int Half; decimal VatRate; long ItemId; decimal CorrectedPrice; }`
  - `public sealed class ItemPriceIncreaseRequest { long ItemId; List<PriceIncreaseRowInput>? Rows; List<PriceIncreaseCorrectionInput>? Corrections; }`
  - `public sealed record PriceIncreaseProblem(long GraveId, string GraveNumber, string Invoice, decimal VatRate)`
  - `public sealed record PriceIncreaseCorrection(long GraveId, string GraveNumber, string Kind, int? Year, int Half, string Invoice, decimal VatRate, long ItemId, string ItemNumber, decimal EnteredPrice, decimal CorrectedPrice)`
  - `public sealed record PriceIncreasePreview(IReadOnlyList<IReadOnlyList<PriceIncreaseProblem>> RowProblems, IReadOnlyList<PriceIncreaseCorrection> Corrections)`. `RowProblems[i]` belongs to `Rows[i]`.
  - `public static PriceIncreaseProblem? ProblemOf(long graveId, string graveNumber, GraveBillingEvaluation evaluation)`: null when `evaluation.Ok`, else `Invoice = HalfYearBilling.Label(FirstFailing.Example)` and `VatRate = FirstFailing.Problems[0].VatRate`.
  - `public Task<PriceIncreasePreview> PreviewAsync(ItemPriceIncreaseRequest request, CancellationToken ct)`. Throws `ValidationException` for an invalid request (see Step 3) and `ItemNotFoundException` (a new `sealed class : Exception` in the same file) for an unknown item.
- Route: `POST /odata/PreviewItemPriceIncrease`, body = `ItemPriceIncreaseRequest`. 200 → `{ Rows: [{ Problems: [{GraveId, GraveNumber, Invoice, VatRate}] }], Corrections: [...PriceIncreaseCorrection fields] }`, serialized PascalCase (`ODataJsonDefaults.PascalCase`). 400 → `{ error: { code, message, args? } }`. 404 for an unknown item.

- [ ] **Step 1: Write the spec text in `docs/spec.md`**

  - FR-009b gets the sentence: "A default raised through a price increase (FR-009c) moves the graves billing the default along with it."
  - FR-009c condenses the design's sections 3.1–3.6 into requirement form: per-price rows, checkboxes, a proposal rounded to two decimals that can be overwritten, a live check that lists corrections and marks rows no correction can fix, nothing written before the dialog's save, and all or nothing on save.
  - One acceptance scenario: "Jährliche Grabpflege" + 5 %, one row unticked, one end price overwritten, saved, so the graves bill the new prices.
  - One edge case: a new price that a multi-recipient grave cannot be corrected to marks the row and blocks applying.

- [ ] **Step 2: Write the failing tests in `ItemPriceIncreaseTests.cs`**

  Seed the way `ItemGravesTests` does: VAT rate 19, an item through `/odata/InvoiceItems`, graves through `/odata/SaveGrave`, term `2026-01-01`–`2040-12-31`, `ApiTestHost` default clock. A helper `LinkAsync(client, itemId, graveNumber, decimal? unitPrice, bool gross = false, int recipients = 1)` creates `recipients` recipients. A grave whose price needs a correction at save time sends the corrections the preview names (as `SaveGraveTests` does). Item default price 120,00.

  ```csharp
  [Fact] public async Task Preview_lists_the_corrections_a_raised_price_needs()
  // G-B: own 95,00, 2 recipients. Row {IsDefault=false, OldPrice=95, NewPrice=99.75}.
  // Assert: RowProblems[0] empty; Corrections not empty, every entry has GraveNumber "G-B",
  // ItemId == itemId, EnteredPrice 99.75m, CorrectedPrice 99.76m.

  [Fact] public async Task Preview_writes_nothing()
  // after the preview above, GET /odata/ItemGraves still shows 95,00 for G-B and no stored corrections changed.

  [Fact] public async Task Preview_leaves_one_recipient_graves_and_cooperative_graves_without_corrections()
  // G-A: own 95,00, 1 recipient; G-K: cooperative (isCooperative=true, contractNumber "V-1", no recipients) own 95,00.
  // Row 95 → 99.75: Corrections empty, RowProblems[0] empty.

  [Fact] public async Task A_raised_grave_is_checked_with_its_other_items()
  // A second all-year item "U-2" at 19 %, 40,01. G-S, 2 recipients, links: our item own 95,00 and
  // U-2 own 40,01 (135,01 is odd, so G-S is saved with its confirmed correction).
  // Row 95 → 99.75: 139,76 now divides on its own, so the preview lists no correction for G-S.
  // Assert: Corrections has no entry for G-S, RowProblems[0] empty. (Checked alone, 99,75 would
  // have needed one, so this fails if the other item's line is left out.)

  [Fact] public void ProblemOf_names_the_first_failing_invoice()
  // Build a GraveBillingEvaluation by hand: one BillingExampleResult with
  // Example = new BillingExample(BillingExampleKind.FullFirstHalf, <a BillingPeriod for H1 2027>),
  // Problems = [new GraveShareProblem(19m, GraveShareProblemKind.NotDivisible, 9975, [])].
  // Assert ProblemOf(7, "G-X", evaluation) == new(7, "G-X", HalfYearBilling.Label(example), 19m),
  // and ProblemOf on an Ok evaluation is null.

  [Fact] public async Task Preview_refuses_an_own_row_at_the_default_price()   // 400 PricesChanged
  [Fact] public async Task Preview_refuses_a_default_row_on_an_item_without_default() // 400 PricesChanged
  [Fact] public async Task Preview_refuses_a_price_with_three_decimals()       // 400 InvalidPrice
  [Fact] public async Task Preview_refuses_a_price_below_one_cent()            // NewPrice 0 → 400 InvalidPrice
  [Fact] public async Task Preview_of_an_unknown_item_is_not_found()           // 404
  ```

- [ ] **Step 3: Run them to verify they fail**

  Run: `scripts/dotnet.sh test tests/Invoicing.IntegrationTests --filter ItemPriceIncreaseTests`
  Expected: build error or FAIL (types and route missing).

- [ ] **Step 4: Extract `GraveCorrections.Build` and switch `GraveService` to it**

  This is behaviour-neutral. `SaveGraveTests` and `GraveBillingCorrectionTests` must stay green unchanged.

- [ ] **Step 5: Implement `ItemPriceIncreaseService.PreviewAsync` and the controller route**

  Validation, in this order:
  - missing body or empty `Rows`: `InvalidRequest`;
  - two rows with the same key: `InvalidRequest`. The key is (IsDefault) for the default row, and (OldPrice, PriceIncludesVat) for own rows;
  - a `NewPrice` that is < 0,01 or not whole cents: `InvalidPrice`;
  - unknown item: 404;
  - a default row when `item.DefaultPrice` is null or ≠ `OldPrice`: `PricesChanged`;
  - an own row whose `OldPrice` equals `item.DefaultPrice`: `PricesChanged`.

  Membership (non-deleted graves only):
  - The default row holds the links with `UnitPrice == null` or `UnitPrice == item.DefaultPrice`.
  - An own row holds the links with `UnitPrice == OldPrice && PriceIncludesVat == row.PriceIncludesVat`.

  Evaluation:
  - The affected graves are those with at least one link in a ticked row.
  - For each affected grave that is non-cooperative, has recipients and has links: build all its links of every item in link-id order with `GraveBilling.LinkFor`. Substitute the new price on raised links: the default row raises null links through the item default as N and equal-to-default links as N; own rows set N. Then `GraveBillingCheck.Evaluate(links, grave.StartDate, grave.EndDate, recipientCount, HalfYearBilling.HalfYearOf(clock.GetLocalNow()))`.
  - A failing grave's `ProblemOf` goes into the problems of every ticked row it has a link in.
  - Corrections come from the evaluation's `Invoices[].Corrections`, labelled with `HalfYearBilling.Label(example)`.

  The controller maps `ValidationException` to `400 { error: { code, message } }` and `ItemNotFoundException` to 404, the way `GravesController.Save` does.

- [ ] **Step 6: Run the tests to verify they pass, then the whole backend suite**

  Run: `scripts/dotnet.sh test tests/Invoicing.IntegrationTests --filter ItemPriceIncreaseTests`, then `scripts/dotnet.sh test Invoicing.sln`
  Expected: all PASS.

- [ ] **Step 7: Commit**

  ```bash
  git add docs/spec.md backend/src/Invoicing.Api backend/tests/Invoicing.IntegrationTests/ItemPriceIncreaseTests.cs
  git commit -m "feat(api): preview an item price increase with the corrections it needs"
  ```

---

### Task 2: Apply endpoint

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/ItemPriceIncreaseService.cs`
- Modify: `backend/src/Invoicing.Api/OData/Controllers/ItemPriceIncreaseController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/ItemPriceIncreaseTests.cs`

**Interfaces:**

- Consumes: everything Task 1 produced, and `GraveCorrections.Build`.
- Produces:
  - `public Task ApplyAsync(ItemPriceIncreaseRequest request, CancellationToken ct)`;
  - a new `PriceIncreaseDoesNotDivideException : ValidationException` with code `PricesDoNotDivide` and `Args = [graveNumber, invoiceLabel]`;
  - the route `POST /odata/ApplyItemPriceIncrease`. It returns 204 on success, and `400 { error: { code, message, args? } }` with the codes `InvalidRequest`, `InvalidPrice`, `PricesChanged`, `PricesDoNotDivide` (args = [grave number, invoice label]) and `CorrectionsChanged`, or 404.

- [ ] **Step 1: Write the failing tests**

  ```csharp
  [Fact] public async Task Apply_raises_the_default_and_the_ticked_own_prices()
  // Default 120; G-A null link; G-D own 120,00 gross; G-B own 95,00 (1 recipient); G-C own 150,00.
  // Rows: default 120→126 (GraveIds [A, D]), own 95→99.75 (GraveIds [B]); 150 not sent.
  // Assert via db (host.Services scope, AppDbContext): item.DefaultPrice 126; G-A link UnitPrice null;
  // G-D link UnitPrice 126 and PriceIncludesVat true; G-B 99.75; G-C 150.

  [Fact] public async Task Apply_stores_the_confirmed_corrections()
  // G-B with 2 recipients; Corrections = the preview's list. After apply: G-B's GraveBillingCorrections
  // all have CorrectedPrice 99.76m, and GraveCorrectionCheck.OutdatedGraveNumbersAsync(2027, 1) is empty.

  [Fact] public async Task Apply_removes_corrections_no_longer_needed()
  // G-F own 99,99 with 2 recipients (saved with its correction to 100,00). Row 99.99 → 100.00 overwritten.
  // After apply: G-F has no stored corrections.

  [Fact] public async Task Apply_leaves_deleted_graves_alone_and_raises_cooperative_ones()
  // G-E own 95 deleted via /odata/DeleteGrave; G-K cooperative own 95. Row 95 → 99.75 (GraveIds [K]).
  // G-E stays 95, G-K becomes 99.75 with no corrections.

  [Fact] public async Task An_own_price_raised_onto_the_new_default_is_listed_under_the_default()
  // G-A null link, G-P own 105,00. Rows: default 120→126, own 105→126 (overwritten).
  // GET /odata/ItemGraves: a single IsDefault group at 126 containing G-A and G-P.

  [Fact] public async Task Apply_refuses_when_a_row_lost_a_grave_since_the_preview()
  // Preview, then SaveGrave changes G-B to 96,00, then apply with GraveIds [B] → 400 PricesChanged;
  // item default and every link unchanged.

  [Fact] public async Task Apply_refuses_unconfirmed_corrections()
  // G-B 2 recipients, Corrections empty → 400 CorrectionsChanged; nothing written.

  [Fact] public async Task A_second_apply_of_the_same_increase_is_refused()
  // Apply once (200/204), send the identical request again → 400 PricesChanged; prices raised only once.

  [Fact] public async Task A_typed_default_still_keeps_graves_on_the_old_price()
  // Regression for FR-009b: PATCH DefaultPrice 120→130 → G-A's link now carries 120 as its own price.
  ```

- [ ] **Step 2: Run them to verify they fail**

  Run: `scripts/dotnet.sh test tests/Invoicing.IntegrationTests --filter ItemPriceIncreaseTests`
  Expected: FAIL (404 on the new route).

- [ ] **Step 3: Implement `ApplyAsync` and the route**

  The checks are Task 1's validation and evaluation, plus:
  - for each row, the distinct grave ids of its members must equal `GraveIds` as a set, else `PricesChanged`;
  - any failing grave raises `PriceIncreaseDoesNotDivideException` (the first failing grave by grave number);
  - for each affected grave, `GraveBillingCheck.SameCorrections` between its computed keys and the confirmed keys that carry its `GraveId` (an unknown `Kind` matches nothing, as in `GraveService`). Confirmed corrections for graves outside the affected set also mean `CorrectionsChanged`.

  Then, in one transaction: set `item.DefaultPrice` if the default row is present, then update the links as in Task 1's substitution (null links stay null). Remove each affected grave's `GraveBillingCorrections` and add `GraveCorrections.Build(grave.Id, evaluation)`, then save and commit. Do not go through the `InvoiceItems` PATCH, so FR-009b's old-default write does not run.

- [ ] **Step 4: Run the tests to verify they pass, then the backend suite**

  Run: `scripts/dotnet.sh test tests/Invoicing.IntegrationTests --filter ItemPriceIncreaseTests`, then `scripts/dotnet.sh test Invoicing.sln`
  Expected: all PASS.

- [ ] **Step 5: Commit**

  ```bash
  git add backend/src/Invoicing.Api backend/tests/Invoicing.IntegrationTests/ItemPriceIncreaseTests.cs
  git commit -m "feat(api): apply an item price increase and its corrections in one transaction"
  ```

---

### Task 3: The "Preise erhöhen" popover

**Files:**

- Create: `frontend/src/lib/priceIncrease.ts`
- Modify: `frontend/src/api/endpoints.ts` (next to `getItemGraves`)
- Create: `frontend/src/composables/useItemPriceIncreasePreview.ts`
- Create: `frontend/src/components/ItemPriceIncreaseDialog.vue`
- Modify: `frontend/src/components/ItemDialog.vue` (button in the "Gräber mit dieser Leistung" heading, pending state, old → new display, locked default field, "Erhöhung verwerfen")
- Modify: `frontend/src/i18n/de.json`
- Test: `e2e/tests/price-increase.spec.ts`

**Interfaces:**

- Consumes: the preview route from Task 1, and `getItemGraves` / `ItemGraves` / `ItemGraveGroup` in `endpoints.ts`.
- Produces (`lib/priceIncrease.ts`):
  - `export function raiseByPercent(price: number, percent: number): number`. Uses integer math: `cents = Math.round(price * 100)`, `basis = Math.round(percent * 100)`, `raised = cents * (10000 + basis)`, result `= Math.floor((raised + 5000) / 10000) / 100`. Prices are non-negative, so the midpoint rounds up.
  - `export interface IncreaseRow { isDefault: boolean; oldPrice: number; priceIncludesVat: boolean; graveIds: number[]; graveCount: number; ticked: boolean; newPrice: number }`
  - `export function increaseRows(graves: ItemGraves): IncreaseRow[]`. The default row comes first when `DefaultPrice != null`, merging every `IsDefault` group (its `graveIds` are the distinct `Graves[].Id`, and it exists even with no graves). Then one row per non-default group with a non-null `UnitPrice`, in the server's order. Every row starts ticked, with `newPrice = oldPrice`.
- Produces (`endpoints.ts`):
  - `export interface PriceIncreaseProblem { GraveId: number; GraveNumber: string; Invoice: string; VatRate: number }`
  - `export interface PriceIncreaseCorrection { GraveId: number; GraveNumber: string; Kind: string; Year: number | null; Half: number; Invoice: string; VatRate: number; ItemId: number; ItemNumber: string; EnteredPrice: number; CorrectedPrice: number }`
  - `export interface PriceIncreasePreview { Rows: { Problems: PriceIncreaseProblem[] }[]; Corrections: PriceIncreaseCorrection[] }`
  - `export function previewItemPriceIncrease(itemId: number, rows: IncreaseRow[]): Promise<PriceIncreasePreview>`. It sends only ticked rows as `{ IsDefault, OldPrice, PriceIncludesVat, NewPrice, GraveIds }`.
  - `export function applyItemPriceIncrease(itemId: number, rows: IncreaseRow[], corrections: PriceIncreaseCorrection[]): Promise<void>`. Used in Task 4.
- Produces (`ItemPriceIncreaseDialog.vue`):
  - props `{ itemId: number; graves: ItemGraves; initial: PendingIncrease | null }`;
  - `v-model:visible`;
  - emits `apply(pending: PendingIncrease)`;
  - `PendingIncrease = { percent: number | null; rows: IncreaseRow[]; corrections: PriceIncreaseCorrection[] }`, exported from `lib/priceIncrease.ts`.
- i18n keys (exact German):

  | Key                         | Text                                                                                          |
  | --------------------------- | --------------------------------------------------------------------------------------------- |
  | `priceIncrease`             | `Preise erhöhen`                                                                              |
  | `priceIncreasePercent`      | `Erhöhung in %`                                                                               |
  | `priceIncreaseRounded`      | `Neue Preise werden auf zwei Stellen gerundet.`                                               |
  | `priceIncreaseOld`          | `Bisher`                                                                                      |
  | `priceIncreaseNew`          | `Neu`                                                                                         |
  | `priceIncreaseGraves`       | `{0} Gräber`                                                                                  |
  | `priceIncreaseProblem`      | `Geht bei Grab {0} ({1}) nicht auf. Bitte anderen Preis eingeben.`                            |
  | `priceIncreaseMoreProblems` | `und {0} weitere`                                                                             |
  | `priceIncreaseCorrections`  | `Rundungsanpassung bei {0} Gräbern`                                                           |
  | `priceIncreaseApply`        | `Übernehmen`                                                                                  |
  | `priceIncreaseDiscard`      | `Erhöhung verwerfen`                                                                          |
  | `errorPricesChanged`        | `Die Preise haben sich inzwischen geändert. Bitte „Preise erhöhen“ erneut öffnen.`            |
  | `errorPricesDoNotDivide2`   | `Bei Grab „{0}“ lässt sich die Rechnung „{1}“ nicht gleichmäßig auf die Empfänger aufteilen.` |

  For a `CorrectionsChanged` refusal of the apply, the existing `errorCorrectionsChanged` text is reused.

- [ ] **Step 1: Write the failing E2E test `e2e/tests/price-increase.spec.ts` (popover part)**

  Seed with `createRow`/`ensureRow`/`seedGrave` from `e2e/seed.ts` and names from `e2e/unique.ts`:
  - an all-year item at 10,10 € default and VAT 19;
  - a grave at the default and a grave at own 150,00, one recipient each;
  - a grave at own 95,00 with **two** recipients (95,00 divides, so it saves without a correction).

  Open the item in `/items` ("Leistung bearbeiten", then `waitForOverlayOpen`), click "Preise erhöhen", wait again, then:
  - fill "Erhöhung in %" with `5` via `fillNumber`;
  - expect the proposals 10,61 (the half-cent case), 99,75 and 157,50;
  - expect the correction hint "Rundungsanpassung bei 1 Gräbern", because 99,75 over two recipients is odd in cents and is corrected to 99,76;
  - overwrite 99,75 with 100,00, expect the hint to disappear, change the percent to 10, and expect the 95 row to show 104,50 again (the manual edit is gone);
  - untick the 150 row, click "Übernehmen", and expect the dialog's grave section to show the old prices struck through with the new ones, and the default field read-only showing 11,11.

  Nothing is saved in this test. Close with "Abbrechen", reopen the item, and expect the old prices.

- [ ] **Step 2: Run it to verify it fails**

  Run: `cd e2e && npm test -- tests/price-increase.spec.ts --project=desktop`
  Expected: FAIL (no "Preise erhöhen" button).

- [ ] **Step 3: Implement `lib/priceIncrease.ts`, the endpoints and `useItemPriceIncreasePreview.ts`**

  The composable is modelled on `useGraveBillingPreview.ts`: 300 ms via `useDebounced`, older answers dropped, and `answered` tracking the input the answer belongs to. Its input is `{ itemId, rows }`, and it is null while no ticked row differs from its old price.

- [ ] **Step 4: Implement `ItemPriceIncreaseDialog.vue`**

  A small modal PrimeVue `Dialog`, titled `priceIncrease`, full width under 640 px like `ItemDialog`.
  - An `InputNumber` for the percent: `locale="de-DE"`, 0–2 fraction digits, min 0, no suffix inside the field.
  - A table with checkbox, label (`itemPriceDefault`/`itemPriceOwn` + `priceIncludesVatShort` as in `ItemDialog.groupLabel`, plus `priceIncreaseGraves`), old price, and a new-price `InputNumber` (2 fraction digits, ` €`). An unticked row shows its old price greyed instead.
  - Changing the percent sets every row's `newPrice = raiseByPercent(oldPrice, percent)`.
  - A row with problems gets a red border and lists up to 3 problems plus `priceIncreaseMoreProblems`.
  - The corrections are an amber `Message` with the count and an expandable list (grave, invoice, item, entered → corrected).
  - "Übernehmen" is disabled unless some ticked row changed, no row has problems, the preview is not pending, and `answered` equals the current input. On click it emits `apply` with the answered corrections and closes.

- [ ] **Step 5: Wire it into `ItemDialog.vue`**

  - A `priceIncrease` button (outlined, small, `pi pi-arrow-up-right`) in the graves heading, shown when editing an item that has a default price or any priced group.
  - A `pending: PendingIncrease | null` ref, reset when the dialog opens.
  - While pending, each changed group label shows `<s>old</s> → new`. The default field shows the default row's new price and is disabled. A text button `priceIncreaseDiscard` clears `pending`.

  Saving stays unchanged in this task: the button is wired, the save comes in Task 4.

- [ ] **Step 6: Run the E2E test and the frontend checks**

  Run: `cd frontend && npm run typecheck && npm run lint && npm run build`, then `cd e2e && npm test -- tests/price-increase.spec.ts --project=desktop`
  Expected: all clean and PASS.

- [ ] **Step 7: Commit**

  ```bash
  git add frontend/src e2e/tests/price-increase.spec.ts
  git commit -m "feat(frontend): propose and check an item price increase in the item dialog"
  ```

---

### Task 4: Saving the increase with the dialog

**Files:**

- Modify: `frontend/src/components/ItemDialog.vue` (`save()`)
- Test: `e2e/tests/price-increase.spec.ts`

**Interfaces:**

- Consumes: `applyItemPriceIncrease` and `PendingIncrease` from Task 3, and the apply route from Task 2.

- [ ] **Step 1: Write the failing E2E tests**

  ```ts
  test("saving the item applies the increase to every ticked price", ...)
  // Seed as in Task 3 (fresh names). Open the item, then 5 %, untick 150, overwrite 99,75 → 100,00,
  // Übernehmen, Speichern. Read back via page.request:
  // GET /odata/ItemGraves?itemId= → default 10,61 (graves on default included), own 100,00, own 150,00 unchanged.

  test("a raised price that needs a rounding correction is stored with it", ...)
  // The two-recipient grave at own 95,00, raised 5 % → 99,75 (hint shown). Übernehmen, Speichern.
  // The dialog closes without an error (an unconfirmed correction would be refused with
  // CorrectionsChanged), and GET /odata/ItemGraves lists that grave at 99,75. Whether the correction
  // itself is stored is the backend test's job (Task 2, Apply_stores_the_confirmed_corrections).

  test("discarding the increase saves the item as before", ...)
  // 5 %, Übernehmen, "Erhöhung verwerfen", change the description, Speichern. The prices are unchanged
  // and the description is saved.
  ```

- [ ] **Step 2: Run them to verify they fail**

  Run: `cd e2e && npm test -- tests/price-increase.spec.ts --project=desktop`
  Expected: the first two FAIL (prices unchanged after save).

- [ ] **Step 3: Implement the save order in `ItemDialog.save()` (spec 3.6)**

  1. Run the existing PATCH, sending the default from `props.item.DefaultPrice` (the old one) while an increase is pending.
  2. If an increase is pending, run `applyItemPriceIncrease(item.Id, pending.rows, pending.corrections)`. On failure, show the error with `showError`, keep the dialog open and keep `pending`, and invalidate the item queries so the list shows the saved fields.
  3. On success, clear `pending` and close as today (invalidate queries, toast `saved`).

- [ ] **Step 4: Run the E2E spec, the frontend checks and the full E2E suite**

  Run: `cd frontend && npm run typecheck && npm run lint && npm run build`, then `cd e2e && npm test`
  Expected: all clean. The full suite passes. A failure in an unrelated spec is re-run alone before blaming this change, since the specs share one stack.

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/components/ItemDialog.vue e2e/tests/price-increase.spec.ts
  git commit -m "feat(frontend): save an item price increase with the item dialog

  Closes #67"
  ```
