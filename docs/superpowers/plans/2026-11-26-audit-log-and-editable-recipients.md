# Change history and editable recipients Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Verlauf" on every grave and invoice that records each change, grave recipients that can change after invoicing, a recipient swap on issued grave invoices, and a recipient picker that shows number and full address.

**Architecture:** Each write path opens an `AuditAction` (scoped `AuditScope`), which snapshots the graves it will touch before the change and diffs them afterwards in domain terms; invoice changes are recorded explicitly from the old and new revision. A `SaveChangesInterceptor` guards that no grave or invoice change is saved outside a declared action. Entries live in one table, read through a read-only OData set, and are shown by an `AuditTrail.vue` component.

**Tech Stack:** .NET / EF Core (SQLite) / ASP.NET Core OData, xunit integration tests; Vue 3, PrimeVue, TanStack Query, vue-i18n; Playwright.

**Spec:** `docs/superpowers/specs/2026-11-26-audit-log-and-editable-recipients-design.md`

## Global Constraints

- Code, identifiers, comments, commits in English; every user-visible string German and in `frontend/src/i18n/de.json` (Principle VII).
- Exactly one migration in this version: `AuditEntries` (Principle IV). Parts 1–3 add no schema.
- Grave recipients: 1 to 4 (`GraveBilling.MaxRecipients`), cooperative graves may have none; all rules other than `GraveRecipientsImmutable` stay.
- Swap on an issued grave invoice is 1:1 only; recipient count, `SharePercent`, `RecipientCount` never change.
- A Verlauf entry stores names as they were when written (labels resolved at write time).
- Entries are written in the transaction of the change and roll back with it.
- No "who" is recorded. No history before the release.
- Backend tests: `docker compose stop backend`, clear `bin`/`obj`, `scripts/dotnet.sh test Invoicing.sln`, `docker compose start backend` (CLAUDE.md).
- Frontend: `npm run typecheck`, `npm run lint`, `npm run build` all clean.
- E2E only against the E2E stack (`cd e2e && npm test`), never port 8080; Selects via `pickOption`/`pickRecipient`, dialogs via `waitForOverlayOpen`, drawers saved via `saveEditor`.
- Commits are Conventional Commits; formatting runs in the pre-commit hook.

## Review Focus

1. **A grave saved twice without changes** — the second save must write no entry (replaced links and rebuilt corrections are no change). Test in Task 3.
2. **Recipients changed after a half-year was invoiced, then that half-year is revised** — the revision bills the half-year's issued recipients, not the grave's new set. Test in Task 1.
3. **A refused write** (validation error after `Begin`) — leaves no entry and no half-written state. Test in Task 2.
4. **A swap to a recipient already in the group, or to one without an address** — refused, nothing written. Test in Task 6.
5. **The picker after adding a recipient** — opens on the newest recipients, not the previous search. E2E in Task 10.

---

### Task 1: A grave's recipients can change after invoicing

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs:130-149` (drop the check), its comment block
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs` (comment "A grave's recipients are fixed once invoiced…")
- Rename/rewrite: `backend/tests/Invoicing.IntegrationTests/GraveRecipientImmutabilityTests.cs` → `GraveRecipientChangeTests.cs`
- Modify: `docs/spec.md` (FR-020b, User Story 5 scenario 5, new edge case)

**Interfaces:**

- Produces: `SaveGrave` accepts any valid recipient set on an invoiced grave. Error code `GraveRecipientsImmutable` no longer exists.

- [ ] **Step 1: Rewrite the test class.** Keep the three passing tests; replace `Saving_a_grave_after_it_was_invoiced_keeps_its_recipients` with:
  - `Saving_an_invoiced_grave_with_another_recipient_set_replaces_it` — generate 2033/1, save with `[annaId, berndId]`, expect 200 and 2 `GraveRecipients`.
  - `The_next_generation_bills_the_new_recipients` — generate 2033/1 (Anna only), save `[annaId, berndId]`, generate 2033/2; 2033/2 has two invoices with `SharePercent == 50` and recipients {Anna, Bernd}; 2033/1 still has one invoice (Anna, 100).
  - `Revising_an_issued_half_year_keeps_its_own_recipients` — as above, then `CreateGraveInvoiceRevision` on the 2033/1 invoice; exactly one new revision, recipient Anna, `RecipientCount == 1`.
- [ ] **Step 2: Run, expect the first and third new tests to fail** with `GraveRecipientsImmutable`.
- [ ] **Step 3: Remove the check** and rewrite both comments: the revision group is the half-year's issued set, which may now differ from the grave's.
- [ ] **Step 4: Run the class, expect PASS.**
- [ ] **Step 5: Update `docs/spec.md`**: FR-020b → "A grave's set of recipients MAY change at any time; a change applies to half-years generated afterwards. Invoices already issued keep their recipients and split, also when revised (FR-034a)." Rewrite User Story 5 scenario 5 to match; add the edge case "recipients changed after a half-year was invoiced, then that half-year is revised → the revision bills the half-year's issued recipients".
- [ ] **Step 6: Commit** `feat(graves): let a grave's recipients change after its first invoice`

