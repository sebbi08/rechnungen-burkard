# Design: Raising an Item's Prices by a Percentage

**Created**: 2026-11-13

**Status**: Draft design (pre-implementation)

**Source**: GitHub issue #67 ("Erhöhung von Leistung um X%"), refined with the
owner in conversation on 2026-11-13. The issue asks for a percentage increase of
item prices from the item section, in a popover that lists every increase, lets
each end price be adjusted, and always rounds to two decimals.

## 1. Why

Once a year the owner raises the prices of the items graves bill. Most of the
work is the annual grave care ("Jährliche Grabpflege"): grave sizes differ, so
many graves carry their own price for the same item, and there are many
different prices. Today each grave has to be opened and its price changed by
hand, and FR-009b keeps a changed default price away from graves that already
bill it. So changing the default alone changes nothing those graves bill.

Success: the owner opens an item, enters "5 %", checks the proposed prices,
rounds a few by hand, confirms, saves. Every grave that billed one of those prices
now bills the new one, and the rounding corrections the new prices need are in
place before the next half-year run.

## 2. Recorded Decisions

| Topic                 | Decision                                                                                                                                                                                                                                             | From                |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------- |
| Where                 | In the item edit dialog (`ItemDialog.vue`), one item at a time. A "Preise erhöhen" button sits in the heading of the section "Gräber mit dieser Leistung" and opens a popover                                                                        | owner               |
| What it raises        | The item's default price **and** the graves' own prices for the item, not the default alone                                                                                                                                                          | owner               |
| One row per price     | The popover lists each distinct price once, the way the dialog already groups the graves, with the number of graves on it. An end price changed there applies to every grave in that row                                                             | owner               |
| Pick single prices    | Each row has a checkbox, all ticked at first. An unticked row keeps its price. Raising every price of an item and raising just one of them both have to be possible                                                                                  | owner               |
| Adjustable end prices | Each ticked row's new price is proposed as old × (1 + p/100), rounded to two decimals, and can be overwritten                                                                                                                                        | issue, owner        |
| Rounding corrections  | Graves with several recipients whose subtotal no longer divides get their correction (FR-021a) computed as on a grave save. The corrections are listed in the popover and confirmed together with the increase                                       | owner               |
| Not correctable       | A row whose new price leaves some grave beyond the reach of a correction (`PricesDoNotDivide`, or a gross that does not add up) is marked red, naming the grave. The user enters another end price. "Übernehmen" stays disabled while any row is red | owner               |
| Live check            | Every change in the popover (the percentage, an end price, a checkbox) is checked against the server after a short pause, as the grave editor's billing preview is                                                                                   | owner               |
| Two steps             | "Übernehmen" closes the popover and shows old → new in the dialog. Nothing is written until the dialog's "Speichern". "Abbrechen" on the dialog discards the increase                                                                                | owner               |
| All or nothing        | On save the server checks again and applies every price and correction in one transaction, or refuses all of them                                                                                                                                    | owner (as proposed) |
| No migration          | Only existing columns change (`InvoiceItems.DefaultPrice`, `GraveItems.UnitPrice`, `GraveBillingCorrections`). D-005 is unaffected                                                                                                                   | follows             |

## 3. Behaviour

### 3.1 The rows

The popover is built from the same data the dialog's grave section uses today
(`GET /odata/ItemGraves?itemId=`, `ItemGravesController`), plus the item's
default price:

- **The default row ("Standardpreis")**, shown first, exists whenever the item
  has a default price, even if no grave bills it, because the default is still
  what new graves get. It covers the item's `DefaultPrice` and every non-deleted
  grave billing the default: a link with no own price, or with an own price equal
  to the default (FR-009b counts that as billing the default, comparing the
  number only, see 7.1). The dialog lists net and gross default graves in two
  groups. The popover merges them into this one row, because the default is one
  number, and each link keeps its gross flag. Its grave count is the number of
  distinct graves across both groups.
- **One row per own price**: each remaining group, keyed by (price, entered as
  gross). The label follows the dialog: "Eigener Preis", with "inkl. MwSt."
  added for a gross. Each row shows its grave count.
- Groups without any price ("ohne Preis") are not listed, since there is nothing
  to raise.

A grave can link the same item more than once. Every link in a ticked row is
raised, and the grave counts once per row.

Rows are ordered as the dialog orders its groups (default first, then by price,
net before gross).

### 3.2 The new prices

- The percentage field accepts a number greater than 0 with up to two decimals
  (e.g. 5 or 2,5). Empty or 0 proposes the old prices unchanged. There is no
  upper limit.
- The proposed new price is `round(old × (1 + p / 100), 2)`, midpoint away from
  zero, as prices are rounded elsewhere (`GraveBilling`). A gross stays a gross:
  the gross amount is what gets raised.
- A ticked row's new price can be overwritten with any amount ≥ 0,01 with at most
  two decimals. Changing the percentage afterwards proposes all prices again and
  overwrites manual edits. That is simple to understand and the owner rounds by
  hand last.
