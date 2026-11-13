import { expect, type Locator, type Page, test } from "@playwright/test";
import { createRow, ensureCemetery, ensureRow, SEED_CEMETERY, seedGrave, seedQuote } from "../seed";
import { unique } from "../unique";
import {
  dialog,
  discardEditor,
  drawer,
  fillDate,
  openPopover,
  pickItem,
  pickOption,
  pickRecipient,
  rowAction,
  waitForOverlayOpen,
} from "../vue";

// Layout regressions on small screens: every screen must fit the viewport
// without sideways scrolling, its actions must stay reachable and readable, and
// on a phone the tables must drop their secondary columns instead of squeezing
// every column into a few pixels.
//
// Runs in the "phone" and "tablet" projects only (see playwright.config.ts).

// Every worker that runs a test of this file seeds its own rows, and several of
// them start at once, so the marker carries the worker index as well — two
// workers starting in the same millisecond would otherwise collide on the
// unique numbers below.
const RUN = unique();
// long on purpose: a number and a name that fit a narrow column anyway would
// not notice a column too narrow to hold them. The numbers have no space in
// them and are longer than a tablet's table has room for, so a column that
// cannot break them pushes the row actions off the card on every run, not only
// when other specs happen to have left a long number in the list.
const ITEM_NUMBER = `L${RUN}-GRABPFLEGE-SOMMERBEPFLANZUNG-HERBST`;
const RECIPIENT_NAME = `Layout ${RUN} Hohenberg-Schwarzenstein`;
const GRAVE_NUMBER = `L-${RUN}-GRABSTELLE-SCHINELLER-BAECKE-NORD`;
const COOPERATIVE_NUMBER = `L-${RUN}-GENOSSENSCHAFTSGRAB-SCHINELLER-BAECKE-SUED`;

/** Tailwind's breakpoints, from which a column a narrow screen drops comes back. */
const BREAKPOINTS = { sm: 640, md: 768, lg: 1024, xl: 1280, "2xl": 1536 };

interface ListScreen {
  route: string;
  /** What this run's own row in the list shows. */
  seeded: string;
  /** The column search, as the list's URL query, that leaves this run's row:
   *  a page holds ten rows, and other specs' rows would push it off. */
  search: Record<string, string>;
  /** The action that opens the create dialog or editor. */
  action: string;
  /** Secondary columns, each with the width from which the table shows it. */
  secondary: Record<string, keyof typeof BREAKPOINTS>;
}

const LIST_SCREENS: ListScreen[] = [
  {
    route: "items",
    seeded: ITEM_NUMBER,
    search: { number: ITEM_NUMBER },
    action: "Leistung anlegen",
    secondary: { "MwSt.-Satz": "md", Standardpreis: "sm" },
  },
  {
    route: "recipients",
    seeded: RECIPIENT_NAME,
    search: { name: RECIPIENT_NAME },
    action: "Empfänger anlegen",
    secondary: { Empfängernummer: "sm", Adresse: "md" },
  },
  {
    route: "invoices",
    seeded: RECIPIENT_NAME,
    search: { recipient: RECIPIENT_NAME },
    action: "Rechnung erstellen",
    secondary: { Empfänger: "sm", Datum: "md", Betrag: "md", Zahlung: "sm" },
  },
  {
    route: "quotes",
    seeded: RECIPIENT_NAME,
    search: { recipient: RECIPIENT_NAME },
    action: "Neues Angebot",
    secondary: { Empfänger: "sm", Datum: "md", Betrag: "md", Status: "sm" },
  },
  {
    route: "graves",
    seeded: GRAVE_NUMBER,
    search: { number: GRAVE_NUMBER },
    action: "Grab anlegen",
    secondary: {
      Grabstätte: "lg",
      Friedhof: "lg",
      "Feld-Nr.": "lg",
      Beginn: "2xl",
      Ende: "2xl",
      Empfänger: "sm",
      Zahlung: "sm",
    },
  },
  {
    route: "cooperative-graves",
    seeded: COOPERATIVE_NUMBER,
    search: { number: COOPERATIVE_NUMBER },
    action: "Neues Genossenschaftsgrab",
    secondary: {
      Grabstätte: "lg",
      Friedhof: "lg",
      "Feld-Nr.": "lg",
      Beginn: "xl",
      Ende: "xl",
      Empfänger: "sm",
    },
  },
];

