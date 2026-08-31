# Moving the VAT-Included Flag to the Line — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `PriceIncludesVat` off the catalogue item and onto the line — an
invoice line and a grave's item link — so the option is ticked where a price is
entered rather than where an item is created.

**Architecture:** The conversion itself does not change: `VatInclusivePrice.NetOf`
stays as it is, and the three call sites keep converting in the same place. They
read the flag from the submitted input or the stored link instead of from the
item. `GraveInvoiceSplitter` gains a passed-through field so a recipient's line
remembers what the grave's line was entered as. On the frontend the checkbox
moves into a column of its own in the three price-collecting dialogs, which lets
the previous version's `ariaLabelledBy`/`InvisibleText` workaround be reverted.

**Tech Stack:** .NET 10 / EF Core / SQLite (through `scripts/dotnet.sh` and
`scripts/ef.sh`), OpenUI5 1.151 with TypeScript, Playwright, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-31-vat-included-item-prices-design.md`
— read its "Revision" section first; it explains what moved and why.

## Global Constraints

- **One migration for this feature.** The branch is unreleased and no database
  has run the current migration, so `20260831144527_VatIncludedItemPrices` is
  **rewritten**, not supplemented. Task 1 owns it; no later task adds one.
- **`VatInclusivePrice.NetOf` does not change**, and neither does the split
  arithmetic in `GraveInvoiceSplitter`, `ShareCalculator`,
  `DocumentDataBuilder` or `InvoiceDocument`.
- The flag is read from the **line or the link**, never from the catalogue item.
  The item's `VatRate` is still the rate — that rule is untouched.
- **English source, identifiers and comments**; user-visible strings through
  i18n (Principle VII). **Conventional Commits.**
- **Backend tests:** `./scripts/dotnet.sh test Invoicing.sln`, narrowed with
  `--filter FullyQualifiedName~ClassName`. Run from the repository root. There is
  no local `dotnet` or `dotnet-ef`.
- **Frontend checks:** `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`.
- **E2E:** `cd e2e && npx playwright test tests/<file> --project=desktop`. It
  starts its own stack on 8081. **Never set `E2E_BASE_URL`; never touch the dev
  stack on 8080** — it holds the user's real data and the API has no DELETE
  endpoints.
- **A dialog takes focus ~230 ms after `open()`** — a spec that opens one calls
  `waitForDialogOpen(page)` from `e2e/dialog.ts` first.
- **Formatting is automatic** (CSharpier for `backend/`, Prettier elsewhere, from
  the `pre-commit` hook). Do not hand-format.
- Current state before this plan: 209 backend tests, 62/62 E2E, all checks clean.

---

### Task 1: The flag moves to the line and the link

**Files:**

- Modify: `backend/src/Invoicing.Domain/Entities/InvoiceItem.cs` (remove the property)
- Modify: `backend/src/Invoicing.Domain/Entities/Invoice.cs` (`InvoiceLine`)
- Modify: `backend/src/Invoicing.Domain/Entities/Grave.cs` (`GraveItem`)
- Delete + regenerate: `backend/src/Invoicing.Api/Migrations/20260831144527_VatIncludedItemPrices*.cs`
- Modify: `backend/src/Invoicing.Api/Migrations/AppDbContextModelSnapshot.cs` (generated)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs` (remove two tests)
- Test: `backend/tests/Invoicing.IntegrationTests/GraveTests.cs` (add one)

**Interfaces:**

- Consumes: nothing.
- Produces: `InvoiceLine.PriceIncludesVat` and `GraveItem.PriceIncludesVat`
  (both bool, default false); `InvoiceItem.PriceIncludesVat` no longer exists.
  Every later task depends on all three facts.

- [ ] **Step 1: Remove the two obsolete item tests, and write the new one**

In `backend/tests/Invoicing.IntegrationTests/InvoiceItemTests.cs`, delete
`An_item_is_priced_net_unless_it_says_otherwise` and
`An_item_can_be_marked_and_unmarked_as_priced_vat_included` — the catalogue no
longer carries the flag, so both now test something that does not exist.

Then append the test below to `backend/tests/Invoicing.IntegrationTests/GraveTests.cs`,
inside the class, following the file's existing style for seeding a grave and an
item. Write it **before** touching any entity, so the RED in Step 2 is real.