- An unticked row shows its old price greyed out and is left alone.

### 3.3 What applying means

For the ticked rows, on save:

- **Default row ticked** with new price N: the item's `DefaultPrice` becomes N.
  Links with no own price keep none and now bill N. Links whose own price equals
  the old default get N as their own price. FR-009b's "write the old default onto
  links billing it" does **not** run for this change, because the point is that
  they follow.
- **Default row unticked** while the default changes anyway: this cannot happen,
  since the default only changes through the default row (see 3.5).
- **Own-price row ticked** with new price N: every link of a non-deleted grave
  for this item with exactly that own price and gross flag gets `UnitPrice = N`.
  Its `PriceIncludesVat` stays.
- Links of deleted graves are never changed.
- Cooperative graves in a ticked row are raised like any other, since the dialog
  lists them too. They are never invoiced, so they are never checked or
  corrected.
- For every non-cooperative, non-deleted grave whose prices changed, the
  corrections are recomputed exactly as `SaveGrave` computes them
  (`GraveBillingCheck.Evaluate` over the grave's links with the new prices, its
  term, its recipient count and the current half-year). The grave's stored
  corrections are replaced with them. This includes ended graves: `Evaluate`
  already leaves out blocks that can no longer be generated.
- Invoices already generated, grave or otherwise, are untouched. The new prices
  apply to whatever is generated or revised from then on, with the same reading
  of prices as today.

A raised own price may come out equal to the (new or old) default. From then on
the dialog lists it under the default, as FR-009b already defines. That is
expected.

### 3.4 Check and confirmation in the popover

The popover asks the server for a check whenever its input settles (debounced
like the grave editor's preview). The answer, per row:

- **problems**: the graves (number, plus the failing invoice and VAT rate as
  `PricesDoNotDivide` names them) for which no correction within 1,00 € exists,
  or whose gross does not add up. The row turns red and lists them. It does not
  have to list every one if there are many: the first few plus a count is
  enough.

and for the whole increase:

- **corrections**: every correction the affected graves need after the increase
  (grave number, invoice, item, VAT rate, entered price, corrected price), as
  the grave save's confirmation lists them. Shown as one amber hint, collapsed to
  a count with the first example, expandable to the full list.

"Übernehmen" is enabled when there is at least one ticked row whose new price
differs from its old one, no row is red, and the latest check has answered for
the current input.

### 3.5 In the dialog after "Übernehmen"

- The popover closes. The grave section shows, for each changed row, the old
  price struck through and the new price ("Standardpreis ~~120,00~~ → 126,00 €").
  Unchanged rows stay as they are.
- If the default row changed, the default price field shows the new value and is
  read-only while the increase is pending. A small "Erhöhung verwerfen" action
  next to the button drops the pending increase and restores the field. This
  keeps the two ways of changing the default (the field, under FR-009b, and the
  increase, under 3.3) from mixing in one save.
- "Preise erhöhen" can be opened again. It opens on the pending values, and
  "Übernehmen" replaces them.
- Number, description, quarter and VAT rate remain editable as today.

### 3.6 Saving

"Speichern" with a pending increase:

1. Saves the item's own fields as today (`PATCH InvoiceItems`), sending the
   **old** default price, so FR-009b sees no default change. If that fails (e.g.
   `ItemNumberExists`), the error shows as today and the increase stays pending.
2. Applies the increase through a new action (3.7). If that fails, the dialog
   stays open with the error. The item fields are already saved, and the increase
   stays pending, so "Speichern" can simply be pressed again after the problem
   is resolved (or the increase discarded). On success the dialog closes as
   today, and the item list and its graves query are refreshed.

Without a pending increase, saving is unchanged.

### 3.7 API

Two unbound OData actions, following the existing pattern of request DTOs read
from the body (as `PreviewGraveBilling` / `SaveGrave`). Names are the builder's
to settle. Proposed: `PreviewItemPriceIncrease` and `ApplyItemPriceIncrease`.

Request (both):

| Field         | Meaning                                                                                                                           |
| ------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `ItemId`      | The item                                                                                                                          |
| `Rows`        | The ticked rows: `IsDefault`, `OldPrice`, `PriceIncludesVat` (ignored for the default row), `NewPrice`                            |
| `GraveIds`    | Per row, the graves the popover showed in it (apply only), so a grave that moved into or out of a row since is noticed            |
| `Corrections` | Apply only: the list the user confirmed, compared like `SaveGrave` compares it (order-independent, the same key fields per grave) |

Preview response: per row its problems (3.4), and the full list of corrections
with the grave they belong to.

Apply refuses the whole increase, writing nothing, with:

- `PricesChanged`: a row's old price no longer matches (the default changed, or
  the set of graves in a row differs from `GraveIds`).
- `PricesDoNotDivide`: some grave cannot be corrected (as on `SaveGrave`, naming
  the grave).
