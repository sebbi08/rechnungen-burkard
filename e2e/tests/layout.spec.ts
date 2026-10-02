import { expect, type Locator, type Page, test } from "@playwright/test";
import { createRow, ensureRow, seedGrave } from "../seed";
import { drawer, pickItem, pickRecipient, rowAction, waitForOverlayOpen } from "../vue";

// Layout regressions on small screens: every screen must fit the viewport
// without sideways scrolling, its actions must stay reachable and readable, and
// on a phone the tables must drop their secondary columns instead of squeezing
// every column into a few pixels.
//
// Runs in the "phone" and "tablet" projects only (see playwright.config.ts).

// The phone and the tablet project seed in parallel workers, so the run marker
// carries the worker index as well — two workers starting in the same
// millisecond would otherwise collide on the unique numbers below.
const RUN = `${Date.now()}-${process.env.TEST_PARALLEL_INDEX ?? "0"}`;
// long on purpose: a number and a name that fit a narrow column anyway would
// not notice a column too narrow to hold them
const ITEM_NUMBER = `L${RUN}-GRABPFLEGE`;
const RECIPIENT_NAME = `Layout ${RUN} Hohenberg-Schwarzenstein`;
const GRAVE_NUMBER = `L-${RUN}-GRABSTELLE`;

/** Tailwind's breakpoints, from which a column a narrow screen drops comes back. */
const BREAKPOINTS = { sm: 640, md: 768, xl: 1280 };

interface ListScreen {
  route: string;
  /** What this run's own row in the list shows. */
  seeded: string;
  /** The action that opens the create dialog or editor. */
  action: string;
  /** Secondary columns, each with the width from which the table shows it. */
  secondary: Record<string, keyof typeof BREAKPOINTS>;
}

const LIST_SCREENS: ListScreen[] = [
  {
    route: "items",
    seeded: ITEM_NUMBER,
    action: "Leistung anlegen",
    secondary: { "MwSt.-Satz": "md", Standardpreis: "sm" },
  },
  {
    route: "recipients",
    seeded: RECIPIENT_NAME,
    action: "Empfänger anlegen",
    secondary: { Empfängernummer: "sm", Adresse: "md" },
  },
  {
    route: "invoices",
    seeded: RECIPIENT_NAME,
    action: "Rechnung erstellen",
    secondary: { Name: "sm", Datum: "md", Zahlung: "sm" },
  },
  {
    route: "graves",
    seeded: GRAVE_NUMBER,
    action: "Grab anlegen",
    secondary: { Beginn: "xl", Ende: "xl", Zahlung: "sm" },
  },
];

// The rows only have to exist — these assertions are about layout, not about
// the data — but their numbers are unique per run so that seeding never
// collides with rows an earlier attempt left in a stack that kept its data.
let seeding: Promise<number> | undefined;

/** Seeds this worker's rows once, and answers its invoice's root id. */
function seed(page: Page): Promise<number> {
  seeding ??= seedRows(page);
  return seeding;
}

async function seedRows(page: Page): Promise<number> {
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
  await seedGrave(page, {
    graveNumber: GRAVE_NUMBER,
    items: [{ itemId: item.Id, count: 1 }],
    recipientIds: [recipient.Id],
  });
  const invoice = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 12.5 }],
  });
  return invoice.RootId;
}

// Layout bugs these specs found in the frontend, left for a session of their
// own. Each test that runs into one is marked with `test.fail`, so the
// suite stays green while the bug is there and turns red once it is fixed —
// which is the reminder to drop the mark.
const TABLES_OVERFLOW =
  "known bug: on a phone a number without a break in it (an item's, a grave's) widens its " +
  "column past the card, so the row actions sit outside it until the table is scrolled sideways";
const PICKER_SQUEEZED =
  "known bug: on a phone the grave editor's recipient picker shares its row with two buttons " +
  "and is left about 12rem, too narrow for a long name";

const onPhone = () => test.info().project.name === "phone";

const widthOf = (locator: Locator): Promise<number> =>
  locator.evaluate((el) => el.getBoundingClientRect().width);

