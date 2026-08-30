import { expect, test, type Page } from "@playwright/test";
import { waitForDialogOpen } from "../dialog";
import { createRow, ensureRow } from "../seed";

// Layout regressions on small screens: every list screen must fit the viewport
// without sideways scrolling, its actions must stay reachable (directly or
// through the toolbar's overflow menu), and on a phone the table rows must
// collapse into pop-ins instead of squeezing every column into a few pixels.
//
// Runs in the "phone" and "tablet" projects only (see playwright.config.ts).

// The phone and the tablet project seed in parallel workers, so the run marker
// carries the worker index as well — two workers starting in the same
// millisecond would otherwise collide on the unique numbers below.
const RUN = `${Date.now()}-${process.env.TEST_PARALLEL_INDEX ?? "0"}`;
const ITEM_NUMBER = `L${RUN}`;
const RECIPIENT_NAME = `Layout ${RUN}`;
const GRAVE_NUMBER = `L-${RUN}`;

interface ListScreen {
  route: string;
  toolbar: string;
  /** Action that opens the create dialog; may sit in the overflow menu. */
  action: string;
  table: string;
}

const LIST_SCREENS: ListScreen[] = [
  {
    route: "items",
    toolbar: "itemsToolbar",
    action: "Leistung anlegen",
    table: "itemsTable",
  },
  {
    route: "recipients",
    toolbar: "recipientsToolbar",
    action: "Empfänger anlegen",
    table: "recipientsTable",
  },
  {
    route: "invoices",
    toolbar: "invoicesToolbar",
    action: "Rechnung erstellen",
    table: "invoicesTable",
  },
  {
    route: "graves",
    toolbar: "gravesToolbar",
    action: "Grab anlegen",
    table: "gravesTable",
  },
];

// The rows only have to exist — these assertions are about layout, not about
// the data — but their numbers are unique per run so that seeding never
// collides with rows a previous run left in the dev stack's database.
let seeding: Promise<void> | undefined;

function seed(page: Page): Promise<void> {
  seeding ??= seedRows(page);
  return seeding;
}

async function seedRows(page: Page): Promise<void> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: ITEM_NUMBER,
    Description: "Grabpflege mit einer längeren Beschreibung",
    VatRate: 7,
    DefaultPrice: 12.5,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: RECIPIENT_NAME,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  const grave = await createRow(page, "/odata/Graves", {
    GraveNumber: GRAVE_NUMBER,
    StartDate: "2026-01-01",
    EndDate: "2040-12-31",
  });
  await createRow(page, "/odata/GraveItems", {
    GraveId: grave.Id,
    ItemId: item.Id,
    Count: 1,
  });
  await createRow(page, "/odata/GraveRecipients", {
    GraveId: grave.Id,
    RecipientId: recipient.Id,
  });
  await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 12.5 }],
  });
}

/** Nothing may stick out sideways: the page must not scroll horizontally. */
async function expectNoSidewaysScrolling(page: Page): Promise<void> {
  // sub-pixel rounding is not a usability problem, hence the 1px tolerance
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      ),
    )
    .toBeLessThanOrEqual(1);
}

/**
 * The toolbar must move what does not fit into its overflow menu. Content wider
 * than the bar is clipped at the right edge, which silently swallows actions,
 * and a truncated label ("Leistung a...") makes an action unreadable.
 */
async function expectToolbarFits(page: Page, toolbar: string): Promise<void> {
  const bar = page.locator(`[id$='${toolbar}']`);
  const clipped = await bar.evaluate((el) => el.scrollWidth - el.clientWidth);
  expect(clipped).toBeLessThanOrEqual(1);

  const truncated = await page
    .locator(`[id$='${toolbar}'] .sapMBtnContent`)
    .evaluateAll((labels) =>
      labels
        .filter((label) => label.scrollWidth - label.clientWidth > 1)
        .map((label) => label.textContent ?? ""),
    );
  expect(truncated).toEqual([]);
}

/**
 * Clicks a toolbar action, opening the overflow menu first when the toolbar
 * moved the action in there.
 */
async function pressToolbarAction(page: Page, toolbar: string, name: string): Promise<void> {
  const button = page.getByRole("button", { name, exact: true });
  if (!(await button.isVisible())) {
    await page.locator(`[id$='${toolbar}-overflowButton']`).click();
  }
  await button.click();
}

const onPhone = () => test.info().project.name === "phone";

for (const screen of LIST_SCREENS) {
  test(`${screen.route}: fits the viewport and keeps its actions reachable`, async ({ page }) => {
    await seed(page);
    await page.goto(`/index.html#/${screen.route}`);

    // any row will do — this run seeded one, but other specs write to the same
    // stack in parallel and their rows can push it out of the visible page
    const table = page.locator(`[id$='${screen.table}-listUl']`);
    await expect(table.locator("tr.sapMListTblRow").first()).toBeVisible();
    await expectNoSidewaysScrolling(page);
    await expectToolbarFits(page, screen.toolbar);

    if (onPhone()) {
      // proof that the columns collapsed instead of being squeezed: the row
      // renders a pop-in row underneath carrying the remaining columns
      await expect(table.locator("tr.sapMListTblSubRow").first()).toBeVisible();
    }

    await pressToolbarAction(page, screen.toolbar, screen.action);
    // the width assertion below is only meaningful once the dialog has
    // finished animating in
    await waitForDialogOpen(page);
    const dialog = page.locator(".sapMDialog");
    await expect(dialog).toBeVisible();
    await expectNoSidewaysScrolling(page);

    if (onPhone()) {
      // the dialogs bind stretch to device>/system/phone, so a phone gets the
      // full screen instead of a window with fields cut off at its edges
      const width = await dialog.evaluate((el) => el.getBoundingClientRect().width);
      expect(width).toBeGreaterThan(test.info().project.use.viewport!.width * 0.9);
    }
    await dialog.getByRole("button", { name: "Abbrechen" }).click();
    await expect(dialog).toHaveCount(0);
  });
}

// The detail pages are built from panels rather than a toolbar and a table,
// so they get their own, narrower check. The grave-invoice detail page is not
// swept here: reaching it needs a generated grave invoice, and generation
// belongs to graves.spec.ts alone. It shares its markup with the invoice
// detail page, which is covered below.
test("detail pages fit the viewport", async ({ page }) => {
  await seed(page);

  const invoices = await (
    await page.request.get("/odata/Invoices?$filter=GraveId eq null&$select=Id,RootId&$top=1")
  ).json();
  const graves = await (await page.request.get("/odata/Graves?$select=Id&$top=1")).json();

  await page.goto(`/index.html#/invoices/${invoices.value[0].RootId}`);
  await expect(page.locator("[id$='invoiceDetailPage']")).toBeVisible();
  await expectNoSidewaysScrolling(page);

  await page.goto(`/index.html#/graves/${graves.value[0].Id}`);
  await expect(page.locator("[id$='graveDetailPage']")).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("settings: the form and the VAT rate row fit the viewport", async ({ page }) => {
  await seed(page);
  await page.goto("/index.html#/settings");

  await expect(page.getByLabel("Stempelwort")).toBeVisible();
  await expect(page.getByRole("button", { name: "MwSt.-Satz hinzufügen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("import/export: the page fits the viewport", async ({ page }) => {
  await page.goto("/index.html#/import-export");

  await expect(page.getByRole("button", { name: "Vorlage herunterladen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});