// The rows only have to exist — these assertions are about layout, not about
// the data — but their numbers are unique per run so that seeding never
// collides with rows an earlier attempt left in a stack that kept its data.
// Every test that needs them calls `seed` itself, so it runs alone or in any
// order; a worker only keeps from seeding them twice.
let seeding: Promise<{ invoiceRootId: number; quoteRootId: number }> | undefined;

/** Seeds this worker's rows once, and answers its invoice's and quote's root ids. */
function seed(page: Page): Promise<{ invoiceRootId: number; quoteRootId: number }> {
  seeding ??= seedRows(page);
  return seeding;
}

async function seedRows(page: Page): Promise<{ invoiceRootId: number; quoteRootId: number }> {
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
  await seedGrave(page, {
    graveNumber: COOPERATIVE_NUMBER,
    items: [],
    recipientIds: [recipient.Id],
    isCooperative: true,
    endDate: null,
  });
  const invoice = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 12.5 }],
  });
  const quote = await seedQuote(page, {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 12.5 }],
  });
  return { invoiceRootId: invoice.RootId, quoteRootId: quote.id };
}

// Layout bugs these specs found in the frontend, left for a session of their
// own. Each test that runs into one is marked with `test.fail`, so the
// suite stays green while the bug is there and turns red once it is fixed —
// which is the reminder to drop the mark.
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
    await seed(page);
    await page.goto(`/${screen.route}?${new URLSearchParams(screen.search)}`);
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