async function remInPixels(page: Page): Promise<number> {
  return page.evaluate(() => parseFloat(getComputedStyle(document.documentElement).fontSize));
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
 * The page's own actions must all be on screen and readable. The header wraps
 * them onto further lines rather than moving them into a menu, so what can go
 * wrong is a button pushed past the right edge or a label cut short
 * ("Leistung a...").
 */
async function expectHeaderActionsFit(page: Page): Promise<void> {
  const viewport = page.viewportSize()!;
  const buttons = page.locator("main > div").first().getByRole("button");
  expect(await buttons.count()).toBeGreaterThan(0);
  for (const button of await buttons.all()) {
    const box = (await button.boundingBox())!;
    expect(box.x, await button.innerText()).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, await button.innerText()).toBeLessThanOrEqual(viewport.width + 1);
  }
  const truncated = await buttons.evaluateAll((elements) =>
    elements
      .filter((element) => element.scrollWidth - element.clientWidth > 1)
      .map((element) => element.textContent ?? ""),
  );
  expect(truncated).toEqual([]);
}

for (const screen of LIST_SCREENS) {
  test(`${screen.route}: fits the viewport and keeps its actions reachable`, async ({ page }) => {
    await seed(page);
    await page.goto(`/${screen.route}`);

    // any row will do — this run seeded one, but other specs write to the same
    // stack in parallel and their rows can push it out of the visible page
    const table = page.getByRole("table");
    await expect(table.locator("tbody tr").first()).toBeVisible();
    await expect(table.locator("tbody tr").first()).not.toContainText("Keine Daten");
    await expectNoSidewaysScrolling(page);
    await expectHeaderActionsFit(page);

    // proof that the columns collapsed instead of being squeezed: a narrow
    // screen does without the secondary ones, a wider one gets them back
    for (const [header, from] of Object.entries(screen.secondary)) {
      const column = table.getByRole("columnheader", { name: header, exact: true });
      if (page.viewportSize()!.width < BREAKPOINTS[from]) {
        await expect(column, header).toBeHidden();
      } else {
        await expect(column, header).toBeVisible();
      }
    }

    await page.getByRole("button", { name: screen.action, exact: true }).click();
    // the width assertion below is only meaningful once the dialog has
    // finished animating in
    await waitForOverlayOpen(page);
    const overlay = page.locator(".p-dialog, .p-drawer");
    await expect(overlay).toBeVisible();
    await expectNoSidewaysScrolling(page);

    if (onPhone()) {
      // a phone gets the full screen instead of a window with fields cut off
      // at its edges
      expect(await widthOf(overlay)).toBeGreaterThan(page.viewportSize()!.width * 0.9);
    }
    await overlay.getByRole("button", { name: "Abbrechen" }).click();
    await expect(overlay).toHaveCount(0);
  });
}

for (const screen of LIST_SCREENS) {
  test(`${screen.route}: the row actions are on screen without scrolling the table`, async ({
    page,
  }) => {
    test.fail(onPhone() && ["items", "graves"].includes(screen.route), TABLES_OVERFLOW);
    await seed(page);
    await page.goto(`/${screen.route}`);
    const row = page.locator("tbody tr").filter({ hasText: screen.seeded }).first();
    await expect(row).toBeVisible();

    // the part of the table its card shows, without scrolling it sideways
    const shown = await page.locator(".p-datatable-table-container").evaluate((el) => {
      const box = el.getBoundingClientRect();
      return { left: box.left, right: box.left + el.clientWidth };
    });
    const actions = row.locator("td").last().getByRole("button").filter({ visible: true });
    expect(await actions.count()).toBeGreaterThan(0);
    for (const action of await actions.all()) {
      const box = (await action.boundingBox())!;
      const name = (await action.getAttribute("aria-label")) ?? "";
      expect(box.x, name).toBeGreaterThanOrEqual(shown.left - 1);
      expect(box.x + box.width, name).toBeLessThanOrEqual(shown.right + 1);
    }
  });
}

