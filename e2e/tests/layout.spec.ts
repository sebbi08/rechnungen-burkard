import { expect, test, type Page } from "@playwright/test";

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

/**
 * Creates a row and returns it. The whole suite writes to one SQLite file, and
 * under the load of the parallel specs a write can fail; a failed attempt is
 * retried and a permanent failure names the status instead of dying on an
 * unparsable body.
 */
async function createRow(
  page: Page,
  path: string,
  data: Record<string, unknown>,
): Promise<any> {
  let failure = "no attempt";
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await page.request.post(path, { data });
    const body = await response.text();
    if (response.ok() && body) {
      return JSON.parse(body);
    }
    failure = `HTTP ${response.status()} ${body}`;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`POST ${path} failed: ${failure}`);
}

async function seedRows(page: Page): Promise<void> {
  // VatRate 7 may already exist; the API then answers 409, which is harmless
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
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
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth,
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
async function pressToolbarAction(
  page: Page,
  toolbar: string,
  name: string,
): Promise<void> {
  const button = page.getByRole("button", { name, exact: true });
  if (!(await button.isVisible())) {
    await page.locator(`[id$='${toolbar}-overflowButton']`).click();
  }
  await button.click();
}

const onPhone = () => test.info().project.name === "phone";

for (const screen of LIST_SCREENS) {
  test(`${screen.route}: fits the viewport and keeps its actions reachable`, async ({
    page,
  }) => {
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

test("settings: the form and the VAT rate row fit the viewport", async ({
  page,
}) => {
  await seed(page);
  await page.goto("/index.html#/settings");

  await expect(page.getByLabel("Stempelwort")).toBeVisible();
  await expect(page.getByRole("button", { name: "MwSt.-Satz hinzufügen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("import/export: the page fits the viewport", async ({ page }) => {
  await page.goto("/index.html#/import-export");

  await expect(
    page.getByRole("button", { name: "Vorlage herunterladen" }),
  ).toBeVisible();
  await expectNoSidewaysScrolling(page);
});