test("inactive-graves: its four panels fit the viewport", async ({ page }) => {
  // a long number, as the list pages' rows have: it must wrap inside its card
  const number = `L-${RUN}-BEENDET-SCHINELLER-BAECKE-NORD-ABSCHNITT`;
  const year = new Date().getFullYear();
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${number}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 12.5,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Layout ${RUN} Inaktiv`,
    Address: "Weg 1",
  });
  for (const isCooperative of [false, true]) {
    await seedGrave(page, {
      graveNumber: `${number}${isCooperative ? "-G" : ""}`,
      items: isCooperative ? [] : [{ itemId: item.Id }],
      recipientIds: isCooperative ? [] : [recipient.Id],
      isCooperative,
      startDate: `${year - 2}-01`,
      endDate: `${year - 1}-12`,
    });
  }
  await page.goto("/inactive-graves");
  for (const title of [
    "Beendete Gräber",
    "Gelöschte Gräber",
    "Beendete Genossenschaftsgräber",
    "Gelöschte Genossenschaftsgräber",
  ]) {
    await expect(page.getByRole("heading", { level: 2, name: title, exact: true })).toBeVisible();
    await expect(page.getByRole("textbox", { name: `${title} durchsuchen` })).toBeVisible();
  }
  const link = page.getByRole("link", { name: number, exact: true });
  await expect(link).toBeVisible();
  await expectNoSidewaysScrolling(page);

  // the number stays inside the viewport and the secondary columns drop out
  const box = (await link.boundingBox())!;
  expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
  const site = page.getByRole("columnheader", { name: "Grabstätte", exact: true }).first();
  if (page.viewportSize()!.width < BREAKPOINTS.lg) {
    await expect(site).toBeHidden();
  } else {
    await expect(site).toBeVisible();
  }
});

test("recipients: the filter sheet", async ({ page }) => {
  await seed(page);
  await page.goto("/recipients");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  // below the table's full width the filter row is hidden, the sheet stands in
  await expect(page.getByRole("textbox", { name: "Name durchsuchen" })).toBeHidden();
  const button = page.getByRole("button", { name: /^Filter( \d+)?$/ });
  await button.click();
  await waitForOverlayOpen(page);

  const sheet = page.locator(".p-drawer");
  const name = sheet.getByRole("textbox", { name: "Name durchsuchen" });
  await name.fill(RECIPIENT_NAME);
  await expect(page.getByText(`Name: ${RECIPIENT_NAME}`)).toBeVisible();
  await expect(button).toContainText("1");

  // a long term wraps instead of pushing the page sideways
  await name.fill("x".repeat(60));
  await expect(page.getByText(`Name: ${"x".repeat(60)}`)).toBeVisible();
  await expectNoSidewaysScrolling(page);

  await page.keyboard.press("Escape");
  await expect(sheet).toHaveCount(0);
  await expectNoSidewaysScrolling(page);

  await page.getByRole("button", { name: "Filter Name entfernen" }).click();
  await expect(page.getByText("Name: ", { exact: false })).toHaveCount(0);
  await expect(button).not.toContainText("1");
});

test("graves: the filter sheet holds the selects as well", async ({ page }) => {
  await seed(page);
  await page.goto("/graves");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  // the filter row comes only with every column; below that the sheet stands in
  await expect(page.getByRole("textbox", { name: "Grabnummer durchsuchen" })).toBeHidden();
  const button = page.getByRole("button", { name: /^Filter( \d+)?$/ });
  await button.click();
  await waitForOverlayOpen(page);

  const sheet = page.locator(".p-drawer");
  await expect(sheet.getByRole("textbox", { name: "Empfänger durchsuchen" })).toBeVisible();
  for (const header of ["Friedhof", "Status", "Zahlung"]) {
    await expect(sheet.getByRole("combobox", { name: `${header} filtern` })).toBeVisible();
  }
  await pickOption(page, sheet.getByRole("combobox", { name: "Status filtern" }), "offen");
  await expect(page.getByText("Status: offen")).toBeVisible();
  await expect(button).toContainText("1");
  await expectNoSidewaysScrolling(page);

  await page.keyboard.press("Escape");
  await expect(sheet).toHaveCount(0);
  await expectNoSidewaysScrolling(page);
});

test("cooperative-graves: the filter sheet opens", async ({ page }) => {
  await seed(page);
  await page.goto("/cooperative-graves");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  const button = page.getByRole("button", { name: /^Filter( \d+)?$/ });
  await button.click();
  await waitForOverlayOpen(page);

  const sheet = page.locator(".p-drawer");
  await expect(sheet.getByRole("textbox", { name: "Empfänger durchsuchen" })).toBeVisible();
  await expect(sheet.getByRole("combobox", { name: "Friedhof filtern" })).toBeVisible();
  await sheet.getByRole("textbox", { name: "Grabnummer durchsuchen" }).fill(COOPERATIVE_NUMBER);
  await expect(page.getByText(`Grabnummer: ${COOPERATIVE_NUMBER}`)).toBeVisible();
  await expectNoSidewaysScrolling(page);

  await page.keyboard.press("Escape");
  await expect(sheet).toHaveCount(0);
  await expectNoSidewaysScrolling(page);
});

test("graves: the recipients popover stays on the screen", async ({ page }) => {
  const marker = `LP-${unique()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  // long names, which the popover has to wrap rather than run off the screen
  const recipientIds: number[] = [];
  for (const n of [1, 2, 3, 4]) {
    const recipient = await createRow(page, "/odata/Recipients", {
      Name: `Empfängerin ${n} ${marker} Hohenberg-Schwarzenstein-Waldenburg`,
      Address: "Musterstraße 1",
    });
    recipientIds.push(recipient.Id);
  }
  // due in the first half of 2026 alone, which no test generates
  await seedGrave(page, {
    graveNumber: marker,
    startDate: "2026-01",
    endDate: "2026-06",
    items: [{ itemId: item.Id }],
    recipientIds,
  });

  await page.goto(`/graves?period=2026-1&number=${marker}`);
  const button = page
    .locator("tbody tr")
    .filter({ hasText: marker })
    .getByRole("button", { name: "4 Empfänger" });
  const popover = await openPopover(page, button);
  await expect(popover.getByRole("listitem")).toHaveCount(4);

  const viewport = page.viewportSize()!;
  const box = (await popover.boundingBox())!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1);
  await expectNoSidewaysScrolling(page);
});

/**
 * A list's row actions: buttons from the widest screens, a menu below them. The
 * menu closes when anything behind it scrolls, so nothing may need to: its
 * button is brought to the middle of the screen first, where the menu has room
 * below it, and fully into the table's view, should the table be wider than its
 * card. The menu has to fit the screen, and its first item has to lead where
 * its button does.
 */