test("invoices: every row action stays reachable", async ({ page }) => {
  await seed(page);
  await page.goto("/invoices");
  const row = page.getByRole("row").filter({ hasText: RECIPIENT_NAME }).first();
  await expect(row).toBeVisible();

  const actions = [
    "Details",
    "Rechnung herunterladen",
    "Kopie herunterladen",
    "Bearbeiten",
    "Als bezahlt markieren",
  ];
  if (page.viewportSize()!.width >= BREAKPOINTS.xl) {
    for (const name of actions) {
      await expect(row.getByRole("button", { name: rowAction(name) })).toBeVisible();
    }
    return;
  }

  // Below the widest screens there is no room for five buttons a row, beside
  // the side bar or without it: they sit in a menu instead, and the menu has
  // to fit the screen. The menu closes when anything behind
  // it scrolls, so nothing may need to: its button is brought to the middle of
  // the screen first, where the menu has room below it, and fully into the
  // table's view, should the table be wider than its card.
  const more = row.getByRole("button", { name: rowAction("Weitere Aktionen") });
  await more.evaluate((element) => element.scrollIntoView({ block: "center", inline: "end" }));
  await more.click();
  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  await expect(page.locator(".p-menu")).not.toHaveClass(/p-connected-overlay-enter-active/);
  for (const name of actions) {
    await expect(menu.getByRole("menuitem", { name, exact: true })).toBeVisible();
  }
  const box = (await menu.boundingBox())!;
  const viewport = page.viewportSize()!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1);
  expect(box.y).toBeGreaterThanOrEqual(0);
  expect(box.y + box.height).toBeLessThanOrEqual(viewport.height + 1);

  // An item leads where its button does. Under load the open menu has been
  // seen to close under the tap that was meant for it, so a tap that did not
  // lead anywhere is made again, from a freshly opened menu.
  const step = { timeout: 3000 };
  await expect(async () => {
    if (!/\/invoices\/\d+$/.test(page.url())) {
      if (!(await menu.isVisible())) {
        await more.click(step);
      }
      await expect(page.locator(".p-menu")).not.toHaveClass(
        /p-connected-overlay-enter-active/,
        step,
      );
      await menu.getByRole("menuitem", { name: "Details", exact: true }).click(step);
    }
    await expect(page).toHaveURL(/\/invoices\/\d+$/, step);
  }).toPass({ timeout: 30_000 });
});

test("the navigation is reachable on every screen size", async ({ page }) => {
  await page.goto("/invoices");
  // the menu closes whenever the route changes, and the first page arriving
  // is a change of route too
  await expect(page.getByRole("heading", { level: 1, name: "Rechnungen" })).toBeVisible();
  const sidebar = page.locator("aside").getByRole("navigation", { name: "Navigation" });

  // the side bar takes 16rem, so only a screen of at least 1024px shows it;
  // the phone and the tablet keep it behind their menu button
  if (page.viewportSize()!.width >= 1024) {
    await expect(sidebar).toBeVisible();
    await expect(page.getByRole("button", { name: "Menü" })).toBeHidden();
    return;
  }

  await expect(sidebar).toBeHidden();
  await page.getByRole("button", { name: "Menü" }).click();
  await waitForOverlayOpen(page);
  const menu = drawer(page);
  // a light page: the menu closes once the page it leads to has rendered, and
  // a list of every grave in the stack takes a slow machine many seconds
  await menu.getByRole("link", { name: "Einstellungen", exact: true }).click();
  await expect(page).toHaveURL(/\/settings$/);
  // choosing a section closes the menu again
  await expect(menu).toHaveCount(0);
  await expectNoSidewaysScrolling(page);
});

/**
 * Two editors collect invoice lines — the grave editor and the invoice editor —
 * and both put them into a drawer that a phone shows full screen. Those rows
 * have the least room in the app, and it has to go to the fields rather than
 * to gutters. The first frontend's dialogs once left the item's number field
 * some ten characters, which showed "L17565..." and hid the rest of the number.
 *
 * A number long enough outruns any column, so what is asserted is the room the
 * field gets, not that this run's number happens to fit.
 */
async function expectFieldsGetTheWidth(page: Page): Promise<void> {
  const editor = drawer(page);
  const rem = await remInPixels(page);

  const itemField = editor.getByRole("combobox", { name: "Nummer 1" });
  await expect(itemField).toHaveText(ITEM_NUMBER);
  const itemSelect = editor.locator(".p-select", {
    has: page.getByRole("combobox", { name: "Nummer 1" }),
  });
  expect(await widthOf(itemSelect), "the item number field is squeezed").toBeGreaterThan(12 * rem);

  // the lines must not sit in a gutter
  const editorWidth = await widthOf(editor);
  const firstLine = editor.locator(".grid", {
    has: page.getByRole("combobox", { name: "Nummer 1" }),
  });
  expect(await widthOf(firstLine), "the line is squeezed").toBeGreaterThan(editorWidth * 0.85);
}