```csharp
    [Fact]
    public async Task A_grave_item_link_carries_its_vat_included_flag()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();
        await client.PostAsync("/odata/VatRates", ODataJson.Body(new { Rate = 19.0m }));

        var item = await client.PostAsync(
            "/odata/InvoiceItems",
            ODataJson.Body(
                new
                {
                    Number = "L-100",
                    Description = "Grabpflege",
                    VatRate = 19.0m,
                    DefaultPrice = 120.00m,
                }
            )
        );
        using var itemJson = await ODataJson.ReadAsync(item);
        var itemId = itemJson.RootElement.GetProperty("Id").GetInt64();

        var grave = await client.PostAsync(
            "/odata/Graves",
            ODataJson.Body(
                new
                {
                    GraveNumber = "L-1",
                    StartDate = "2026-01-01",
                    EndDate = "2040-12-31",
                }
            )
        );
        using var graveJson = await ODataJson.ReadAsync(grave);
        var graveId = graveJson.RootElement.GetProperty("Id").GetInt64();

        var link = await client.PostAsync(
            "/odata/GraveItems",
            ODataJson.Body(
                new
                {
                    GraveId = graveId,
                    ItemId = itemId,
                    Count = 1,
                    PriceIncludesVat = true,
                }
            )
        );
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        using var linkJson = await ODataJson.ReadAsync(link);
        Assert.True(linkJson.RootElement.GetProperty("PriceIncludesVat").GetBoolean());
    }
```

Check the file's existing usings cover `System.Net`, `System.Threading.Tasks`
and `Xunit`; add whatever is missing.

- [ ] **Step 2: Run it to verify it fails**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~GraveTests`
Expected: FAIL — `GraveItem` has no `PriceIncludesVat`, so OData rejects the
unknown property on POST or the response carries no such member.

- [ ] **Step 3: Move the property**

In `backend/src/Invoicing.Domain/Entities/InvoiceItem.cs`, delete the
`PriceIncludesVat` property and its doc comment entirely.

In `backend/src/Invoicing.Domain/Entities/Invoice.cs`, add to `InvoiceLine`
after `VatRate`:

```csharp
    /// <summary>True when this line's price was entered as a gross — the amount
    /// the customer pays, VAT included. <see cref="UnitPrice"/> is still the net
    /// it was converted to; this records how that net was arrived at, so a
    /// revision dialog can show the gross again and take it back unchanged.</summary>
    public bool PriceIncludesVat { get; set; }
```

In `backend/src/Invoicing.Domain/Entities/Grave.cs`, add to `GraveItem` after
`UnitPrice`:

```csharp
    /// <summary>True when this grave's price for the item was entered as a
    /// gross. Applies to whichever price is billed — the link's own
    /// <see cref="UnitPrice"/>, or the item's default standing in for it.</summary>
    public bool PriceIncludesVat { get; set; }
```

- [ ] **Step 4: Rewrite the migration**

The old migration is wrong now, and the branch is unreleased, so it is replaced
rather than added to. Delete both files:

```bash
rm backend/src/Invoicing.Api/Migrations/20260831144527_VatIncludedItemPrices.cs
rm backend/src/Invoicing.Api/Migrations/20260831144527_VatIncludedItemPrices.Designer.cs
```

Then regenerate against the new model:

```bash
./scripts/ef.sh migrations add VatIncludedLinePrices -p src/Invoicing.Api -s src/Invoicing.Api
```

Open the generated `Up` and confirm it is exactly two `AddColumn<bool>` calls —
`PriceIncludesVat` on `InvoiceLines` and on `GraveItems`, both
`nullable: false, defaultValue: false` — and **no** column on `InvoiceItems`. If
EF emits anything else, or emits a `DropColumn` on `InvoiceItems` (which would
mean the snapshot still remembers the old model), stop and report BLOCKED rather
than editing the generated file.

- [ ] **Step 5: Point the grave projection at the link**

In `backend/src/Invoicing.Api/OData/Controllers/GravesController.cs`, the grave
detail projection currently selects `i.Item.PriceIncludesVat` inside the nested
`Item = new { ... }`. Move it out to the link itself: delete that line from the
`Item` block and add `i.PriceIncludesVat,` beside `i.UnitPrice,` in the outer
projection.

- [ ] **Step 6: Run the suite**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: the two deleted item tests are gone and the new grave test passes.
Other tests referencing the item-level flag will still fail — Task 2 fixes them.
Report the failure list rather than papering over it; if only
`VatIncludedInvoiceTests`, `VatIncludedGraveTests` and `ImportTests` are red,
that is expected at this point.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat(invoices): carry the VAT-included flag on the line, not the item"
```