async function expectRowActionsReachable(
  page: Page,
  list: { route: string; actions: string[]; detailsUrl: RegExp },
): Promise<void> {
  await seed(page);
  // a page holds ten rows, and other workers' rows would push this run's off it
  await page.goto(`/${list.route}?${new URLSearchParams({ recipient: RECIPIENT_NAME })}`);
  const row = page.getByRole("row").filter({ hasText: RECIPIENT_NAME }).first();
  await expect(row).toBeVisible();

  if (page.viewportSize()!.width >= BREAKPOINTS.xl) {
    for (const name of list.actions) {
      await expect(row.getByRole("button", { name: rowAction(name) })).toBeVisible();
    }
    return;
  }

  const more = row.getByRole("button", { name: rowAction("Weitere Aktionen") });
  await more.evaluate((element) => element.scrollIntoView({ block: "center", inline: "end" }));
  await more.click();
  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  await expect(page.locator(".p-menu")).not.toHaveClass(/p-connected-overlay-enter-active/);
  for (const name of list.actions) {
    await expect(menu.getByRole("menuitem", { name, exact: true })).toBeVisible();
  }
  const box = (await menu.boundingBox())!;
  const viewport = page.viewportSize()!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1);
  expect(box.y).toBeGreaterThanOrEqual(0);
  expect(box.y + box.height).toBeLessThanOrEqual(viewport.height + 1);

  // Under load the open menu has been seen to close under the tap that was
  // meant for it, so a tap that did not lead anywhere is made again, from a
  // freshly opened menu.
  const step = { timeout: 3000 };
  await expect(async () => {
    if (!list.detailsUrl.test(page.url())) {
      if (!(await menu.isVisible())) {
        await more.click(step);
      }
      await expect(page.locator(".p-menu")).not.toHaveClass(
        /p-connected-overlay-enter-active/,
        step,
      );
      await menu.getByRole("menuitem", { name: "Details", exact: true }).click(step);
    }
    await expect(page).toHaveURL(list.detailsUrl, step);
  }).toPass({ timeout: 30_000 });
}

test("invoices: every row action stays reachable", async ({ page }) => {
  await expectRowActionsReachable(page, {
    route: "invoices",
    actions: [
      "Details",
      "Rechnung herunterladen",
      "Kopie herunterladen",
      "Bearbeiten",
      "Als bezahlt markieren",
    ],
    detailsUrl: /\/invoices\/\d+$/,
  });
});

test("quotes: every row action stays reachable", async ({ page }) => {
  await expectRowActionsReachable(page, {
    route: "quotes",
    actions: [
      "Details",
      "Angebot herunterladen",
      "Bearbeiten",
      "In Rechnung umwandeln",
      "Ablehnen",
    ],
    detailsUrl: /\/quotes\/\d+$/,
  });
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
  await seed(page);
  await page.goto("/graves");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  await page.getByRole("button", { name: "Grab anlegen", exact: true }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);

  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, ITEM_NUMBER);

  // The recipient list opens within the screen. Its options do not wrap, so a
  // long name widened it past a phone's edge, which zoomed the page out, slid
  // the editor under the list and left the option behind the editor's text.
  await page.locator("#graveRecipient").click();
  const recipientList = page.locator(".p-select-overlay");
  // searched for: the list holds the newest twenty, and other tests add recipients
  await recipientList.locator("input.p-select-filter").fill(RECIPIENT_NAME);
  await expect(recipientList.getByRole("option", { name: RECIPIENT_NAME })).toBeVisible();
  await expectNoSidewaysScrolling(page);
  const listBox = (await recipientList.boundingBox())!;
  expect(listBox.x + listBox.width, "the recipient list runs off the screen").toBeLessThanOrEqual(
    page.viewportSize()!.width + 1,
  );
  // closed by the field that opened it, as a click would
  await page.locator("#graveRecipient").click();
  await expect(recipientList).toHaveCount(0);

  await pickRecipient(page, "graveRecipient", RECIPIENT_NAME);
  await editor.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  const added = editor.getByRole("listitem").filter({ hasText: RECIPIENT_NAME });
  await expect(added).toBeVisible();
  // the billing preview's cards are on screen when sideways scrolling is checked
  await expect(editor.getByRole("group", { name: "Volles H1" })).toBeVisible();

  await expectFieldsGetTheWidth(page);
  await expectNoSidewaysScrolling(page);

  // the recipient list gets the editor's width too
  expect(await widthOf(added)).toBeGreaterThan((await widthOf(editor)) * 0.85);

  await discardEditor(page);
});

// A test of its own, so the mark for its known bug cannot also swallow a
// regression in the test above: `test.fail` is satisfied by whatever fails
// first, and the picker's assertion used to be the last of several.
test("graves: the editor gives the recipient picker its width", async ({ page }) => {
  test.fail(onPhone(), PICKER_SQUEEZED);
  await seed(page);
  await page.goto("/graves");
  await expect(page.locator("tbody tr").first()).toBeVisible();

  await page.getByRole("button", { name: "Grab anlegen", exact: true }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await pickRecipient(page, "graveRecipient", RECIPIENT_NAME);

  await expectPickerGetsTheWidth(page, "graveRecipient");

  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);
});