/** The recipient picker, measured the same way: at a fixed 12rem the first
 *  frontend's showed "Layout 17565..." and left the recipient of the next click a guess.
 *  A phone narrower than 20rem plus the editor's padding gets most of its width. */
async function expectPickerGetsTheWidth(page: Page, recipientPicker: string): Promise<void> {
  const editor = drawer(page);
  const rem = await remInPixels(page);
  const picker = page.locator(`#${recipientPicker}`);
  await expect(picker).toHaveText(RECIPIENT_NAME);
  const pickerSelect = editor.locator(".p-select", { has: page.locator(`#${recipientPicker}`) });
  expect(await widthOf(pickerSelect), "the recipient picker is squeezed").toBeGreaterThan(
    Math.min(20 * rem, (await widthOf(editor)) * 0.8),
  );
}

test("graves: the editor spends its width on the fields", async ({ page }) => {
  test.fail(onPhone(), PICKER_SQUEEZED);
  await seed(page);
  await page.goto("/graves");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  await page.getByRole("button", { name: "Grab anlegen", exact: true }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);

  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, ITEM_NUMBER);
  await pickRecipient(page, "graveRecipient", RECIPIENT_NAME);
  await editor.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  const added = editor.getByRole("listitem").filter({ hasText: RECIPIENT_NAME });
  await expect(added).toBeVisible();

  await expectFieldsGetTheWidth(page);
  await expectNoSidewaysScrolling(page);

  // the recipient list gets the editor's width too
  expect(await widthOf(added)).toBeGreaterThan((await widthOf(editor)) * 0.85);

  // last, since on a phone it is where this test stops (see PICKER_SQUEEZED)
  await expectPickerGetsTheWidth(page, "graveRecipient");

  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);
});

test("invoices: the editor spends its width on the fields", async ({ page }) => {
  await seed(page);
  await page.goto("/invoices");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  await page.getByRole("button", { name: "Rechnung erstellen", exact: true }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);

  await pickRecipient(page, "invoiceRecipient", RECIPIENT_NAME);
  // unlike the grave editor this one opens with an empty line already in place
  await pickItem(page, 1, ITEM_NUMBER);

  await expectFieldsGetTheWidth(page);
  await expectPickerGetsTheWidth(page, "invoiceRecipient");
  await expectNoSidewaysScrolling(page);

  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);
});

// The grave-invoice detail page is not swept here: reaching it needs a
// generated grave invoice, and generation belongs to graves.spec.ts alone. It
// shares its revision panels with the invoice detail page, covered below.
test("the invoice detail page fits the viewport", async ({ page }) => {
  const rootId = await seed(page);

  await page.goto(`/invoices/${rootId}`);
  await expect(page.locator(".p-accordionpanel").first()).toContainText(ITEM_NUMBER);
  await expectHeaderActionsFit(page);
  await expectNoSidewaysScrolling(page);
});

test("the grave detail page fits the viewport", async ({ page }) => {
  await seed(page);
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${GRAVE_NUMBER}'&$select=Id`)
  ).json();

  await page.goto(`/graves/${graves.value[0].Id}`);
  await expect(page.getByRole("heading", { level: 1, name: GRAVE_NUMBER })).toBeVisible();
  await expect(page.getByRole("cell", { name: ITEM_NUMBER })).toBeVisible();
  await expectHeaderActionsFit(page);
  await expectNoSidewaysScrolling(page);
});

test("settings: the form and the VAT rate row fit the viewport", async ({ page }) => {
  await seed(page);
  await page.goto("/settings");

  await expect(page.getByLabel("Stempelwort")).toBeVisible();
  await expect(page.getByRole("button", { name: "MwSt.-Satz hinzufügen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("import/export: the page fits the viewport", async ({ page }) => {
  await page.goto("/import-export");

  await expect(page.getByRole("button", { name: "Vorlage herunterladen" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Importieren", exact: true })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});