---

### Task 2: The conversion reads the line's flag

**Files:**

- Modify: `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs` (`GraveLine`, `Split`, `Combine`)
- Modify: `backend/src/Invoicing.Api/Services/InvoiceService.cs` (`InvoiceLineInput`, `BuildLinesAsync`)
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceRevisionService.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveInvoiceGenerator.cs`
- Modify: `backend/src/Invoicing.Api/Services/GraveService.cs` (`GraveItemInput`)
- Modify: `backend/src/Invoicing.Api/OData/Controllers/GraveInvoiceGroupController.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedInvoiceTests.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/VatIncludedGraveTests.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/GraveInvoiceSplitterTests.cs`

**Interfaces:**

- Consumes: Task 1's two columns.
- Produces: `InvoiceLineInput.PriceIncludesVat` and
  `GraveItemInput.PriceIncludesVat` (both bool), accepted by `/odata/CreateInvoice`,
  `/odata/CreateRevision`, `/odata/CreateGraveInvoiceRevision` and `/odata/SaveGrave`;
  `GraveInvoiceGroup`'s `Lines` carry `PriceIncludesVat`. Tasks 4 and 5 use all of it.

- [ ] **Step 1: Write the failing tests**

In `VatIncludedInvoiceTests.cs`, the seed currently creates a flagged and an
unflagged _item_. Rework it so both items are plain (no flag) and the tests pass
the flag per line instead. Every `lines = new[] { new { itemId, count, unitPrice } }`
gains `priceIncludesVat`. Concretely: `SeedAsync` returns two ordinary items at
120,00 and 100,00, and each test decides per line.

Keep every existing assertion value — the arithmetic does not change. Then make
sure the file covers, with `priceIncludesVat = true` on the flagged line:

- the default-price case totalling 120,00, and asserting the stored line's
  `PriceIncludesVat` is now `true`;
- the mixed invoice, one line ticked and one not;
- the revision that returns the shown gross and stores the identical net,
  sending `priceIncludesVat = true` back with it;
- the count-of-3 case totalling 360,00.

In `VatIncludedGraveTests.cs`, move the flag from the seeded item to the
`GraveItem`: `SeedAsync` creates a plain item at 120,00 and a `GraveItem` with
`PriceIncludesVat = true`. Add to the revision test's line inputs
`priceIncludesVat = true`. Keep every expected constant.

In `GraveInvoiceSplitterTests.cs`, add:

```csharp
    [Fact]
    public void Every_recipient_line_remembers_how_the_grave_price_was_entered()
    {
        var lines = new[] { new GraveLine(1, "1001", "Grabpflege", 1, 100.840m, 19m, true) };

        var split = GraveInvoiceSplitter.Split(lines, 2);

        Assert.All(split, recipient => Assert.True(Assert.Single(recipient).PriceIncludesVat));
        Assert.True(Assert.Single(GraveInvoiceSplitter.Combine(split)).PriceIncludesVat);
    }
```

Check the file's existing `GraveLine` constructions and add the new positional
argument to each of them.

- [ ] **Step 2: Run them to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~VatIncluded`
Expected: compile errors — `InvoiceLineInput` has no `PriceIncludesVat`, and
`GraveLine` takes six arguments, not seven.

- [ ] **Step 3: Carry the flag through the splitter**

In `backend/src/Invoicing.Domain/GraveInvoiceSplitter.cs`, add a seventh
positional field to the record:

```csharp
public sealed record GraveLine(
    long ItemId,
    string ItemNumber,
    string Description,
    int Count,
    decimal UnitPrice,
    decimal VatRate,
    bool PriceIncludesVat
);
```

In `Split`, add to the `InvoiceLine` it builds, after `VatRate = line.VatRate,`:

```csharp
                        // the recipient's line remembers what the grave's line was
                        // entered as, so reopening the edit dialog shows the gross
                        // again rather than this recipient's net share
                        PriceIncludesVat = line.PriceIncludesVat,
```

In `Combine`, add the same to the reconstructed `GraveLine`, taking it from
`template.PriceIncludesVat`.

- [ ] **Step 4: Take the flag from the input at the three call sites**

In `InvoiceService.cs`, add to `InvoiceLineInput`:

```csharp
    public bool PriceIncludesVat { get; set; }
```

In `BuildLinesAsync`, the `NetOf` call's third argument becomes
`input.PriceIncludesVat`, and the `InvoiceLine` gains
`PriceIncludesVat = input.PriceIncludesVat,`. Update the comment above it to say
the line, not the item, is what was entered as a gross.

In `GraveInvoiceRevisionService.cs`, `BuildGraveLinesAsync`: the `NetOf` call's
third argument becomes `input.PriceIncludesVat`, and the `GraveLine` gains
`input.PriceIncludesVat` as its seventh argument.

In `GraveInvoiceGenerator.cs`, the `NetOf` call's third argument becomes
`item.PriceIncludesVat` (the `GraveItem`'s own, not `item.Item`'s), and the
`GraveLine` gains `item.PriceIncludesVat` as its seventh argument.

In `GraveService.cs`, add to `GraveItemInput`:

```csharp
    public bool PriceIncludesVat { get; set; }
```

and set `PriceIncludesVat = input.PriceIncludesVat,` on the `GraveItem` it
builds, beside `UnitPrice = input.UnitPrice,`.

- [ ] **Step 5: Expose it on the group endpoint**

In `GraveInvoiceGroupController.cs`, the `Lines` projection selects the
`GraveLine`'s fields explicitly. Add `l.PriceIncludesVat,` after `l.VatRate,`.
The `GraveLines` helper builds `GraveLine`s from stored `InvoiceLine`s in its
`GraveUnitPrice` branch — give it `l.PriceIncludesVat` as the seventh argument
there too.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: everything green except `ImportTests`, which Task 3 fixes.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat(invoices): convert a price the line was entered as gross"
```

---

### Task 3: The import carries the flag on the mapping row

**Files:**

- Modify: `backend/src/Invoicing.Api/Services/Import/ImportArchiveLayout.cs`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportModel.cs`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportValidator.cs`
- Modify: `backend/src/Invoicing.Api/Services/Import/ImportService.cs`
- Test: `backend/tests/Invoicing.IntegrationTests/ImportTests.cs`
- Modify: `e2e/tests/import-export.spec.ts`

**Interfaces:**

- Consumes: `GraveItem.PriceIncludesVat` (Task 1).
- Produces: `items.csv` back to four columns; `mapping.csv` at five. Nothing
  later depends on it.

- [ ] **Step 1: Write the failing tests**

In `ImportTests.cs`:

- `ItemsHeader` goes back to `"ItemNumber,Description,VatRate,Price\r\n"`, and
  every `items.csv` data row loses its trailing fifth field. There are eight
  data-bearing literals — find them all; the header-only occurrences need no
  change.
- `MappingHeader` becomes `"Type,GraveNumber,Reference,Count,PriceIncludesVat\r\n"`,
  and every `mapping.csv` data row gains a trailing field: `Item` rows may carry
  a value, `Recipient` rows leave it empty.
- Rename `An_item_row_carries_its_vat_included_flag` to reflect the link, and
  assert on `GraveItems` rather than `InvoiceItems`: the archive's `Item` mapping
  row carrying `true` produces a `GraveItem` with `PriceIncludesVat` true, and a
  second link left empty produces false.
- Point `An_unreadable_vat_included_cell_is_reported` at a `mapping.csv` `Item`
  row whose fifth field is `vielleicht`, still expecting `PriceIncludesVatInvalid`
  for `mapping.csv`.
- Point `An_items_header_without_the_vat_included_column_is_rejected` at
  `mapping.csv` instead, and rename it accordingly.

- [ ] **Step 2: Run them to verify they fail**

Run: `./scripts/dotnet.sh test Invoicing.sln --filter FullyQualifiedName~ImportTests`
Expected: FAIL with `HeaderInvalid` on both files — the layout still has the old
shape.

- [ ] **Step 3: Move the column**

In `ImportArchiveLayout.cs`, restore `ItemsHeader` to its four original entries
and add `"PriceIncludesVat"` as the fifth entry of `MappingHeader`. Extend the
comment above `MappingHeader` so it says what the new column means: `Item` rows
carry a count and a VAT-included flag, `Recipient` rows leave both empty.

In `ImportModel.cs`, remove `PriceIncludesVat` from `ItemRow` and add it to
`ItemLinkRow`:

```csharp
public sealed record ItemLinkRow(
    int Row,
    string GraveNumber,
    string ItemNumber,
    int Count,
    bool PriceIncludesVat
);
```

- [ ] **Step 4: Move the parsing**

In `ImportValidator.cs`:

- `ReadItems`: restore `HasColumns(row, 4, ...)`, drop the `rawIncludesVat`
  read, drop the `TryParseBool` block, and construct `ItemRow` with its original
  five arguments.
- The mapping reader: raise its column guard to 5, read the fifth field, and for
  `Item` rows parse it with `TryParseBool`, reporting `PriceIncludesVatInvalid`
  against `ImportArchiveLayout.MappingFile` when it does not parse. Construct
  `ItemLinkRow` with the parsed flag. Leave `Recipient` rows alone — their fifth
  field is ignored exactly as their `Count` already is.
- `TryParseBool` itself does not change; it just moves usage.

In `ImportService.cs`, drop `PriceIncludesVat = row.PriceIncludesVat,` from the
`InvoiceItem` projection and set it on the `GraveItem` the item-link loop builds.

- [ ] **Step 5: Update the E2E archives**

In `e2e/tests/import-export.spec.ts`: `ITEMS_HEADER` back to its four columns and
its data rows back to four fields; `MAPPING_HEADER` gains `PriceIncludesVat` and
its `Item`/`Recipient` data rows gain a trailing field. Read the file and fix
every literal, not only the ones a grep for the header constant finds.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: the whole suite green.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/tests e2e
git commit -m "feat(import): carry the VAT-included flag on the mapping row"
```