test("graves: the rounding corrections dialog fits the viewport", async ({ page }) => {
  // 47,80 at 19 % does not divide across three recipients, so saving the grave
  // asks for a confirmation first
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const itemNumber = `L${RUN}-KORREKTUR`;
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Grabstein reinigen mit einer längeren Beschreibung",
    VatRate: 19,
    DefaultPrice: 47.8,
  });
  const names = ["Anna", "Bert", "Cilly"].map((name) => `${name} ${RUN}`);
  for (const name of names) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" });
  }
  await ensureCemetery(page);

  await page.goto("/graves");
  await page.getByRole("button", { name: "Grab anlegen", exact: true }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await editor.getByLabel("Grabnummer").fill(`LK-${RUN}`);
  await editor.getByLabel("Feld-Nr.").fill("27");
  await editor.getByLabel("Grabstätte").fill("Schineller Bäcke");
  await pickOption(page, page.locator("#graveCemetery"), SEED_CEMETERY, { filter: SEED_CEMETERY });
  await pickOption(page, page.locator("#gravePosition"), "Doppelgrab außen");
  await fillDate(page, editor.getByLabel("Beginn"), "01/2026");
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  for (const name of names) {
    await pickRecipient(page, "graveRecipient", name);
    await editor.getByRole("button", { name: "Hinzufügen", exact: true }).click();
    await expect(editor.getByRole("listitem").filter({ hasText: name })).toBeVisible();
  }

  await editor.getByRole("button", { name: "Speichern" }).click();
  const corrections = dialog(page, "Preise werden angepasst");
  await expect(corrections).toBeVisible();
  await expect(corrections).toContainText("47,82 €");
  await waitForOverlayOpen(page);
  await expectNoSidewaysScrolling(page);

  await corrections.getByRole("button", { name: "Abbrechen" }).click();
  await expect(corrections).toHaveCount(0);
  await discardEditor(page);
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

  await discardEditor(page);
});

// The grave-invoice detail page is not swept here: reaching it needs a
// generated grave invoice, and generation belongs to graves.spec.ts alone. It
// shares its revision panels with the invoice detail page, covered below.
test("the invoice detail page fits the viewport", async ({ page }) => {
  const { invoiceRootId } = await seed(page);

  await page.goto(`/invoices/${invoiceRootId}`);
  await expect(page.locator(".p-accordionpanel").first()).toContainText(ITEM_NUMBER);
  await expectHeaderActionsFit(page);
  await expectNoSidewaysScrolling(page);
});

test("the quote detail page fits the viewport", async ({ page }) => {
  const { quoteRootId } = await seed(page);

  await page.goto(`/quotes/${quoteRootId}`);
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
  await expect(page.getByRole("cell", { name: ITEM_NUMBER }).first()).toBeVisible();
  // The billing preview arrives after the rest of the page, and its tables are
  // the widest part of it. Measured before they are there, the page fitted on
  // a quiet machine and overflowed on a busy one, where the preview had
  // already come in.
  await expect(page.getByRole("group", { name: /^Volles H[12]$/ }).first()).toBeVisible();
  await expectHeaderActionsFit(page);
  await expectNoSidewaysScrolling(page);
});

test("settings: the form, the VAT rate row and the cemeteries fit the viewport", async ({
  page,
}) => {
  await seed(page);
  await page.goto("/settings");

  await expect(page.getByLabel("Stempelwort")).toBeVisible();
  await expect(page.getByRole("button", { name: "MwSt.-Satz hinzufügen" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Friedhof hinzufügen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("import/export: the page fits the viewport", async ({ page }) => {
  await page.goto("/import-export");

  await expect(page.getByRole("button", { name: "Vorlage herunterladen" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Importieren", exact: true })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});

test("lists: both cards fit the viewport", async ({ page }) => {
  await page.goto("/lists");

  await expect(
    page.getByRole("button", { name: "Pflegeliste als PDF herunterladen" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Pflanzliste als PDF herunterladen" }),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "Mit allen Leistungen" })).toBeVisible();
  await expectNoSidewaysScrolling(page);
});