- `CorrectionsChanged`: the recomputed corrections differ from the confirmed
  ones.
- `InvalidPrice`: a new price below 0,01 or with more than two decimals.
- `NotFound` for an unknown item.

Each needs a German message in the frontend's error mapping, worded like its
neighbours. `PricesChanged` and `CorrectionsChanged` should tell the user to
open "Preise erhöhen" again, which reloads.

On success: the item's default (if its row was ticked), the links' prices and the
affected graves' corrections, all in one transaction.

## 4. Spec changes (`docs/spec.md`)

- New **FR-009c** next to FR-009b: raising an item's prices by a percentage from
  the item dialog, with rows per price, checkboxes, adjustable end prices rounded
  to two decimals, live check, confirmation of corrections, and all or nothing
  on save (3.1–3.6 condensed to requirement form).
- **FR-009b** gains one sentence: a default changed through the increase moves
  the graves billing the default along with it (3.3), unlike a default typed into
  the field.
- An acceptance scenario under the item user story: raising "Jährliche
  Grabpflege" by 5 % with one row unticked and one end price overwritten.
- An edge case: a new price that leaves a multi-recipient grave uncorrectable.

## 5. Tests

- **Integration (backend)**, for the two actions:
  - default and own prices raised, links updated, an unticked row untouched;
  - a default-billing link keeps no own price, and an own price equal to the old
    default gets the new one;
  - a gross row stays gross;
  - a deleted grave untouched, a cooperative grave raised but not corrected;
  - a correction needed after the increase is returned by preview and stored by
    apply;
  - a correction no longer needed is removed;
  - an uncorrectable grave is reported per row and makes apply refuse;
  - `PricesChanged` when a grave's price changed between preview and apply;
  - `CorrectionsChanged`;
  - `InvalidPrice`;
  - after apply, the generation precheck (`GraveCorrectionCheck`) reports no
    outdated graves;
  - FR-009b still holds for a default typed into the field.
- **E2E** (`e2e/tests/`): seed an item with three graves at two own prices and the
  default, open the item, raise by 5 %, untick one row, overwrite one end price,
  "Übernehmen", "Speichern", and read the links back through the API. A second
  spec seeds a two-recipient grave whose new price needs a correction, and checks
  the hint and the stored correction.
- Frontend typecheck, lint, build as usual.

## 6. Non-goals

- Raising several items at once, or doing it from the item list. The owner
  chose the dialog.
- Lowering prices by a percentage. The field takes only increases. A lower end
  price can still be typed by hand in a row.
- A history or undo of increases. The old prices are visible in the popover until
  saved, and afterwards the owner can raise or edit again.
- Changing prices on invoices, quotes or grave invoices already generated.
- Invoice and quote lines: they copy the default at the time they are written,
  so a raised default simply shows up as the prefill next time.

## 7. Calls made after the builder review

These were settled by the designer, not the owner. Each follows the existing
code.

1. **"Billing the default" compares the number only**, as `ItemGravesController`
   and FR-009b do today. A link whose own price equals the default belongs to the
   default row whatever its gross flag. A link without an own price does too,
   since its gross flag says how the default is read for that grave. When raised
   it gets the new number and keeps its flag. No separate "Eigener Preis inkl.
   MwSt." row is made for such links.
2. **A grave may be split across rows.** Corrections are always computed over
   all of a grave's links with the new prices of the ticked rows and the old
   prices elsewhere, so a partly raised grave is checked as it will be billed.
   `GraveIds` is compared per row as an exact set. A grave in two rows is listed
   in both, and a red row names every grave that fails in it.
3. **Corrections are replaced exactly as `SaveGrave` would store them** for a
   grave with its new prices. A grave `SaveGrave` would not evaluate (no
   recipients, or cooperative) keeps no corrections. A one-recipient grave
   evaluates to none. The shared helper's contract is therefore "what a save of
   this grave with these prices would store".
4. **The increase never touches the VAT rate or the quarter.** The `PATCH` in
   3.6 sends what the dialog sends today, with the old default. Its existing
   locks apply unchanged.
5. **A default row for an item without a default price** (or with a different
   default than `OldPrice`) is refused with `PricesChanged`.
6. **The current half-year** comes from the server's `IClock` in both preview and
   apply, as on `SaveGrave`. It is not part of the request. A rollover between
   the two shows up as `CorrectionsChanged`, which asks the user to reopen the
   popover.

## 8. Left to the builder

- Action names, DTO field names, and where the shared "apply new prices to a
  grave's links and recompute its corrections" code lives (it should reuse
  `GraveBillingCheck` and the correction mapping `GraveService` uses, not copy
  them).
- PrimeVue component for the popover (`Popover` or a small `Dialog`). It has to
  hold a table, so a small modal `Dialog` on phones is fine.
- How many problem graves a red row names before "und N weitere".
- The debounce interval (match the grave editor).