---

### Task 4: The invoice dialog gets a checkbox column

**Files:**

- Modify: `frontend/src/view/fragments/ItemDialog.fragment.xml` (revert)
- Modify: `frontend/src/view/Items.view.xml` (revert)
- Modify: `frontend/src/controller/Items.controller.ts` (revert)
- Modify: `frontend/src/model/state.ts` (`ItemDraft`, `DraftLine`)
- Modify: `frontend/src/model/api.ts`
- Modify: `frontend/src/controller/InvoiceRevisions.controller.ts`
- Modify: `frontend/src/controller/InvoiceEdit.controller.ts`
- Modify: `frontend/src/view/fragments/InvoiceDialog.fragment.xml`
- Modify: `frontend/src/i18n/i18n.properties`, `i18n_en.properties`
- Test: `e2e/tests/vat-included.spec.ts`

**Interfaces:**

- Consumes: Tasks 1-2.
- Produces: `api.grossFromNet(net, vatRate)` (unchanged),
  `api.priceFieldValue(net, priceIncludesVat, vatRate)` (new signature),
  `InvoiceRevisions.loadItemVatRates(): Promise<Map<number, number>>`
  (renamed from `loadItemPricing`), and `api.ItemPricing` **removed**. Task 5
  uses all of them.

- [ ] **Step 1: Rewrite the E2E spec**

Replace `e2e/tests/vat-included.spec.ts` entirely — its first test covered the
item dialog, which no longer has a checkbox:

```ts
import { expect, test } from "@playwright/test";
import { waitForDialogOpen } from "../dialog";
import { createRow, ensureRow } from "../seed";

// Unique per run: the specs share one stack and write into it in parallel.
const RUN = Date.now();

test("a line ticked as VAT-included bills the price that was entered", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VI-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 120,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });

  await page.goto("/index.html#/invoices");
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForDialogOpen(page);

  const dialog = page.locator("[id$='invoiceDialog']");
  await dialog.getByRole("combobox").last().click();
  await page.getByText(`VI-${RUN}`).click();
  await expect(dialog.getByLabel("Preis")).toHaveValue("120");

  // the price above is the gross the customer pays
  await dialog.getByRole("checkbox").check();
  await dialog.getByRole("button", { name: "Speichern" }).click();

  // 120,00 gross in, 100,84 net + 19 % = 120,00 out
  await expect(page.getByText("120,00 €").first()).toBeVisible();
});

test("a ticked line reopens on its gross and re-saves to the same net", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VII-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 120,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto zwei ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: null, priceIncludesVat: true }],
  });

  await page.goto(`/index.html#/invoices/${created.RootId ?? created.Id}`);
  await expect(page.getByText("120,00 €").first()).toBeVisible();

  // the dialog opens on the gross with the box still ticked, not on the stored 100,84
  await page.getByRole("button", { name: "Bearbeiten" }).first().click();
  await waitForDialogOpen(page);
  const dialog = page.locator("[id$='invoiceDialog']");
  await expect(dialog.getByLabel("Preis")).toHaveValue("120");
  await expect(dialog.getByRole("checkbox")).toBeChecked();

  // saved untouched, the VAT must not come out a second time
  await dialog.getByRole("button", { name: "Speichern" }).click();
  await expect(page.getByText("Revision 1", { exact: true })).toBeVisible();
  await expect(page.getByText("120,00 €").first()).toBeVisible();
});
```

If a selector does not match what the UI renders, report it rather than
weakening the assertion — Task 5 copies this style.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: FAIL — there is no checkbox in the invoice dialog.

- [ ] **Step 3: Revert the item-level UI**

- `ItemDialog.fragment.xml`: delete the `itemPriceIncludesVat` `CheckBox`.
- `Items.view.xml`: restore the price cell to a bare
  `<Text text="{odata>DefaultPrice}" />`, removing the `VBox` and the marker.
- `Items.controller.ts`: drop `priceIncludesVat` from both branches of the draft
  and from the `fields` object in `onDialogSave`.
- `state.ts`: drop `priceIncludesVat` from `ItemDraft` (keep it on `DraftLine`).
- `api.ts`: drop `PriceIncludesVat` from `ItemRow`, `createItem` and
  `updateItem`.
- i18n: delete `itemPriceIncludesVat` from both files. Keep
  `priceIncludesVatShort` — the checkbox and the grave detail marker both use it.

- [ ] **Step 4: Reshape the helpers**

In `api.ts`, delete the `ItemPricing` interface and change `priceFieldValue` to
take the flag and the rate directly:

```ts
/** What a price field shows for a line's stored net price: the gross for a line
 *  entered VAT-included, and the untouched net for every other one — including
 *  the three decimals a grave split leaves behind. The rate is the item's
 *  current one, not the line's stored one: the backend converts what the dialog
 *  sends using the item as it is now, so displaying with an older rate would
 *  move the price of a line nobody touched. */
export function priceFieldValue(net: number, priceIncludesVat: boolean, vatRate: number): number {
  return priceIncludesVat ? grossFromNet(net, vatRate) : net;
}
```

In `InvoiceRevisions.controller.ts`, the catalogue read now needs only the rate:

```ts
  /**
   * Each item's current VAT rate, indexed by id, for the price fields. Both edit
   * dialogs already bind the whole catalogue into their item ComboBox, so this is
   * one more request rather than more data — and it has to be the catalogue
   * rather than the line, because the backend converts what is sent back using
   * the item's current rate.
   */
  protected async loadItemVatRates(): Promise<Map<number, number>> {
    const items = await this.readList<{ Id: number | string; VatRate: number | string }>(
      "/InvoiceItems",
      { $select: "Id,VatRate" },
    );

    return new Map(items.map((item) => [Number(item.Id), Number(item.VatRate)]));
  }
