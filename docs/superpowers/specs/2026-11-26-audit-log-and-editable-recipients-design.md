# Change history for graves and invoices, editable recipients — design

Status: Approved

## Intent

Three things the user needs, decided together on 2026-11-26:

1. **A change history ("Verlauf")** for graves and invoices. It is for the
   people working with the app: to see later what happened to a grave or an
   invoice and when — a price increase is the example they named. It is not a
   compliance record: it does not have to be tamper-proof, but it has to be
   complete, or nobody can rely on it.
2. **Recipients that can change again.** Since 2026-08-29 a grave's recipients
   are frozen by its first invoice (FR-020b, `GraveRecipientsImmutable`). The
   user wants to add, remove and swap a grave's recipients for its future
   billing at any time, and to swap (only swap) the recipient of an invoice that
   was already issued — grave invoices included.
3. **More about a recipient where one is picked**: the number and the full
   address, not just the name. While at it, the picker keeps its old search text
   after a recipient was added to a grave — a bug the user reported.

## Goals

- Every change to a grave or an invoice appears in its Verlauf, whichever
  feature made it, with the date and time, what was done, and each changed value
  from old to new.
- A change to an item's price that changes or pins what a grave bills appears in
  that grave's Verlauf — on every grave it touches (decided by the user, knowing
  that one item can touch hundreds of graves).
- A grave's recipient set can be edited (1 to 4, as before) whether or not the
  grave has invoices; it applies from the next generation on.
- On a grave invoice already issued, each recipient can be swapped for another
  as part of the half-year's revision; how many recipients there are, and with
  it the split, stays.
- Ordinary invoices keep what they already do: a revision can take another
  recipient (unless the invoice is paid).
- Where a recipient is picked or listed in an editor, it shows number, name and
  full address.

## Non-goals

- No "who": the app has no logins, so the Verlauf cannot say who made a change.
- No history before the release: the Verlauf starts empty. The revision list of
  an invoice stays as it is and still shows its earlier versions.
- No Verlauf for recipients, items, cemeteries, settings or quotes as such.
  Their changes appear only where they change a grave or an invoice (an item's
  price, see below).
- Adding or removing recipients on an invoice that was already issued: that
  would change the split of an issued bill. Only a 1:1 swap.
- A backup restore is not recorded: it replaces the database, history included.
- No search or global list across all entries.

## Anti-goals

- A Verlauf with gaps that nobody notices — worse than none, since it would be
  trusted.
- Entries that speak in database terms (ids, column names, "UnitPrice: null").
- An invoice already sent out that changes because a grave's recipients changed.

## Approaches

Approach chosen by the user: **C**.

- **A — every action writes its own entry.** Readable entries with the reason
  ("Preiserhöhung"), but every one of the ~15 write paths has to remember it,
  and a new feature that forgets leaves a silent gap.