### Task 2: Audit storage, scope, guard and read endpoint

**Files:**

- Create: `backend/src/Invoicing.Domain/Entities/AuditEntry.cs`
- Create: `backend/src/Invoicing.Api/Audit/AuditChange.cs`, `AuditActions.cs`, `AuditSubject.cs`, `AuditScope.cs`, `AuditAction.cs`, `AuditGuardInterceptor.cs`, `AuditGuardMode.cs`, `AppDbContextRegistration.cs`, `GraveSnapshot.cs` (records + `GraveSnapshots` loader), `GraveAuditDiff.cs`
- Create: `backend/src/Invoicing.Api/OData/Controllers/AuditEntriesController.cs`
- Create: migration `AuditEntries` (`scripts/dotnet.sh ef migrations add AuditEntries …` as the earlier migrations were made)
- Modify: `AppDbContext.cs` (DbSet, indexes), `EdmModelBuilder.cs` (`b.EntitySet<AuditEntry>("AuditEntries")`), `Program.cs` (registration), `ApiTestHost.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/AuditGuardTests.cs`, `GraveAuditDiffTests.cs`

**Interfaces:**

- Produces (the grave snapshot and diff types are listed under Task 3's Interfaces; they are built here because `AuditAction` uses them):
  ```csharp
  // Domain
  public sealed class AuditEntry {
      public long Id; public DateTime OccurredAt /* UTC */; public string Action = "";
      public string? DetailsJson; public long? GraveId; public long? InvoiceRootId;
      public string ChangesJson = "[]";
  } // exactly one of GraveId / InvoiceRootId is set; indexes on both; no FKs

  // Api.Audit
  public sealed record AuditChange(string Field, string? Label, string? Old, string? New,
      string Type, string? Period = null);
  // Type ∈ text | money | month | date | int | bool | position | priceSource | invoice
  // Values are invariant: money "105.00", month "2040-12", date "2026-11-26",
  // bool "true"/"false", position = enum name, priceSource "default"/"own",
  // invoice "<rootId>:<invoiceNumber>". Period: "<Kind>:<Year|->-<Half>" on corrections.

  public abstract record AuditSubject { Grave(long GraveId), Invoice(long RootId) }

  public static class AuditActions { // string constants
      GraveCreated, GraveEdited, GraveHandedOver, GraveDeleted, GraveRestored, GraveImported,
      ItemPriceIncreased, ItemDefaultPriceChanged, GraveInvoicesGenerated, GraveInvoiceRevised,
      InvoiceCreated, InvoiceCreatedFromQuote, InvoiceRevised, InvoicePaid, InvoiceUnpaid,
      Unattributed }

  public enum AuditGuardMode { Throw, Record } // singleton; Throw unless env is Production

  public sealed class AuditScope { // scoped
      public bool IsArmed { get; } public void Arm();
      public AuditAction? Current { get; }
      public AuditAction Begin(string action, IReadOnlyDictionary<string, string>? details = null);
      // throws InvalidOperationException when an action is already open
  }

  public sealed class AuditAction : IDisposable {
      public Task WatchGravesAsync(IEnumerable<long> graveIds, CancellationToken ct,
          string? action = null, IReadOnlyDictionary<string, string>? details = null);
      public void WatchNewGrave(Grave grave, string? action = null,
          IReadOnlyDictionary<string, string>? details = null);
      public void Record(AuditSubject subject, IReadOnlyList<AuditChange> changes,
          string? action = null, IReadOnlyDictionary<string, string>? details = null);
      public Task CompleteAsync(CancellationToken ct);
      public void Dispose(); // clears AuditScope.Current
  }

  public static class AppDbContextRegistration {
      public static IServiceCollection AddAppDbContext(this IServiceCollection s, string connectionString);
  } // AddDbContext((sp, o) => o.UseSqlite(cs).AddInterceptors(sp.GetRequiredService<AuditGuardInterceptor>()))
  ```
- `ApiTestHost` gains `public Action<IServiceCollection>? ConfigureTestServices { get; init; }` and uses `AddAppDbContext`.

Behaviour the code must have:

- `WatchGravesAsync` stores a before-snapshot per grave via `GraveSnapshots.LoadAsync`; `CompleteAsync` diffs it with `GraveAuditDiff.Diff`.
- `Record` merges into one pending entry per (subject, action); grave subjects from watches and from `Record` merge too.
- `CompleteAsync`: throws `InvalidOperationException` if `Database.CurrentTransaction` is null; takes after-snapshots, diffs (Task 3), drops watched subjects with no changes, keeps every `Record`ed subject, adds the `AuditEntry` rows with `OccurredAt = clock.UtcNow`, `SaveChangesAsync`. Then checks every touched invoice root (see guard) has a `Record`; missing → `Throw` mode throws, `Record` mode adds an `Unattributed` entry.
- `AuditGuardInterceptor.SavingChanges[Async]`, only when the scope `IsArmed`: audited changes are `Grave`, `GraveItem`, `GraveRecipient`, `GraveBillingCorrection`, `Invoice`, `InvoiceLine` in Added/Modified/Deleted, and `InvoiceItem` with `DefaultPrice` modified.
  - No open action: `Throw` → `InvalidOperationException("Audited change without a declared audit action: <entity types>")`; `Record` → log a warning and add one `Unattributed` entry per grave id found (`Grave.Id`, `*.GraveId`, `Invoice.GraveId`) or per invoice root.
  - Open action: a grave touched (modified/deleted `Grave`, or any link/correction row by `GraveId`) that was not watched, or an Added `Grave` not passed to `WatchNewGrave` (reference equality) → same Throw/Record handling. Touched `Invoice` entities are remembered for the check in `CompleteAsync` (their `RootId` is only known after the save).
  - The interceptor resolves `AuditScope` from the scoped provider at call time (no constructor cycle with `AppDbContext`).
- Arming is **not** wired in this task (Task 7), so existing paths and tests keep working.
- `AuditEntriesController`: `[EnableQuery(MaxTop = 100)] IQueryable<AuditEntry> Get() => _db.AuditEntries.AsNoTracking();` no writes.

- [ ] **Step 1: Write `AuditGuardTests`**:
  - `An_armed_scope_refuses_a_grave_change_without_an_action` — scope, `Arm()`, add a `Grave`, `SaveChanges` throws `InvalidOperationException`.
  - `An_unarmed_scope_seeds_freely` — same without `Arm()`, saves.
  - `Record_mode_writes_an_unattributed_entry` — host with `ConfigureTestServices = s => s.AddSingleton(AuditGuardMode.Record)`, armed scope, change a seeded grave's `SiteName`, save succeeds, one `AuditEntry` with `Action == "Unattributed"` and that `GraveId`.
  - `An_action_must_complete_inside_a_transaction` — `Begin`, `Record(Invoice(1), …)`, `CompleteAsync` without a transaction throws.
  - `A_rolled_back_action_leaves_no_entry` — transaction, `Begin`, `Record`, `CompleteAsync`, rollback → `AuditEntries` empty.
  - `The_entries_are_read_by_grave_and_by_invoice` — insert two entries, `GET /odata/AuditEntries?$filter=GraveId eq 5&$orderby=OccurredAt desc` returns only the grave's.
  - `GraveAuditDiffTests` (pure, the one place unit tests pay off — Principle III): `Equal_snapshots_give_no_change`; `A_replaced_item_with_equal_values_is_no_change`; `Count_and_price_changes_are_reported_per_item`; `Equal_effective_price_from_another_source_reports_the_source`; `Corrections_before_the_current_half_are_ignored`; `A_null_before_reports_every_value`.
- [ ] **Step 2: Run, expect compile failure / FAIL.**
- [ ] **Step 3: Implement the files above and the migration.**
- [ ] **Step 4: Run `AuditGuardTests` and the full backend suite, expect PASS** (nothing is armed yet).
- [ ] **Step 5: Commit** `feat(api): store audit entries behind a guard for undeclared changes`

### Task 3: Grave snapshots, diffs and the grave write paths

**Files:**

- Modify: `GraveService.cs` (`SaveAsync`, `DeleteAsync`, `RestoreAsync`), `Services/Import/ImportService.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveAuditTests.cs`

**Interfaces:**

- Consumes: Task 2 `AuditScope`, `AuditAction`, `AuditChange`, and these types, which Task 2 builds:
  ```csharp
  public sealed record GraveSnapshot(string GraveNumber, string FieldNumber, string SiteName,
      string CemeteryName, GravePosition? Position, DateOnly StartDate, DateOnly? EndDate,
      bool IsCooperative, string? ContractNumber,
      IReadOnlyList<GraveItemSnapshot> Items, IReadOnlyList<GraveRecipientSnapshot> Recipients,
      IReadOnlyList<GraveCorrectionSnapshot> Corrections);
  public sealed record GraveItemSnapshot(long ItemId, string Label /* "1001 Grabpflege" */,
      int Count, decimal? OwnPrice, decimal? EffectivePrice);
  public sealed record GraveRecipientSnapshot(long RecipientId, string Label /* "E-0107 Carla Muster"; the name alone when there is no number */);
  public sealed record GraveCorrectionSnapshot(string Kind, int? Year, int Half, decimal VatRate,
      long ItemId, string ItemLabel, decimal CorrectedPrice);
  public static class GraveSnapshots {
      public static Task<Dictionary<long, GraveSnapshot>> LoadAsync(AppDbContext db,
          IReadOnlyCollection<long> graveIds, CancellationToken ct); // AsNoTracking, one round trip per table
  }
  public static class GraveAuditDiff {
      public static IReadOnlyList<AuditChange> Diff(GraveSnapshot? before, GraveSnapshot after,
          (int Year, int Half) currentHalf);
  }
  ```
- Field keys: `graveNumber`, `fieldNumber`, `siteName`, `cemetery`, `position`, `start` (month), `end` (month; null = "ohne Ende"), `cooperative`, `contractNumber`; `itemAdded` / `itemRemoved` (Label), `itemCount` (Label, int), `itemPrice` (Label, money, effective price), `itemPriceSource` (Label, priceSource — only when the effective price is equal but own/default changed); `recipientAdded` (New = label), `recipientRemoved` (Old = label); `correction` (Label = item, Period, money old/new, null for added/removed). An added item emits `itemAdded`, `itemCount` and `itemPrice`.
- Diff rules: items paired by `ItemId`, recipients by `RecipientId`, corrections by (Kind, Year, Half, VatRate, ItemId); corrections compared only where `Year` is null or (Year, Half) ≥ `currentHalf` (`HalfYearBilling.HalfYearOf(clock.GetLocalNow())`). `before == null` (created/imported) reports every non-empty value.

Write paths:

- `SaveAsync`, new grave: `Begin(GraveCreated)`, `WatchNewGrave(grave)` once the entity exists; with a hand-over: details `{ "from": "<yyyy-MM>" }` on the new grave, and `WatchGravesAsync([predecessorId], action: GraveHandedOver, details: { "from": … })` before the predecessor's end is written.
- `SaveAsync`, existing grave: `Begin(GraveEdited)`, `WatchGravesAsync([graveId])` before any write. Validation still throws before `Begin` where it does today; `Begin` sits right before the transaction, `CompleteAsync` right before `CommitAsync`.
- `DeleteAsync` / `RestoreAsync`: `Begin(GraveDeleted|GraveRestored)`, watch, add a transaction, `Record(Grave(id), [])` so the entry exists without field changes.
- `ImportService`: one action `GraveImported` for the import, `WatchNewGrave` for every grave; ensure the import runs in one transaction.

- [ ] **Step 1: Write `GraveAuditTests`** (through HTTP; actions record in unarmed scopes too, so these pass before Task 7 arms requests):
  - `Creating_a_grave_records_its_values` — entry `GraveCreated` with `graveNumber` New "G-500", `itemAdded` Label "1001 Grabpflege", `recipientAdded` New "E-0001 Anna Beispiel".
  - `Editing_a_grave_records_old_and_new` — change end 2040-12 → 2041-12 and count 1 → 2: entry `GraveEdited` with `end` "2040-12"→"2041-12", `itemCount` "1"→"2", and nothing else.
  - `Saving_unchanged_writes_no_entry` — save the same body twice, exactly one entry (the creation).
  - `Changing_recipients_records_added_and_removed` — Anna → Bernd: `recipientAdded` "E-0002 Bernd Beispiel", `recipientRemoved` "E-0001 Anna Beispiel".
  - `A_hand_over_records_on_both_graves` — successor `GraveCreated` with details `from`, predecessor `GraveHandedOver` with `end` old→new.
  - `A_refused_save_writes_nothing` — an edit refused with `TooManyRecipients` leaves the entry count unchanged.
  - `Deleting_and_restoring_are_recorded` — two entries, `GraveDeleted` then `GraveRestored`.
  - `An_import_records_each_grave` — import the template fixture used by `ImportTests`; one `GraveImported` entry per grave.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement snapshot, diff and the write paths.**
- [ ] **Step 4: Run `GraveAuditTests` plus `SaveGraveTests`, `DeleteGraveTests`, `ImportTests`, `GraveTests`, expect PASS.**
- [ ] **Step 5: Commit** `feat(api): record grave changes in the audit log`

### Task 4: Item price changes reach the graves' Verlauf

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/ItemPriceIncreaseService.cs` (apply)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/InvoiceItemsController.cs:68-167` (`Patch` becomes `async Task<IActionResult>`, transaction)
- Test: `backend/tests/Invoicing.IntegrationTests/ItemPriceAuditTests.cs`

**Interfaces:**

- Consumes: Task 2 `AuditAction`, Task 3 `GraveSnapshots`/diff (effective price drives `itemPrice`).
- Produces: actions `ItemPriceIncreased` with details `{ "item": "1001 Grabpflege" }`; `ItemDefaultPriceChanged` with details `{ "item": …, "old": "100.00", "new": "110.00" }`.

- [ ] **Step 1: Write the tests**:
  - `A_price_increase_records_on_a_grave_with_its_own_price` — own price 100 → 105: `ItemPriceIncreased`, `itemPrice` "100.00"→"105.00".
  - `A_price_increase_of_the_default_records_on_graves_billing_it` — grave on default 100, default row ticked to 105: same change on that grave.
  - `A_price_increase_records_changed_corrections` — a grave whose new price needs a correction: entry carries a `correction` change.
  - `Editing_the_default_pins_it_and_records_why` — Patch default 100 → 110: every grave on the default gets `ItemDefaultPriceChanged` with details old/new and `itemPriceSource` "default"→"own"; a grave with its own price gets nothing.
  - `Editing_a_default_no_grave_bills_writes_nothing`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**: in apply, `Begin(ItemPriceIncreased, …)`, `WatchGravesAsync(affected ∪ graves on the default when the default row is ticked)` before any write, transaction around the writes, `CompleteAsync` before commit. In `Patch`, when the default changes and graves bill it: `Begin(ItemDefaultPriceChanged, …)`, watch those graves, transaction, complete.
- [ ] **Step 4: Run these and `ItemPriceIncreaseTests`, expect PASS.**
- [ ] **Step 5: Commit** `feat(api): record item price changes on the graves they reach`

### Task 5: Invoice write paths

**Files:**

- Create: `backend/src/Invoicing.Api/Audit/InvoiceAuditDiff.cs`
- Modify: `Services/DocumentDataBuilder.cs` (add `BilledGross`), `OData/Controllers/InvoicesController.cs` (`WithTotals` uses it), `Services/InvoiceService.cs` (create, revision, quote conversion, `SetPaidAsync`, `MarkPaidAsync`), `Services/GraveInvoiceGenerator.cs`, `Services/GraveInvoiceRevisionService.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceAuditTests.cs`

**Interfaces:**

- Consumes: Task 2.
- Produces:
  ```csharp
  public decimal BilledGross(Invoice invoice); // on DocumentDataBuilder: Share?.GrossTotal ?? GrossTotal; lines loaded
  public static class InvoiceAuditDiff {
      public static IReadOnlyList<AuditChange> Created(Invoice invoice, decimal gross);
      public static IReadOnlyList<AuditChange> Revised(Invoice previous, decimal previousGross,
          Invoice revision, decimal revisionGross);
      public static AuditChange InvoiceLink(Invoice invoice); // Field "invoice", Type "invoice"
  }
  ```
- Field keys: `invoiceNumber` (text; new number), `recipient` (text label from the snapshot fields `RecipientNumber`/`RecipientName`), `introText`, `lineAdded` / `lineRemoved` (Label "ItemNumber Description"), `lineCount`, `linePrice` (money; the entered gross for VAT-included lines, else the net), `lineVatIncluded` (bool), `lineComment` (text), `total` (money, billed gross), `paid` (bool). Lines paired by `ItemId` (by `ItemNumber` when null), in line order for repeats.
- Actions and subjects:
  - Create: `InvoiceCreated` on `Invoice(rootId)`; quote conversion `InvoiceCreatedFromQuote` with details `{ "quote": "<quote number>" }`.
  - Revision: `InvoiceRevised` on the chain.
  - `SetPaidAsync`: `InvoicePaid` / `InvoiceUnpaid`, `paid` change on the chain; for a grave invoice also on `Grave(graveId)` with Label = invoice number.
  - `MarkPaidAsync`: `InvoicePaid` per chain, one merged grave entry per grave.
  - Generation (per grave, in `GenerateForGraveAsync`'s transaction): `GraveInvoicesGenerated` with details `{ "year", "half" }` on the grave with one `InvoiceLink` per invoice; each invoice chain `InvoiceCreated`.
  - Grave-invoice revision: `GraveInvoiceRevised` with `{ "year", "half" }` on the grave with `InvoiceLink`s; each chain `InvoiceRevised` from `Revised(sibling, …, revision, …)`.
  - Every path without a transaction gets one; `CompleteAsync` before commit.

- [ ] **Step 1: Write `InvoiceAuditTests`**:
  - `Creating_an_invoice_records_recipient_lines_and_total`.
  - `A_revision_records_what_changed` — new recipient, count 1→3: `recipient` old→new, `lineCount` "1"→"3", `total` old→new, `invoiceNumber` New ends with "-R1"; all revisions share the entry list by `InvoiceRootId`.
  - `Converting_a_quote_records_the_quote_number`.
  - `Paying_and_unpaying_are_recorded`.
  - `Marking_grave_invoices_paid_records_on_invoices_and_grave` — two recipients' invoices: two invoice entries, one grave entry with two `paid` changes.
  - `Generating_records_on_grave_and_invoices` — grave entry with two `invoice` changes "<rootId>:<number>", each chain `InvoiceCreated`.
  - `Revising_a_grave_invoice_records_every_chain`.
  - `A_refused_revision_writes_nothing` — paid sibling → `GraveInvoicePaid`, no new entries.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run these plus `CreateInvoiceTests`, `RevisionTests`, `QuoteConversionTests`, `PaymentTests`, `MarkInvoicesPaidTests`, `GraveGenerationTests`, `GraveInvoiceRevisionTests`, `InvoiceTotalsTests`, expect PASS.**
- [ ] **Step 5: Commit** `feat(api): record invoice changes in the audit log`

### Task 6: Swap a recipient on an issued grave invoice

**Files:**

- Modify: `OData/Controllers/InvoiceActionsController.cs` (`CreateGraveInvoiceRevisionRequest`), `Services/GraveInvoiceRevisionService.cs`, `OData/Controllers/GraveInvoiceGroupController.cs` (recipients gain `Number`, `Address` from the invoice snapshot)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceRecipientSwapTests.cs`
- Modify: `docs/spec.md` (new FR-034c)

**Interfaces:**

- Consumes: Task 5 grave-invoice revision recording.
- Produces:
  ```csharp
  public sealed class RecipientReplacementInput { public long InvoiceId { get; set; } public long RecipientId { get; set; } }
  // request: public List<RecipientReplacementInput>? Replacements { get; set; }
  public Task<List<Invoice>> CreateAsync(long invoiceId, IReadOnlyList<InvoiceLineInput> lineInputs,
      IReadOnlyList<RecipientReplacementInput> replacements, CancellationToken ct);
  ```
- Error codes: `ReplacementNotInGroup` (invoice not one of the group's latest revisions, or named twice), `GraveRecipientLinkExists` (resulting set holds a recipient twice), `RecipientNotFound`, `RecipientAddressRequired`, `GraveInvoicePaid` (unchanged).
- The swapped revision's `RecipientId/Name/Address/Number` are the new recipient's current values; `SharePercent`, `RecipientCount` unchanged; the grave's `GraveRecipients` untouched. The grave entry gets `recipientSwapped` (Label = invoice number, Old/New = recipient labels).

- [ ] **Step 1: Write the tests**: `A_swap_gives_that_invoice_the_new_recipient` (two recipients, swap Bernd → Carla with unchanged lines: two revisions, Bernd's chain now Carla with her address, Anna's unchanged recipient, both `-R1`, grave still Anna+Bernd); `A_swap_to_a_recipient_already_in_the_group_is_refused`; `A_swap_to_a_recipient_without_address_is_refused`; `A_swap_naming_an_invoice_outside_the_group_is_refused`; `A_swap_is_refused_while_an_invoice_is_paid`; `A_swap_is_recorded` (chain entry `recipient` old→new, grave entry `recipientSwapped`). Each refusal asserts no new invoices and no new entries.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**; the controller passes `request.Replacements ?? []`; the group endpoint adds `Number`, `Address`.
- [ ] **Step 4: Run, expect PASS**, plus `GraveInvoiceRevisionTests`, `GraveInvoiceGroupTests`.
- [ ] **Step 5: Add FR-034c to `docs/spec.md`**: "Revising a grave invoice MAY swap the recipient of any invoice of the half-year for another recipient with an address who is not already among them; the number of recipients and the split stay, and the grave's own recipients are not changed."
- [ ] **Step 6: Commit** `feat(invoices): swap a recipient when revising a grave invoice`

### Task 7: Arm the guard and document it

**Files:**

- Modify: `backend/src/Invoicing.Api/Program.cs` (middleware after `DatabaseGateMiddleware`: resolve `AuditScope`, `Arm()`), `Jobs/GraveInvoicesJobHandler.cs` (arm the scope it creates)
- Modify: `CLAUDE.md` (short section "Every write to graves and invoices declares an audit action")
- Modify: `docs/spec.md` (FR-109–FR-113)
- Test: `backend/tests/Invoicing.IntegrationTests/AuditCoverageTests.cs`

- [ ] **Step 1: Wire the middleware and the job handler.**
- [ ] **Step 2: Write `AuditCoverageTests.Every_write_path_is_declared`** — through HTTP: create grave, edit, delete, restore, price increase, default patch, create invoice, revise, set paid, mark paid, generate (job, wait for it), grave revision with swap, import, convert quote — every call succeeds and each produces at least one entry.
- [ ] **Step 3: Prove the arming** by temporarily removing the `Begin` in `GraveService.DeleteAsync`: the coverage test must fail with a 500 from the guard. Put it back.
- [ ] **Step 4: Run the full backend suite, expect PASS.** Any 500 from the guard is a path Tasks 3–6 missed: declare it there.
- [ ] **Step 5: Spec**: FR-109 Verlauf on graves and invoices (what, when, old → new, no who, from this version on); FR-110 grave actions (table from the design); FR-111 invoice actions; FR-112 item price changes appear on every grave whose billed or pinned price they change; FR-113 an entry belongs to the change's transaction; an unchanged save writes none.
- [ ] **Step 6: CLAUDE.md**: a new write path that touches graves or invoices must `Begin` an audit action, watch or record its subjects and `CompleteAsync` inside its transaction; tests that fail with "Audited change without a declared audit action" mean exactly that. Test seeding through `host.Services.CreateScope()` is unarmed on purpose.
- [ ] **Step 7: Commit** `feat(api): guard every request and job against unrecorded changes`

### Task 8: The Verlauf in the frontend

**Files:**

- Regenerate: `frontend/src/api/metadata.xml`, `frontend/src/api/generated/` (`npm run gen:odata` against the E2E stack)
- Create: `frontend/src/lib/audit.ts`, `frontend/src/components/AuditTrail.vue`
- Modify: `frontend/src/api/queries.ts` (`useAuditEntriesQuery`), `frontend/src/i18n/de.json`, `pages/GraveDetailPage.vue`, `pages/InvoiceDetailPage.vue`, `pages/GraveInvoiceDetailPage.vue`

**Interfaces:**

- Consumes: `GET /odata/AuditEntries` (Task 2), change format (Tasks 3–6).
- Produces:
  ```ts
  export interface AuditChange {
    Field: string;
    Label: string | null;
    Old: string | null;
    New: string | null;
    Type: string;
    Period: string | null;
  }
  export function parseChanges(json: string): AuditChange[];
  export function formatAuditValue(
    change: AuditChange,
    value: string | null,
    t: Composer["t"],
  ): string;
  export function auditActionText(
    action: string,
    details: Record<string, string>,
    t: Composer["t"],
  ): string;
  export function useAuditEntriesQuery(
    subject: Ref<{ graveId: number } | { invoiceRootId: number } | null>,
    top: Ref<number>,
  );
  // AuditTrail.vue props: { graveId?: number; invoiceRootId?: number }
  ```
- Copy (de.json, flat keys like the rest): `auditTitle` "Verlauf", `auditEmpty` "Änderungen werden ab dieser Version aufgezeichnet.", `auditMore` "Weitere laden"; actions `auditAction<Key>`: GraveCreated "Grab angelegt" (with `from`: "Grab angelegt als Nachfolger ab {from}"), GraveEdited "Grab bearbeitet", GraveHandedOver "An Nachfolger übergeben ab {from}", GraveDeleted "Grab gelöscht", GraveRestored "Grab wiederhergestellt", GraveImported "Grab importiert", ItemPriceIncreased "Preiserhöhung {item}", ItemDefaultPriceChanged "Standardpreis von {item} geändert ({old} → {new}); Grab behält {old} als eigenen Preis", GraveInvoicesGenerated "Rechnungen {halfYear} erzeugt", GraveInvoiceRevised "Rechnungen {halfYear} überarbeitet", InvoiceCreated "Rechnung erstellt", InvoiceCreatedFromQuote "Rechnung aus Angebot {quote} erstellt", InvoiceRevised "Rechnung überarbeitet", InvoicePaid "Als bezahlt markiert", InvoiceUnpaid "Als unbezahlt markiert", Unattributed "Änderung". Field labels `auditField<Field>` in German (Grabnummer, Feldnummer, Grabstätte, Friedhof, Lage, Beginn, Ende, Genossenschaft, Vertragsnummer, Position hinzugefügt/entfernt, Anzahl, Preis, Preisart, Empfänger hinzugefügt/entfernt/getauscht, Rundungskorrektur, Rechnung, Rechnungsnummer, Empfänger, Einleitungstext, Zeile hinzugefügt/entfernt, inkl. MwSt., Kommentar, Summe, Bezahlt). Money via the existing money formatter, months `MM/yyyy`, position via the existing position labels, `halfYear` via `halfYearText`, priceSource "Standardpreis"/"eigener Preis", end null "ohne Ende".
- Display: newest first, 20 entries, "Weitere laden" adds 20; each entry: local date and time, action text, then `Label: Feld: alt → neu` lines (added: `+ neu`, removed: `− alt`); `invoice` values render as router links to the invoice's detail page (grave invoice detail route by root id). Placed as the last card on each detail page.

- [ ] **Step 1: Regenerate the client** (E2E stack running) and commit-ready check `npm run typecheck`.
- [ ] **Step 2: Implement `audit.ts`, the query, `AuditTrail.vue`, the copy, the three pages.**
- [ ] **Step 3: `npm run typecheck && npm run lint && npm run build`, expect clean.**
- [ ] **Step 4: Commit** `feat(frontend): show a grave's and an invoice's Verlauf`

### Task 9: Recipient picker and recipient cards

**Files:**

- Create: `frontend/src/components/RecipientCard.vue`
- Modify: `frontend/src/lib/format.ts` (`formatRecipientLabel`), `components/RecipientPicker.vue`, `components/InvoiceEditor.vue`
- Modify: `docs/spec.md` (FR-016d)

**Interfaces:**

- Produces:
  ```ts
  export function formatRecipientLabel(number: string | null | undefined, name: string): string; // "E-0042 · Anna Beispiel", or the name alone
  // RecipientCard.vue props: { number?: string | null; name: string; address: string } — label line, then every address line (white-space: pre-line)
  ```
- `RecipientPicker`: option slot shows `formatRecipientLabel` and the full address (all lines); a `#value` slot shows `formatRecipientLabel` of the pick; the option's `aria-label` stays the name (e2e `pickRecipient` relies on it); `reset-filter-on-hide`, and `filter.value = ""` on `@hide` and whenever the model is set to null from outside, so the next opening shows the newest recipients.
- `InvoiceEditor`: under the picker, a `RecipientCard` for the selected recipient (`@selected`).

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Frontend checks clean.**
- [ ] **Step 3: FR-016d**: "Where a recipient is picked, every option and the chosen value MUST show the recipient's number and name, and the options and the editor MUST show the full address."
- [ ] **Step 4: Commit** `feat(frontend): show a recipient's number and full address where one is picked`

### Task 10: Grave editor and grave-invoice editor

**Files:**

- Modify: `components/GraveEditor.vue` (recipient section), `components/GraveInvoiceEditor.vue`, `composables/useGraveInvoiceEdit.ts`, `api/endpoints.ts` (`createGraveInvoiceRevision`), `i18n/de.json` (drop `graveRecipientsFixed`; add `swapRecipient` "Austauschen", `errorReplacementNotInGroup`)

**Interfaces:**

- Consumes: Task 6 request and group shape, Task 9 `RecipientCard`, `formatRecipientLabel`.
- Produces:
  ```ts
  export async function createGraveInvoiceRevision(
    invoiceId: number,
    lines: InvoiceLineInput[],
    replacements: { invoiceId: number; recipientId: number }[],
  ): Promise<void>;
  export interface GraveInvoiceDraft {
    invoiceId: number;
    warning: string;
    lines: DraftLine[];
    recipientCount: number; // renamed from `recipients`
    members: {
      invoiceId: number;
      recipientId: number;
      number: string | null;
      name: string;
      address: string;
    }[];
  }
  ```
- `GraveEditor`: drop `recipientsEditable` and the fixed hint; recipients held as `{ id, number, name, address }` and shown as `RecipientCard`s with "Austauschen" and "Entfernen" (the existing `pi pi-times`); "Austauschen" puts a `RecipientPicker` (excluding the other recipients) in place of that card, a pick replaces the recipient at the same position, Escape/Abbrechen restores the card. The add flow stays.
- `GraveInvoiceEditor`: above the lines, one card per member with a `RecipientPicker` preset to the member's recipient, excluding the other members; a changed member goes into `replacements`; the discard guard includes the members' recipient ids.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Frontend checks clean.**
- [ ] **Step 3: Commit** `feat(frontend): change a grave's recipients and swap one on a grave invoice`

### Task 11: E2E coverage

**Files:**

- Create: `e2e/tests/audit.spec.ts`
- Modify: `e2e/tests/graves.spec.ts` (rewrite "editing a grave warns about future invoices and, once invoiced, freezes its recipients"; extend "a recipient added to a grave is no longer offered for it"), `e2e/tests/invoices.spec.ts`, `e2e/tests/layout.spec.ts`

- [ ] **Step 1: Write the specs** (each seeds its own data with `unique()`; invoiced graves follow the pattern of "editing a grave invoice revises every recipient"):
  - `audit.spec.ts`: "editing a grave shows the change in its Verlauf" (end month old → new); "a price increase shows on the grave's Verlauf"; "revising an invoice shows the new recipient in its Verlauf".
  - `graves.spec.ts`: "an invoiced grave takes another recipient" (add Bernd after invoicing, save succeeds, card shows number and address); "a recipient is swapped in the grave editor"; "the recipient search is empty again after adding a recipient" (type a search, pick, add, reopen: the newest recipients are listed and the filter field is empty).
  - "a grave invoice's recipient is swapped on revision" — the swapped invoice's detail page shows the new recipient; the sibling keeps its recipient.
  - `invoices.spec.ts`: "the invoice editor shows the recipient's number and full address".
  - `layout.spec.ts` (phone, tablet): the Verlauf card and the recipient cards stay inside the viewport.
- [ ] **Step 2: Run `cd e2e && npm test`, expect PASS** (re-run a failing spec alone before suspecting a bug — specs share one stack).
- [ ] **Step 3: Commit** `test(e2e): cover the Verlauf and changing recipients`