```

- [ ] **Step 5: Wire the invoice dialog**

In `InvoiceEdit.controller.ts`:

- rename the call and variable: `const rates = await this.loadItemVatRates();`,
  still inside the `invoiceId != null` branch and still inside its try/catch;
- in `loadLines`, replace the pricing lookup with
  `const vatRate = line.ItemId != null ? (rates.get(Number(line.ItemId)) ?? 0) : 0;`
  and set
  `unitPrice: api.priceFieldValue(Number(line.UnitPrice), Boolean(line.PriceIncludesVat), vatRate),`
  and `priceIncludesVat: Boolean(line.PriceIncludesVat),`;
- in `onLineItemSelected`, **delete** the two lines that set
  `priceIncludesVat` and `vatRate` from the item context — picking an item no
  longer has an opinion about the flag;
- in `onInvoiceDialogSave`, add `priceIncludesVat: line.priceIncludesVat,` to the
  mapped line objects.

In `api.ts`, add `PriceIncludesVat: boolean;` to `InvoiceLineRow`, and add
`priceIncludesVat: boolean;` to the line shape that `createInvoice` and
`createRevision` send.

- [ ] **Step 6: Give the checkbox its own column**

In `InvoiceDialog.fragment.xml`, add a fifth column after the price column:

```xml
<Column width="9rem" importance="Medium" popinDisplay="Inline">
                            <Text text="{i18n>priceIncludesVatShort}" />
                        </Column>
```

and restore the price cell to a plain `Input` — deleting the `VBox`, the
`core:InvisibleText`, the `ariaLabelledBy` and the comment explaining them,
because the `Input` is a direct child of its cell again and the table labels it
automatically. Then add the checkbox as the new cell:

```xml
                                <Input
                                    value="{invoiceDraft>unitPrice}"
                                    type="Number"
                                    width="7rem"
                                    placeholder="{invoiceDraft>defaultPrice}"
                                />
                                <CheckBox selected="{invoiceDraft>priceIncludesVat}" />
```

- [ ] **Step 7: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: clean — but only after you unbreak the grave side. Removing
`api.ItemPricing`, changing `priceFieldValue`'s signature and renaming
`loadItemPricing` **will** break `GraveEdit.controller.ts`, which uses all
three. Make the minimal mechanical change there to compile: rename the call to
`loadItemVatRates`, and pass the flag and the rate to `priceFieldValue`
positionally, reading the flag from `line.PriceIncludesVat` and the rate from
the map. Do **not** add the checkbox column or touch the grave fragments — that
is Task 5. List exactly what you changed in `GraveEdit.controller.ts` in your
report so Task 5 knows what it inherits.

- [ ] **Step 8: Run the E2E spec**

Run: `cd e2e && npx playwright test tests/vat-included.spec.ts --project=desktop`
Expected: PASS, 2 tests. Also run `tests/invoices.spec.ts` — it reaches the price
field with `getByLabel("Preis")` and is the proof the reverted labelling works.

- [ ] **Step 9: Commit**

```bash
git add frontend/src e2e/tests/vat-included.spec.ts
git commit -m "feat(invoices): tick VAT-included on the invoice line"
```

---

### Task 5: The grave dialogs get the same column

**Files:**

- Modify: `frontend/src/controller/GraveEdit.controller.ts`
- Modify: `frontend/src/view/fragments/GraveDialog.fragment.xml`
- Modify: `frontend/src/view/fragments/GraveInvoiceDialog.fragment.xml`
- Modify: `frontend/src/controller/GraveDetail.controller.ts`
- Modify: `frontend/src/view/GraveDetail.view.xml`
- Modify: `frontend/src/model/api.ts` (`GraveItemLink`, the grave save payload)
- Test: `e2e/tests/graves.spec.ts`

**Interfaces:**

- Consumes: everything from Tasks 1-4.
- Produces: nothing later depends on it.

- [ ] **Step 1: Update the E2E test**

In `e2e/tests/graves.spec.ts`, `seedGrave`'s `pricing` parameter currently sets
`PriceIncludesVat` on the **item**. Move it to the `GraveItems` POST instead:
the `InvoiceItems` POST loses the property, and the `GraveItems` POST gains
`PriceIncludesVat: pricing.priceIncludesVat ?? false`.

The existing test "a grave splits a VAT-included price without taking its VAT
out twice" keeps every assertion — the arithmetic is unchanged — but add, after
it opens the grave invoice dialog, that the checkbox is ticked:

```ts
await expect(dialog.getByRole("checkbox")).toBeChecked();
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd e2e && npx playwright test tests/graves.spec.ts --project=desktop -g "VAT-included"`
Expected: FAIL — the grave invoice dialog has no checkbox.

- [ ] **Step 3: Wire both grave dialogs**

In `GraveEdit.controller.ts`:

- `loadGraveDraft`'s `Items` map: `priceIncludesVat: link.PriceIncludesVat,`
  (the link's own, not `link.Item`'s), and delete the `vatRate` line if one
  remains;
- `onGraveItemSelected`: delete the lines that set `priceIncludesVat` and
  `vatRate` from the item context;
- the grave save payload: add `priceIncludesVat: row.priceIncludesVat,` to the
  mapped items;
- the grave invoice dialog: rename `loadItemPricing` to `loadItemVatRates`, and
  build each line as
  `unitPrice: api.priceFieldValue(Number(line.UnitPrice), Boolean(line.PriceIncludesVat), rates.get(Number(line.ItemId)) ?? 0),`
  with `priceIncludesVat: Boolean(line.PriceIncludesVat),`, keeping the existing
  comment about not rounding a pre-`GraveUnitPrice`-migration line;
- `onGraveInvoiceItemSelected`: delete the same two lines;
- `onGraveInvoiceDialogSave`: add `priceIncludesVat: row.priceIncludesVat,` to
  the mapped lines.

In `api.ts`: add `PriceIncludesVat: boolean;` to `GraveItemLink` (beside
`UnitPrice`, not inside `Item`), add it to the grave-invoice group's line shape,
and add `priceIncludesVat: boolean;` to the grave save and grave-revision
payloads.

- [ ] **Step 4: Give both fragments the column**

In `GraveDialog.fragment.xml` and `GraveInvoiceDialog.fragment.xml`, add the
same column after the price column:

```xml
<Column width="9rem" importance="Medium" popinDisplay="Inline">
                                <Text text="{i18n>priceIncludesVatShort}" />
                            </Column>