- **B — automatic, on the database level.** A hook on saving diffs every
  audited row. Nothing can forget it, but the reason is missing ("Preis 100 →
  105", not that it was a price increase), raw ids need translating, and the item
  price fan-out and invoice revisions (new rows, not updates) need their own code
  anyway.
- **C — hybrid (chosen).** Each action declares only _what_ it is ("Grab
  bearbeitet", "Preiserhöhung"); the changed values are collected
  automatically. A change to a grave or an invoice saved without a declared
  action fails the tests, so a forgotten path shows at once. The item price
  fan-out and revisions get targeted code.

For the recipient and picker parts there was one sensible way: lift the lock,
add a swap to the grave-invoice revision, widen the picker.

## Part 1 — A grave's recipients can change again

- `GraveService.SaveAsync` drops the `GraveRecipientsImmutable` check. All other
  recipient rules stay: 1 to 4 (FR-020), no duplicates, cooperative graves may
  have none, an ordinary grave that has recipients cannot be emptied
  (`NoRecipients`), and `CooperativeFlagFixed` stays as it is.
- The change applies to half-years generated afterwards. Invoices already issued
  keep their recipients and split; revising them (FR-034a) keeps working from the
  invoices of that half-year, not from the grave's current set — that is already
  how `GraveInvoiceRevisionService` picks its group. Its comment "A grave's
  recipients are fixed once invoiced, so the group is always the grave's full
  set" becomes wrong and must be rewritten: the group is the half-year's issued
  set, which may now differ from the grave's.
- The split check and rounding corrections already run on save for the new
  recipient count (`GraveBillingCheck.Evaluate` from the current half-year on),
  so a third recipient can bring up the corrections dialog, as on creation. A
  correction computed for a half-year that is already invoiced is stored but
  never applied, since generation skips invoiced half-years — acceptable.
- Editor (`GraveEditor.vue`): the hint `graveRecipientsFixed` and whatever
  disables adding/removing after the first invoice go. Each recipient is shown as
  a card (Part 3) with "Austauschen" and "Entfernen". "Austauschen" opens the
  picker in place of that card and replaces the recipient with the pick; it is a
  convenience for remove + add, the save sends the resulting set as today.
- The grave's own detail page shows the recipients the same way (number, name,
  full address) if it lists them.

## Part 2 — Swapping a recipient on an issued grave invoice

- `CreateGraveInvoiceRevision` takes an optional list of replacements:
  `{ invoiceId, recipientId }` — the latest revision of one recipient's invoice
  in the half-year's group, and the recipient who takes it over.
- Rules, each refused with its own code and a German message in the i18n
  catalogue:
  - the invoice must be one of the group's latest revisions;
  - the new recipient must exist and have an address
    (`RecipientAddressRequired`, as today);
  - the resulting set must not hold a recipient twice
    (`GraveRecipientLinkExists` or a new code — builder's choice);
  - a paid invoice in the group blocks the whole revision, as today
    (`GraveInvoicePaid`).
- A revision with only a swap and unchanged lines is valid. As today every
  recipient of the half-year gets a new revision (`-R<n>`); the swapped invoice's
  revision carries the new recipient's current name, address and number (FR-017
  snapshot). Number of recipients, `SharePercent` and `RecipientCount` stay.
- The grave itself is **not** changed by this. To bill the new recipient in
  future half-years too, the user changes the grave as well (decided: each change
  applies where it is made).
- Editor (`GraveInvoiceEditor.vue`): above the lines, the half-year's recipients
  as cards, each with a picker preset to the current recipient; the picker does
  not offer recipients already in the group. The discard guard counts a swap as
  a change.

Ordinary invoices need nothing new: `CreateRevision` already takes a recipient
and `InvoiceEditor.vue` already presets and sends it. They get the new picker
display from Part 3.

## Part 3 — Recipient display and the search bug

- `RecipientPicker.vue`:
  - each option shows `number · name` and below it the full address, every
    line (today: name and a one-line address, no number). Long text wraps as it
    does today (phone layout);
  - the field shows `number · name` for the pick;
  - the search looks in the number already (FR-016b) — unchanged;
  - **bug**: after a pick is taken (in the grave editor, after "Hinzufügen"
    resets the field), the Select's filter text and the server search term are
    cleared, so the next opening shows the newest recipients, not the last
    search.
- Under the picker, in `InvoiceEditor.vue` (invoices and quotes) and in the
  recipient cards of the grave and grave-invoice editors, the chosen recipient's
  full address is shown as a card:

  ```
  Empfänger
  [ E-0042 · Anna Beispiel                       ▾ ]
    Anna Beispiel
    Hauptstraße 12
    97070 Würzburg
  ```

- The recipient's number can be missing on old data; then only the name shows.

## Part 4 — The Verlauf

### What is recorded

One entry per subject (a grave, or an invoice chain) per action. An entry has:
when (UTC, shown in local time), the action, optional action details, and a list
of changes `{field, old, new}` with values already resolved to what a person
reads (item number and description, recipient number and name, money, month)
and captured at the time of writing — a recipient renamed later still reads as
they were.

Grave actions and what they record:

| Action                                               | Recorded on                                         | Changes                                                                                                                                                                                                |
| ---------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Grave created (editor)                               | grave                                               | every initial value, items with count and price, recipients                                                                                                                                            |
| Grave created by hand-over                           | new grave; predecessor                              | new: initial values and "takes over from <number>"; predecessor: its end month old → new                                                                                                               |
| Grave edited                                         | grave                                               | each changed field; items added/removed, count and own price old → new (incl. "Standardpreis" ↔ own price); recipients added/removed; rounding corrections added/removed/changed                       |
| Grave deleted / restored                             | grave                                               | —                                                                                                                                                                                                      |
| Grave imported                                       | grave                                               | initial values, as for created                                                                                                                                                                         |
| Price increase applied                               | every grave whose billed price for the item changes | item, billed price old → new (also for graves on the default price whose default rose); corrections that changed                                                                                       |
| Item's default price edited                          | every grave that billed the default                 | "Standardpreis von <item> geändert (old → new); Grab behält <old> als eigenen Preis" — the effective price does not change, the code pins the old default on the link (`InvoiceItemsController.Patch`) |
| Grave invoices generated                             | grave; each new invoice chain                       | grave: half-year and the invoice numbers; invoice: created, recipient, total                                                                                                                           |
| Grave invoice revised                                | grave; each chain of the group                      | grave: half-year, numbers; invoice: new revision number, lines added/removed/changed, recipient swapped, total old → new                                                                               |
| Marked paid / unpaid (single or from the grave list) | invoice chain; grave for grave invoices             | paid ↔ unpaid                                                                                                                                                                                          |

Invoice actions (ordinary invoices):

| Action               | Changes                                                                                                                              |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Created              | recipient, intro text, lines, total                                                                                                  |
| Created from quote   | as created, plus the quote number                                                                                                    |
| Revised              | revision number; recipient old → new; intro text; lines added/removed/changed (count, price, VAT-included, comment); total old → new |
| Marked paid / unpaid | paid ↔ unpaid                                                                                                                        |

An invoice chain's Verlauf is keyed by its `RootId`, so all revisions share one
Verlauf.

### Mechanism (approach C)

- **Declared action.** A scoped service (e.g. `AuditScope`) that every write
  path opens around its work: `using var audit = _audit.Begin(AuditAction.X,
details)`. One action may span several `SaveChanges` calls (the revision
  service saves per recipient; generation saves per grave).
- **Collected changes.** A `SaveChangesInterceptor` sees every tracked change
  to the audited entities — `Grave`, `GraveItem`, `GraveRecipient`,
  `GraveBillingCorrection`, `Invoice`, `InvoiceLine`, and `InvoiceItem`'s
  default price — and hands it to the open action.
- **Domain-level diffs.** Changes are reported per grave and per invoice chain
  in domain terms, regardless of how the service writes rows. `SaveGrave`
  replaces links (delete + insert): an item whose link was replaced with the same
  count and price is no change; one with a different count is "count 1 → 2", not
  "removed, added". The builder chooses how (pairing deleted and added rows by
  `ItemId`/`RecipientId`, or snapshotting the subject on first touch and on
  completion and diffing the two). Invoices are new rows per revision, so
  revisions diff against the predecessor revision explicitly.
- **Same transaction.** Entries are written in the transaction of the change and
  roll back with it; a refused action leaves no entry.
- **Guard.** An audited change saved without an open action throws in the
  Development and Testing environments, so the integration tests catch any path
  that forgot to declare itself. In Production it writes an entry with a generic
  action ("Änderung") and logs a warning, rather than blocking the user's work
  (my call).
- The item-price fan-out is explicit code in `ItemPriceIncreaseService` and
  `InvoiceItemsController.Patch`, which know the old and new prices and which
  graves were on the default.

### Storage

- One table `AuditEntries`: `Id`, `OccurredAt` (UTC), `Action` (string key),
  `Details` (JSON, nullable), `GraveId` (nullable, indexed), `InvoiceRootId`
  (nullable, indexed), `Changes` (JSON array). No foreign keys that cascade —
  entries outlive nothing, since graves and invoices are never hard-deleted, but
  they must never block a write either.
- Field and action names are keys (English); the German labels live in the
  i18n catalogue (Principle VII). Values are stored with a type hint
  (`money`, `month`, `date`, `text`, `position`, `bool`) so the frontend formats
  them like the rest of the app.
- This is the version's one migration (Principle IV); Parts 1–3 need no schema
  change.

### API and UI

- Read-only OData entity set `AuditEntries`, filterable by `GraveId` or
  `InvoiceRootId`, ordered newest first, paged. Regenerate the client
  (`npm run gen:odata`) and commit `metadata.xml` and `generated/`.
- A component `AuditTrail.vue` ("Verlauf") on `GraveDetailPage`,
  `InvoiceDetailPage` and `GraveInvoiceDetailPage`, below the existing content:
  newest first, 20 entries, "Weitere laden" for more; each entry shows date and
  time, the action label with its details, and the changes as
  `Feld: alt → neu`. An empty Verlauf says that changes are recorded from this
  version on. Grave entries about invoices link to the invoice.

```
Verlauf
26.11.2026 14:32  Preiserhöhung 1001 Grabpflege
                  Preis: 100,00 € → 105,00 €
12.11.2026 09:10  Grab bearbeitet
                  Empfänger: + E-0107 Carla Muster, − E-0002 Bernd Beispiel
                  Ende: 12/2035 → 12/2040
01.07.2026 08:00  Rechnungen 1. Hj. 2026 erzeugt  R2026-0412, R2026-0413
```

## Spec and docs changes

- `docs/spec.md`: rewrite FR-020b (recipients may change at any time, from the
  next generation on; issued invoices keep theirs) and User Story 5 scenario 5;
  add an FR for the swap on a grave-invoice revision (next to FR-034a); add FRs
  for the Verlauf (from FR-109 on) and for the picker display (next to FR-016a);
  add the edge case "recipients changed after a half-year was invoiced, then that
  half-year is revised".
- `CLAUDE.md` gains a short note on the audit guard, since a new write path that
  does not declare an action fails the tests for a reason that is not obvious
  from the failure.

## Testing

- Integration tests (backend): one per write path that its entry appears with
  the expected action and changes — including the price increase on a grave on
  the default price, the default-price edit pinning, a hand-over writing on both
  graves, generation, a grave-invoice revision with a swap, mark paid from the
  grave list; the guard throws for an undeclared change; a refused save writes
  nothing; a replaced link with unchanged values records no change.
- Integration tests for Part 1 and 2: changing recipients after invoicing is
  accepted and applies to the next generation; the issued half-year's revision
  keeps its own set; swap rules (duplicate, missing address, paid, not in group).
- `GraveRecipientImmutabilityTests` is rewritten to the new rule.
- E2E (on the E2E stack, via `e2e/vue.ts` helpers): edit a grave and see the
  Verlauf entry; swap a recipient on a grave invoice; add a recipient after
  invoicing; the picker shows number and address; the search is empty after
  adding a recipient. Phone/tablet layout of the Verlauf and the recipient
  cards in `layout.spec.ts`.

## Calls made after review (Claude's, for the user to check)

- **A save that changes nothing writes no entry.** Same for an item default-price
  edit that touches no grave.
- **Recomputed rounding corrections are no change.** Every grave save rebuilds
  its corrections, and those for half-years before the current one fall away on
  their own. Only corrections that differ for the current half-year or later are
  recorded.
- **The total an invoice entry records** is the gross the invoice bills — the
  recipient's share on a grave invoice — as the invoice list shows it.
- **Price fan-out reaches every grave whose link changes**, deleted and
  cooperative graves included: the Verlauf records what happened to the data,
  and a deleted grave can be restored.
- **Tests seed past the guard on purpose.** About 80 places in the integration
  tests insert graves and invoices directly; the test host gets an explicit
  seeding action, so the guard still catches production paths.
- **Deleting a recipient record itself** (from the recipient list, not removing
  them from a grave — that is Part 1 and in scope) stays as it is: the UI has no
  delete for recipient records, only the bare API, so no rule is added for a
  record that was removed from a grave but is still named on issued invoices.

## Left to the builder

- How the interceptor pairs replaced rows or snapshots subjects.
- Exact action and field keys, JSON shape, error code for a duplicate swap.
- Whether the Verlauf loads lazily (collapsed panel) or with the page.
- Ordering of recipient cards.