```

restore each price `Input` to a plain direct child of `<cells>` — deleting the
`VBox`, the `core:InvisibleText`, the `ariaLabelledBy` and their comment — and
add the checkbox cell immediately after it, bound to `graveDraft>priceIncludesVat`
and `graveInvoiceDraft>priceIncludesVat` respectively. Both fragments keep their
trailing delete-button column, so the checkbox column goes **before** it in both
`<columns>` and `<cells>`.

- [ ] **Step 5: Rebind the grave detail marker**

In `GraveDetail.controller.ts`, the `Items` projection reads
`link.Item.PriceIncludesVat`; change it to `link.PriceIncludesVat`. The view
needs no change — it already binds `{grave>PriceIncludesVat}` on the row.

- [ ] **Step 6: Run the frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean.

- [ ] **Step 7: Run the E2E specs**

Run: `cd e2e && npx playwright test tests/graves.spec.ts tests/vat-included.spec.ts --project=desktop`
Expected: PASS — the whole of `graves.spec.ts`, not just the new assertion,
since `seedGrave` changed and several specs reach these dialogs by label.

- [ ] **Step 8: Commit**

```bash
git add frontend/src e2e/tests/graves.spec.ts
git commit -m "feat(graves): tick VAT-included on a grave's item"
```

---

### Task 6: Full verification

No code. The gate before the branch is offered again.

- [ ] **Step 1: Backend**

Run: `./scripts/dotnet.sh test Invoicing.sln`
Expected: 0 failures. Two item tests were deleted and one grave test and one
splitter test added, so expect around 209.

- [ ] **Step 2: Frontend**

Run: `cd frontend && npm run typecheck && npm run lint && npm run ui5lint`
Expected: all three clean.

- [ ] **Step 3: The whole E2E suite**

Run: `cd e2e && npm test`
Expected: PASS across desktop, phone and tablet. If a spec fails, re-run it
alone before calling it a regression — the workers share one stack.

- [ ] **Step 4: Formatting**

Run: `npm run format:check`
Expected: clean.

- [ ] **Step 5: Confirm no second migration**

Run: `ls backend/src/Invoicing.Api/Migrations/`
Expected: exactly four migrations — the three that predate this feature and
`VatIncludedLinePrices`. If `VatIncludedItemPrices` is still there, Task 1's
rewrite did not happen and the branch violates the one-migration-per-version
rule.
