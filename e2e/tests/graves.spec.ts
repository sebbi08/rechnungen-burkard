import { expect, type Locator, type Page, test } from "@playwright/test";
import { acquireStackLock } from "../lock";
import {
  createRow,
  ensureCemetery,
  ensureRow,
  SEED_CEMETERY,
  seedGrave as saveGrave,
  type SeedGraveLine,
} from "../seed";
import { GENERATION_LOCK_PATH } from "../stack";
import { unique } from "../unique";
import {
  confirmation,
  dialog,
  drawer,
  fillDate,
  fillNumber,
  fillYear,
  filterColumn,
  openPopover,
  pickItem,
  pickOption,
  pickRecipient,
  rowAction,
  saveEditor,
  searchColumn,
  toasts,
  waitForOverlayOpen,
} from "../vue";

// Unique per worker, and each test puts its own prefix in front. `npm test`
// starts with an empty database, but CI runs the suite against an image it
// started once, and a rerun there must not collide with the rows the first
// attempt left or make assertions vacuous.
const RUN = unique();

// The overview and the generate dialog both open on the half-year today falls
// in, so a generation started without further input lands here. Derived from
// the same clock the app reads, not hardcoded, or every January would turn
// this file red.
const CURRENT_YEAR = new Date().getFullYear();
const CURRENT_HALF = new Date().getMonth() < 6 ? 1 : 2;

// The half-year after today's. Only the grave detail page test generates it.
const NEXT_YEAR = CURRENT_HALF === 1 ? CURRENT_YEAR : CURRENT_YEAR + 1;
const NEXT_HALF = CURRENT_HALF === 1 ? 2 : 1;

/**
 * A grave's term from the first month of one half-year to the last month of
 * another, as SaveGrave takes it.
 *
 * Every test runs at the same time as every other, and a generation invoices
 * every grave due in its half-year, whoever seeded it. So a test that counts
 * its grave's invoices keeps the grave's term to the half-years it generates
 * itself: no other test's generation then reaches that grave, and its counts
 * stay exact. It does not keep other graves out of this test's generation —
 * a grave seeded with seed.ts's default term (2026-01 to 2040-12) is due in
 * every one of these half-years and gets invoiced too, which nothing here
 * counts.
 */
function term(
  year: number,
  half: number,
  until: { year: number; half: number } = { year, half },
): { startDate: string; endDate: string } {
  return {
    startDate: `${year}-${half === 1 ? "01" : "07"}`,
    endDate: `${until.year}-${until.half === 1 ? "06" : "12"}`,
  };
}

// A generation renders the documents of every grave not yet invoiced for its
// half-year, and a stack that kept the graves of earlier runs has more of them
// each time: generateFor waits up to 90s for its job, and before that for any
// other test's generation to finish, so a test has to be allowed longer.
test.describe.configure({ timeout: 240_000 });

// The overview shows its filter row, through which these tests search it, only
// from Tailwind's 2xl on; narrower, the filter sheet stands in (layout.spec.ts).
// The one test about narrower desktops sets its own widths.
test.use({ viewport: { width: 1536, height: 720 } });

/** The grave editor's recipient picker. */
const RECIPIENT = "graveRecipient";

/** A card of a page, by its title. */
function card(page: Page, title: string): Locator {
  return page
    .locator(".p-card")
    .filter({ has: page.locator(".p-card-title", { hasText: new RegExp(`^${title}$`) }) });
}

/** The rows of the grave detail page's invoice list. */
function graveInvoiceRows(page: Page): Locator {
  return card(page, "Rechnungen").locator("tbody tr");
}

/** The blocking overlay a long task shows (constitution VIII). */
function blocking(page: Page): Locator {
  return page.locator("[role='alertdialog'][aria-busy='true']");
}

/**
 * Seeds `count` graves due in the current half-year only, so a generation of
 * another half-year does not have to render them, and answers the first one's
 * number. The last one is billed to two recipients, so the duplicate check
 * after the generation always covers a grave with more than one invoice.
 */
async function seedGraveGenerationData(page: Page, count: number): Promise<string> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `8001-${RUN}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Anna Beispiel ${RUN}`,
    Address: "Hauptstraße 1",
  });
  const secondRecipient = await createRow(page, "/odata/Recipients", {
    Name: `Clara Beispiel ${RUN}`,
    Address: "Seitenweg 2",
  });

  for (let i = 1; i <= count; i++) {
    await saveGrave(page, {
      graveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
      ...term(CURRENT_YEAR, CURRENT_HALF),
      items: [{ itemId: item.Id, count: 1 }],
      recipientIds: i === count ? [recipient.Id, secondRecipient.Id] : [recipient.Id],
    });
  }
  return `G-${RUN}-001`;
}

// The backend runs one generation at a time and refuses a second one while the
// first is running (JobAlreadyRunning). The tests of this file run in parallel
// workers, so the ones that generate take turns: through a lock file, the same
// kind of machine-wide lock the stack itself is guarded by (../lock.ts).

/** Runs `work` once no other test is generating, and keeps the others waiting until it is done. */
async function withGenerationLock<T>(work: () => Promise<T>): Promise<T> {
  // a step of its own, so a test that times out waiting says what it waited
  // for, and on which file: one a dead process left can be removed by hand
  const step = `wait until no other test is generating (lock ${GENERATION_LOCK_PATH})`;
  const lock = await test.step(step, async () => {
    for (;;) {
      try {
        return acquireStackLock(GENERATION_LOCK_PATH, { waitMs: 0 });
      } catch (error) {
        // a file system error is not contention
        if ((error as NodeJS.ErrnoException).code) {
          throw error;
        }
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    }
  });
  try {
    return await work();
  } finally {
    lock.release();
  }
}

interface PeriodInvoice {
  GraveId: number;
  RecipientId: number;
}

/** Every grave invoice of the current half-year, paged past the service's
 *  200-row limit so the duplicate check sees all of them. */
async function periodInvoices(page: Page): Promise<PeriodInvoice[]> {
  const pageSize = 200;
  const all: PeriodInvoice[] = [];
  for (let skip = 0; ; skip += pageSize) {
    const response = await page.request.get(
      `/odata/Invoices?$filter=PeriodYear eq ${CURRENT_YEAR} and PeriodHalf eq ${CURRENT_HALF}` +
        `&$select=GraveId,RecipientId&$orderby=Id&$top=${pageSize}&$skip=${skip}`,
    );
    const batch = (await response.json()).value as PeriodInvoice[];
    all.push(...batch);
    if (batch.length < pageSize) {
      return all;
    }
  }
}

/** Opens the grave editor for a new grave and fills in its number, site,
 *  cemetery and term. The cemetery is created first if it is not there yet. */
async function startNewGrave(
  page: Page,
  graveNumber: string,
  { end = "12/2040", cemetery = SEED_CEMETERY }: { end?: string | null; cemetery?: string } = {},
): Promise<Locator> {
  await ensureCemetery(page, cemetery);
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await editor.getByLabel("Grabnummer").fill(graveNumber);
  await editor.getByLabel("Feld-Nr.").fill("27");
  await editor.getByLabel("Grabstätte").fill(`Schineller Bäcke ${graveNumber}`);
  // searched for: other specs add cemeteries of their own
  await pickOption(page, page.locator("#graveCemetery"), cemetery, { filter: cemetery });
  await pickOption(page, page.locator("#gravePosition"), "Doppelgrab außen");
  await fillDate(page, editor.getByLabel("Beginn"), "01/2026");
  if (end) {
    await fillDate(page, editor.getByLabel("Ende", { exact: true }), end);
  }
  return editor;
}

/** Adds a recipient to the grave being created. */
async function addRecipient(page: Page, editor: Locator, name: string): Promise<void> {
  await pickRecipient(page, RECIPIENT, name);
  // "Hinzufügen" alone also matches "Zeile hinzufügen"
  await editor.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  await expect(editor.getByRole("listitem").filter({ hasText: name })).toBeVisible();
}

/** A cooperative grave with one all-year item and no recipient, the way a save
 *  without any stores it; `startDate` and `endDate` as in `term`. */
async function seedCooperativeGrave(
  page: Page,
  marker: string,
  { startDate, endDate }: { startDate: string; endDate: string | null },
): Promise<number> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  return saveGrave(page, {
    graveNumber: marker,
    startDate,
    endDate,
    items: [{ itemId: item.Id, count: 1 }],
    recipientIds: [],
    isCooperative: true,
  });
}

/** Opens the editor of a grave from its detail page. */
async function editGraveOnItsPage(page: Page, graveId: number): Promise<Locator> {
  await page.goto(`/graves/${graveId}`);
  await page.getByRole("button", { name: "Grab bearbeiten" }).click();
  await waitForOverlayOpen(page);
  return drawer(page);
}

test("a recipient added to a grave is no longer offered for it", async ({ page }) => {
  const marker = `GX-${unique()}`;
  const [first, second] = [`Erna ${marker}`, `Fritz ${marker}`];
  // Searched by an address the two share and their names do not hold: leaving
  // a recipient out has to hold for every field the search looks in.
  const street = `Sackgasse ${unique()}`;
  for (const name of [first, second]) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: street });
  }
  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  await addRecipient(page, editor, first);

  // the field is empty again, rather than on the recipient just added or the next one
  const picker = page.locator(`#${RECIPIENT}`);
  await expect(picker).toHaveText("Empfänger wählen");
  await picker.click();
  const overlay = page.locator(".p-select-overlay");
  await overlay.locator("input.p-select-filter").fill(street);
  await expect(overlay.getByRole("option", { name: second })).toHaveCount(1);
  await expect(overlay.getByRole("option", { name: first })).toHaveCount(0);
});

test("a cooperative grave is saved without recipients and items", async ({ page }) => {
  const marker = `CG-${unique()}`;
  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  await editor.getByLabel("Genossenschaftsgrab").check();
  await saveEditor(editor);

  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${marker}'&$select=Id`)
  ).json();
  expect(graves.value).toHaveLength(1);

  // the detail page names it
  await page.goto(`/graves/${graves.value[0].Id}`);
  await expect(page.locator(".p-tag", { hasText: "Genossenschaftsgrab" })).toBeVisible();

  // saved as a cooperative grave: reopened, the box is ticked and there is nothing to bill
  const reopened = await editGraveOnItsPage(page, graves.value[0].Id);
  await expect(reopened.getByLabel("Genossenschaftsgrab")).toBeChecked();
  await expect(reopened.getByRole("listitem")).toHaveCount(0);
  await reopened.getByRole("button", { name: "Abbrechen" }).click();
  await expect(reopened).toHaveCount(0);
});

test("unticking a cooperative grave asks for a recipient", async ({ page }) => {
  const marker = `CU-${unique()}`;
  const graveId = await seedCooperativeGrave(page, marker, { startDate: "2026-01", endDate: null });

  const editor = await editGraveOnItsPage(page, graveId);
  const box = editor.getByLabel("Genossenschaftsgrab");
  await expect(box).toBeChecked();
  await box.uncheck();
  await editor.getByRole("button", { name: "Speichern" }).click();

  await expect(
    toasts(page).filter({ hasText: "Das Grab braucht mindestens einen Empfänger." }),
  ).toBeVisible();
  await expect(editor).toBeVisible();

  // a recipient makes it an ordinary grave, billed from now on
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Gen ${marker}`,
    Address: "Musterweg 1",
  });
  await addRecipient(page, editor, recipient.Name);
  await saveEditor(editor);
  const stored = await (
    await page.request.get(`/odata/Graves(${graveId})?$select=IsCooperative`)
  ).json();
  expect(stored.IsCooperative).toBe(false);
});

test("create a grave with item, price and recipient in one save (Story 5)", async ({ page }) => {
  const itemNumber = `8002-${RUN}`;
  const recipientName = `Bert Beispiel ${RUN}`;
  const graveNumber = `X-${RUN}`;

  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Nebenweg 2" });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber);

  // the grave does not exist yet: item and recipient are collected in the editor
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  // selecting the item pre-fills its default price, as the invoice editor does;
  // typing another one overrides it for this grave
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("50,00");
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 1" }), "2");
  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "55");

  await addRecipient(page, editor, recipientName);

  // nothing has been persisted so far — one save writes the grave and both links
  await saveEditor(editor);
  await expect(page.getByRole("table")).toContainText(graveNumber);

  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id,StartDate,EndDate,FieldNumber,SiteName,Position`,
    )
  ).json();
  const grave = graves.value[0];
  // the picked months, stored as their first day -- not the month before
  expect(grave.StartDate).toBe("2026-01-01");
  expect(grave.EndDate).toBe("2040-12-01");
  expect(grave.FieldNumber).toBe("27");
  expect(grave.SiteName).toBe(`Schineller Bäcke ${graveNumber}`);
  expect(grave.Position).toBe("DoubleOutside");
  const links = await (
    await page.request.get(`/odata/GraveItems?$filter=GraveId eq ${grave.Id}`)
  ).json();
  expect(links.value).toHaveLength(1);
  expect(Number(links.value[0].Count)).toBe(2);
  expect(Number(links.value[0].UnitPrice)).toBe(55);
  const recipientLinks = await (
    await page.request.get(`/odata/GraveRecipients?$filter=GraveId eq ${grave.Id}`)
  ).json();
  expect(recipientLinks.value).toHaveLength(1);

  // Reopening the grave shows the saved count and price again. The editor was
  // just open in create mode — no warnings, recipients editable — and this is
  // the same page reopening it in edit mode, so it proves the editor follows
  // the grave it is given rather than keeping the state of the first open.
  await page
    .getByRole("row")
    .filter({ hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);
  await expect(editor.getByLabel("Grabnummer")).toHaveValue(graveNumber);
  await expect(editor.getByLabel("Beginn")).toHaveValue("01/2026");
  await expect(editor.getByLabel("Ende", { exact: true })).toHaveValue("12/2040");
  await expect(editor.getByLabel("Feld-Nr.")).toHaveValue("27");
  await expect(editor.getByRole("spinbutton", { name: "Anzahl 1" })).toHaveValue("2");
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("55,00");
  await expect(editor).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(editor).toContainText("Die Empfänger eines Grabs");
  await expect(editor.getByRole("listitem").filter({ hasText: recipientName })).toBeVisible();
  await expect(editor.getByRole("button", { name: "Hinzufügen", exact: true })).toHaveCount(0);
  await expect(editor.getByRole("button", { name: "Löschen", exact: true })).toHaveCount(0);
  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);
});

test("an item without a default price needs an explicit price (Story 6)", async ({ page }) => {
  const noPriceItemNumber = `NP-${RUN}`;
  const graveNumber = `NPG-${RUN}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: noPriceItemNumber,
    Description: "Ohne Preis",
    VatRate: 7,
    DefaultPrice: null,
  });
  // a grave needs a recipient before it can be saved at all, so the refusal
  // below is about the price alone
  const recipientName = `Preis Beispiel ${RUN}`;
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Preisweg 1" });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, noPriceItemNumber);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("");
  await addRecipient(page, editor, recipientName);

  // no price anywhere: saving is refused with the German "missing price" message
  await editor.getByRole("button", { name: "Speichern" }).click();
  await expect(
    toasts(page).filter({ hasText: "Zu dieser Leistung fehlt ein Preis." }),
  ).toBeVisible();
  await expect(editor).toBeVisible();
  await expect(page.getByRole("table")).not.toContainText(graveNumber);

  // entering the price in the editor makes the same grave saveable
  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "40");
  await saveEditor(editor);
  await expect(page.getByRole("table")).toContainText(graveNumber);

  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`)
  ).json();
  const links = await (
    await page.request.get(`/odata/GraveItems?$filter=GraveId eq ${graves.value[0].Id}`)
  ).json();
  expect(Number(links.value[0].UnitPrice)).toBe(40);
});

test("editing a grave warns about future invoices and freezes its recipients", async ({ page }) => {
  const marker = unique();
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `7100-${marker}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Fest ${marker}`,
    Address: "Festweg 1",
  });
  await saveGrave(page, {
    graveNumber: `F-${marker}`,
    items: [{ itemId: item.Id, count: 1 }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/graves");
  await page
    .getByRole("row")
    .filter({ hasText: `F-${marker}` })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);

  const editor = drawer(page);
  await expect(editor).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(editor).toContainText("Die Empfänger eines Grabs");
  // the recipient stays visible but cannot be removed or added to; the line
  // editor has its own "Zeile löschen" and "Zeile hinzufügen", hence exact names
  await expect(editor.getByRole("listitem").filter({ hasText: `Fest ${marker}` })).toBeVisible();
  await expect(editor.getByRole("button", { name: "Hinzufügen", exact: true })).toHaveCount(0);
  await expect(editor.getByRole("button", { name: "Löschen", exact: true })).toHaveCount(0);
  await expect(editor.locator(`#${RECIPIENT}`)).toHaveCount(0);
});

/**
 * Generates the grave invoices of one half-year and waits for the job. Each
 * caller other than the current half-year's tests uses a period no other test
 * generates, and keeps its grave's term to it, so a run never collides with
 * another test's expectations.
 */
async function generateFor(page: Page, year: number, half: number): Promise<void> {
  await withGenerationLock(async () => {
    const response = await page.request.post("/odata/GenerateGraveInvoices", {
      data: { year, half },
    });
    expect(response.status()).toBe(202);
    const job = await response.json();
    await expect
      .poll(async () => (await (await page.request.get(`/odata/Jobs(${job.Id})`)).json()).Status, {
        timeout: 90000,
      })
      .toBe("Succeeded");
  });
}

/** One item line `seedGrave` links to the grave. */
interface SeedGraveItem {
  description?: string;
  vatRate?: number;
  price?: number;
  priceIncludesVat?: boolean;
  count?: number;
}

/** A grave with the given items and recipients, unique per run. Defaults to
 *  one 100.00 net item, count 1, at 7 %, and to a term of the current
 *  half-year alone (see `term`). */
async function seedGrave(
  page: Page,
  marker: string,
  recipientNames: string[],
  items: SeedGraveItem[] = [{}],
  { startDate, endDate } = term(CURRENT_YEAR, CURRENT_HALF),
): Promise<{ graveId: number; recipientNames: string[] }> {
  // each line bills its item's default price: the link carries no price of
  // its own, only the count and whether that default is a gross one
  const lines: SeedGraveLine[] = [];
  for (const [index, spec] of items.entries()) {
    const vatRate = spec.vatRate ?? 7;
    await ensureRow(page, "/odata/VatRates", { Rate: vatRate });
    const item = await createRow(page, "/odata/InvoiceItems", {
      Number: items.length > 1 ? `${marker}-item${index + 1}` : `${marker}-item`,
      Description: spec.description ?? "Grabpflege",
      VatRate: vatRate,
      DefaultPrice: spec.price ?? 100,
    });
    lines.push({
      itemId: item.Id,
      count: spec.count ?? 1,
      priceIncludesVat: spec.priceIncludesVat ?? false,
    });
  }
  const recipientIds: number[] = [];
  for (const name of recipientNames) {
    const recipient = await createRow(page, "/odata/Recipients", {
      Name: name,
      Address: "Musterweg 1",
    });
    recipientIds.push(recipient.Id);
  }
  const graveId = await saveGrave(page, {
    graveNumber: marker,
    startDate,
    endDate,
    items: lines,
    recipientIds,
  });
  return { graveId, recipientNames };
}

test("editing a grave invoice revises every recipient", async ({ page }) => {
  const marker = `GE-${unique()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [{}], term(2038, 2));

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2038, 2);

  await page.goto(`/graves/${graveId}`);
  await expect(graveInvoiceRows(page)).toHaveCount(2);

  await graveInvoiceRows(page)
    .filter({ hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);

  // the editor names the half-year and both recipients it is about to revise
  const editor = drawer(page);
  await expect(editor).toContainText("H2 2038");
  await expect(editor).toContainText(names[0]);
  await expect(editor).toContainText(names[1]);
  // it opens on the grave's price, not on the 50.00 share each recipient pays
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("100,00");

  // A layout check, which would belong to layout.spec.ts — but reaching this
  // editor needs a generated grave invoice, and generation stays in this file.
  // The item's number field has to hold a long number on the desktop this
  // test runs at.
  const numberField = editor.locator(".p-select", {
    has: page.getByRole("combobox", { name: "Nummer 1" }),
  });
  const rem = await page.evaluate(() =>
    parseFloat(getComputedStyle(document.documentElement).fontSize),
  );
  const numberWidth = await numberField.evaluate((el) => el.getBoundingClientRect().width);
  expect(numberWidth).toBeGreaterThan(12 * rem);

  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "200");
  await saveEditor(editor);

  // both recipients moved to R1, each at half of 1 × 200.00 + 7% = 107,00 —
  // asserted per row, not just "at least one", so a revision that reached
  // only one recipient would fail this
  await expect(graveInvoiceRows(page)).toHaveCount(2);
  await expect(graveInvoiceRows(page).filter({ hasText: "R1" })).toHaveCount(2);
  await expect(graveInvoiceRows(page).filter({ hasText: "107,00 €" })).toHaveCount(2);
});

test("a grave invoice's detail page shows its revisions and its siblings", async ({ page }) => {
  const marker = `GI-${unique()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [{}], term(2037, 2));

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2037, 2);

  await page.goto(`/graves/${graveId}`);
  await graveInvoiceRows(page)
    .filter({ hasText: names[0] })
    .getByRole("button", { name: rowAction("Details") })
    .click();

  await expect(page).toHaveURL(new RegExp(`/graves/${graveId}/invoices/\\d+$`));
  await expect(page.locator(".p-accordionheader").first()).toContainText("Original");
  const facts = page.locator("dl").first();
  await expect(facts).toContainText(marker);
  await expect(facts).toContainText("H2 2037");
  await expect(facts).toContainText("50 %");
  await expect(page.getByText(names[0], { exact: true })).toBeVisible();

  // the other recipient of the same half-year is listed, and leads to their
  // own grave-invoice detail page
  const others = card(page, "Weitere Empfänger");
  await others.getByRole("link", { name: names[1] }).click();
  await expect(page.getByText(names[1], { exact: true })).toBeVisible();
  await expect(card(page, "Weitere Empfänger").getByRole("link", { name: names[0] })).toBeVisible();

  // the breadcrumb leads back to the grave, not to the grave list
  await page
    .getByRole("navigation", { name: "Seitenpfad" })
    .getByRole("link", { name: marker })
    .click();
  await expect(page).toHaveURL(new RegExp(`/graves/${graveId}$`));
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();
});

/** The H1/H2 toggle of a form: the button of this half, pressed or not. */
function halfButton(scope: Page | Locator, half: number): Locator {
  return scope.getByRole("group", { name: "Halbjahr" }).getByRole("button", { name: `H${half}` });
}

test("the overview shows a hundred graves a page, of those the search leaves", async ({ page }) => {
  const marker = `GS-${unique()}`;
  // A half-year far ahead that no test generates: a generation renders the
  // documents of every grave due in its half-year, and 101 more of them would
  // slow down whichever test started it.
  const year = CURRENT_YEAR + 9;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Paula ${marker}`,
    Address: "Musterweg 1",
  });
  const numbers = Array.from(
    { length: 101 },
    (_, i) => `${marker}-${String(i + 1).padStart(3, "0")}`,
  );
  for (let start = 0; start < numbers.length; start += 10) {
    await Promise.all(
      numbers.slice(start, start + 10).map((graveNumber) =>
        saveGrave(page, {
          graveNumber,
          ...term(year, 1),
          items: [{ itemId: item.Id }],
          recipientIds: [recipient.Id],
        }),
      ),
    );
  }

  await page.goto("/graves");
  await fillYear(page, page.locator("#gravesYear"), String(year));
  await halfButton(page, 1).click();
  await expect(halfButton(page, 1)).toHaveAttribute("aria-pressed", "true");

  // the pages are cut from the graves the search leaves, not from all of them
  await searchColumn(page, "Grabnummer", marker);
  const rows = page.getByRole("row").filter({ hasText: marker });
  await expect(rows).toHaveCount(100);
  await expect(page.getByText("1–100 von 101")).toBeVisible();

  // a hundred rows are taller than the window, and the paginator stays in it
  const next = page.getByRole("button", { name: "Nächste Seite" });
  await expect(next).toBeInViewport();
  await next.click();
  await expect(rows).toHaveCount(1);
  await expect(page.getByText("101–101 von 101")).toBeVisible();

  // a narrower search starts on page one again: 001 to 009
  await searchColumn(page, "Grabnummer", `${marker}-00`);
  await expect(rows).toHaveCount(9);
  await expect(page.getByText("1–9 von 9")).toBeVisible();
});

test("overview and generate dialog open on the half-year today falls in", async ({ page }) => {
  await page.goto("/graves");

  await expect(page.locator("#gravesYear")).toHaveValue(String(CURRENT_YEAR));
  await expect(halfButton(page, CURRENT_HALF)).toHaveAttribute("aria-pressed", "true");

  // the two are asserted together on purpose: a dialog defaulting to another
  // half-year still generates successfully, but leaves the status column the
  // overview is showing red, which reads as a failed run
  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  const generate = dialog(page, "Rechnungen erzeugen");
  await expect(generate.locator("#generateYear")).toHaveValue(String(CURRENT_YEAR));
  await expect(halfButton(generate, CURRENT_HALF)).toHaveAttribute("aria-pressed", "true");
  await generate.getByRole("button", { name: "Abbrechen" }).click();
  await expect(generate).toHaveCount(0);
});

test("the generate dialog starts on the half-year the overview shows", async ({ page }) => {
  await page.goto("/graves");
  const [year, half] = CURRENT_HALF === 1 ? [CURRENT_YEAR - 1, 2] : [CURRENT_YEAR, 1];
  if (year !== CURRENT_YEAR) {
    await fillYear(page, page.locator("#gravesYear"), String(year));
  }
  await halfButton(page, half).click();
  await expect(halfButton(page, half)).toHaveAttribute("aria-pressed", "true");

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  const generate = dialog(page, "Rechnungen erzeugen");
  await expect(generate.locator("#generateYear")).toHaveValue(String(year));
  await expect(halfButton(generate, half)).toHaveAttribute("aria-pressed", "true");
  await generate.getByRole("button", { name: "Abbrechen" }).click();
});

test("the generate dialog refuses a half-year older than two half-years back", async ({ page }) => {
  // the current half-year minus two, as the backend counts it
  const index = CURRENT_YEAR * 2 + (CURRENT_HALF - 1) - 2;
  const earliestYear = Math.floor(index / 2);
  const earliestHalf = (index % 2) + 1;

  await page.goto("/graves");
  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  const generate = dialog(page, "Rechnungen erzeugen");
  await expect(generate).toContainText(`Frühestens H${earliestHalf} ${earliestYear}.`);
  const submit = generate.getByRole("button", { name: "Jetzt erzeugen" });
  await expect(submit).toBeEnabled();

  // the calendar starts in the earliest year: typing an earlier one is not taken
  const year = generate.locator("#generateYear");
  await fillYear(page, year, String(earliestYear));
  await year.fill(String(earliestYear - 1));
  await year.press("Escape");
  await expect(page.locator(".p-datepicker-panel")).toHaveCount(0);
  await expect(year).toHaveValue(String(earliestYear));

  // in the earliest year a half before the earliest one cannot be chosen
  await expect(halfButton(generate, 1)).toBeEnabled({ enabled: earliestHalf === 1 });
  await expect(halfButton(generate, 2)).toBeEnabled();
  await expect(submit).toBeEnabled();
});

test("the generate dialog disables its submit button below the earliest half-year", async ({
  page,
}) => {
  const index = CURRENT_YEAR * 2 + (CURRENT_HALF - 1) - 2;
  const earliestYear = Math.floor(index / 2);
  const earliestHalf = (index % 2) + 1;
  // with the earliest half being H1 no half of its year lies below the bound
  test.skip(earliestHalf === 1, "every half of the earliest year is allowed");

  await page.goto("/graves");
  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  const generate = dialog(page, "Rechnungen erzeugen");
  const submit = generate.getByRole("button", { name: "Jetzt erzeugen" });

  // H1 of the current year is allowed; moving the year back below the bound
  // leaves it selected, and the run would be refused
  await halfButton(generate, 1).click();
  await expect(halfButton(generate, 1)).toHaveAttribute("aria-pressed", "true");
  await expect(submit).toBeEnabled();
  await fillYear(page, generate.locator("#generateYear"), String(earliestYear));
  await expect(submit).toBeDisabled();

  // the allowed half enables it again
  await halfButton(generate, 2).click();
  await expect(submit).toBeEnabled();
});

test("the generate dialog opens on today when the overview shows a half-year too old", async ({
  page,
}) => {
  // an early start widens the overview's year range down to it
  const oldYear = CURRENT_YEAR - 3;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `OLD-${RUN}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Alt ${RUN}`,
    Address: "Hauptstraße 1",
  });
  await saveGrave(page, {
    graveNumber: `OLD-${RUN}`,
    startDate: `${oldYear}-01`,
    items: [{ itemId: item.Id, count: 1 }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/graves");
  // emptying the year field leaves it empty while it is edited, and the kept year returns when it closes
  const overviewYear = page.locator("#gravesYear");
  await overviewYear.fill("");
  await expect(overviewYear).toHaveValue("");
  await overviewYear.press("Escape");
  await expect(overviewYear).toHaveValue(String(CURRENT_YEAR));
  await fillYear(page, overviewYear, String(oldYear));
  await halfButton(page, 1).click();
  await expect(halfButton(page, 1)).toHaveAttribute("aria-pressed", "true");

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  const generate = dialog(page, "Rechnungen erzeugen");
  await expect(generate.locator("#generateYear")).toHaveValue(String(CURRENT_YEAR));
  await expect(halfButton(generate, CURRENT_HALF)).toHaveAttribute("aria-pressed", "true");
  const submit = generate.getByRole("button", { name: "Jetzt erzeugen" });
  await expect(submit).toBeEnabled();

  // without a year there is nothing to generate
  await generate.locator("#generateYear").fill("");
  await expect(submit).toBeDisabled();
  await generate.locator("#generateYear").press("Escape");
});

test("a grave with three recipients bills every one of them the identical share (2035/2)", async ({
  page,
}) => {
  const marker = `GT-${unique()}`;
  const names = [`Erna ${marker}`, `Otto ${marker}`, `Rosa ${marker}`];
  const { graveId } = await seedGrave(
    page,
    marker,
    names,
    [
      { description: "Grabpflege", vatRate: 7, price: 100, count: 1 },
      { description: "Blumenschmuck", vatRate: 19, price: 24.95, count: 2 },
      { description: "Grabstein reinigen", vatRate: 19, price: 47.81, count: 1 },
      { description: "Gießdienst", vatRate: 7, price: 3.7, count: 5 },
    ],
    term(2035, 2),
  );
  // 2035 H2 is not used by any other test, so no duplicate-generation conflicts
  await generateFor(page, 2035, 2);

  await page.goto(`/graves/${graveId}`);
  await expect(graveInvoiceRows(page)).toHaveCount(3);

  // the 7 % subtotal (100,00 + 18,50 = 118,50) and the 19 % one (49,90 +
  // 47,81 = 97,71) both divide by three. Each recipient pays 39,50 net at 7 %
  // and 32,57 net at 19 %: 72,07 net, VAT 2,77 + 6,19 = 8,96, total 81,03 —
  // asserted per row, so a split that shortchanged even one recipient fails
  await expect(graveInvoiceRows(page).filter({ hasText: "81,03 €" })).toHaveCount(3);
});

test("a grave splits a VAT-included price without taking its VAT out twice", async ({ page }) => {
  const marker = `GV-${unique()}`;
  const names = [`Vera ${marker}`, `Walter ${marker}`];
  const { graveId } = await seedGrave(
    page,
    marker,
    names,
    [{ vatRate: 7, price: 117.02, priceIncludesVat: true }],
    term(2036, 2),
  );
  await generateFor(page, 2036, 2);

  await page.goto(`/graves/${graveId}`);
  // the grave's own item list shows the gross and says so in its own column
  const careRow = card(page, "Leistungen").locator("tbody tr").filter({ hasText: "Grabpflege" });
  await expect(careRow).toContainText("Ja");
  // 117,02 gross at 7 % is a net of 109,36, whose 10 936 cents divide evenly by
  // two. The page shows the price exactly as it was typed.
  await expect(careRow).toContainText("117,02 €");

  // 54,68 net and 58,51 gross each — 117,02 together, the price that was entered
  await expect(graveInvoiceRows(page).filter({ hasText: "58,51 €" })).toHaveCount(2);

  await graveInvoiceRows(page)
    .filter({ hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);

  // the editor opens on the grave's gross — not on the 109,36 net it stores,
  // and not on either recipient's share
  const editor = drawer(page);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("117,02");
  await expect(editor.getByRole("checkbox", { name: "inkl. MwSt." })).toBeChecked();

  // saved back untouched, every share stays where it was
  await saveEditor(editor);
  await expect(graveInvoiceRows(page).filter({ hasText: "R1" })).toHaveCount(2);
  await expect(graveInvoiceRows(page).filter({ hasText: "58,51 €" })).toHaveCount(2);
});

test("the grave's item table shows the price as entered, its rate and the flag", async ({
  page,
}) => {
  const marker = `GK-${unique()}`;
  const { graveId } = await seedGrave(
    page,
    marker,
    [`Xaver ${marker}`, `Yvonne ${marker}`],
    [{ description: "Grabpflege", vatRate: 19, price: 100, count: 2 }],
  );

  await page.goto(`/graves/${graveId}`);
  const row = card(page, "Leistungen").locator("tbody tr").filter({ hasText: "Grabpflege" });

  // the price exactly as it was typed, with what qualifies it beside it. No net
  // or gross totals: a grave's page is about what was agreed, and those figures
  // belong to the invoices listed beneath it.
  await expect(row).toContainText("100,00 €");
  await expect(row).toContainText("19 %");
  // the flag reads as a word, since a screen reader announces these cell by cell
  await expect(row).toContainText("Nein");
  await expect(row).not.toContainText("200,00 €");
  await expect(row).not.toContainText("238,00 €");
});

test("an invoice works each rate's gross out at that rate, not at a blended one", async ({
  page,
}) => {
  const marker = `GR-${unique()}`;
  // Two rates over three recipients, both dividing: 7 % comes to 300,00 net and
  // 19 % to 99,00. A gross column that shares one blended total across every
  // line gives both the same multiplier — 300,00 at 7 % then read 331,71 €
  // beside its own "7 %".
  const { graveId } = await seedGrave(
    page,
    marker,
    [`Anna ${marker}`, `Bernd ${marker}`, `Carla ${marker}`],
    [
      { description: "Grabpflege", vatRate: 7, price: 100, count: 3 },
      { description: "Blumenschmuck", vatRate: 19, price: 99, count: 1 },
    ],
    term(2037, 1),
  );
  await generateFor(page, 2037, 1);

  const invoices = await (
    await page.request.get(`/odata/Invoices?$filter=GraveId eq ${graveId}&$select=Id,RootId`)
  ).json();
  await page.goto(`/graves/${graveId}/invoices/${invoices.value[0].RootId}`);

  // the lines are the whole grave, so the gross column is the grave's: 300,00
  // at 7 % is 321,00, and 99,00 at 19 % is 117,81
  const lines = page.locator(".p-accordionpanel").first().locator("tbody tr");
  await expect(lines.filter({ hasText: "Grabpflege" })).toContainText("321,00 €");
  await expect(lines.filter({ hasText: "Blumenschmuck" })).toContainText("117,81 €");
});

test("a grave whose prices do not divide is saved with confirmed corrections", async ({ page }) => {
  const marker = `GW-${unique()}`;
  // 47,80 at 19 % over three recipients: its 4 780 net cents leave a remainder,
  // so the three of them cannot be billed the same amount. The price is raised
  // to the next one that divides (47,82), shown before saving and confirmed.
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabstein reinigen",
    VatRate: 19,
    DefaultPrice: 47.8,
  });
  const names = [`Anna ${marker}`, `Bert ${marker}`, `Cilly ${marker}`];
  for (const name of names) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" });
  }
  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, `${marker}-item`);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("47,80");

  for (const name of names) {
    await addRecipient(page, editor, name);
  }

  // the preview says so before anything is saved
  const card = editor.getByRole("group", { name: "Volles H1" });
  await expect(card).toContainText("Geht auf (angepasst)");
  await expect(card).toContainText("angepasst, eingegeben 47,80 €");
  await expect(card).toContainText("47,82 €");

  // saving asks first and lists what changes
  await editor.getByRole("button", { name: "Speichern" }).click();
  const confirm = dialog(page, "Preise werden angepasst");
  await expect(confirm).toBeVisible();
  await expect(confirm).toContainText("47,80 €");
  await expect(confirm).toContainText("47,82 €");
  await waitForOverlayOpen(page);

  // cancelling leaves the editor open and the grave unsaved
  await confirm.getByRole("button", { name: "Abbrechen" }).click();
  await expect(confirm).toHaveCount(0);
  await expect(editor).toBeVisible();

  await editor.getByRole("button", { name: "Speichern" }).click();
  await expect(confirm).toBeVisible();
  await waitForOverlayOpen(page);
  await confirm.getByRole("button", { name: "Mit Anpassungen speichern" }).click();
  await expect(editor).toHaveCount(0);

  await expect(page.getByRole("table")).toContainText(marker);
});

test("the grave editor previews a prorated start and a seasonal item", async ({ page }) => {
  const marker = `GQ-${unique()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-pflege`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 60,
  });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-sommer`,
    Description: "Sommerbepflanzung",
    VatRate: 19,
    DefaultPrice: 40,
    Quarter: "Summer",
  });

  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  // always a start in the future: a started term has no start block
  const startYear = CURRENT_YEAR + 1;
  await fillDate(page, editor.getByLabel("Beginn"), `05/${startYear}`);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, `${marker}-pflege`);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 2, `${marker}-sommer`);

  // rate and quarter under the item
  await expect(editor).toContainText("Sommerbepflanzung · 19 % · Sommer");

  const start = editor.getByRole("group", { name: `Anfang · H1 ${startYear}` });
  await expect(start).toContainText("2 von 6 Monaten");
  await expect(start).toContainText(`anteilig 05–06/${startYear}`);
  await expect(start).toContainText("20,00");
  await expect(start).toContainText("Sommerbepflanzung");
  await expect(start).toContainText("40,00");
  const fullSecond = editor.getByRole("group", { name: "Volles H2" });
  await expect(fullSecond).toBeVisible();
  await expect(fullSecond).not.toContainText("Sommerbepflanzung");
  await expect(editor).toContainText("Noch kein Empfänger");
});

test("the grave editor asks for an item billed all year", async ({ page }) => {
  const marker = `GY-${unique()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-pflege`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 60,
  });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-sommer`,
    Description: "Sommerbepflanzung",
    VatRate: 19,
    DefaultPrice: 40,
    Quarter: "Summer",
  });

  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  const hint = editor.getByText("Ein Grab braucht mindestens eine ganzjährige Leistung.");
  await expect(hint).toHaveCount(0);

  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, `${marker}-sommer`);
  await expect(hint).toBeVisible();

  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 2, `${marker}-pflege`);
  await expect(hint).toHaveCount(0);
});

test("a new grave takes at most four recipients", async ({ page }) => {
  const marker = `GM-${unique()}`;
  const names = [1, 2, 3, 4, 5].map((n) => `Empf${n} ${marker}`);
  for (const name of names) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" });
  }

  await page.goto("/graves");
  const editor = await startNewGrave(page, marker);
  for (const name of names.slice(0, 4)) {
    await addRecipient(page, editor, name);
  }

  await expect(editor.getByRole("button", { name: "Hinzufügen", exact: true })).toHaveCount(0);
  await expect(editor).toContainText("Ein Grab hat höchstens 4 Empfänger.");
});

test("the overview searches graves by recipient name as well", async ({ page }) => {
  const marker = `GS-${unique()}`;
  await seedGrave(page, marker, [`Suchname ${marker}`]);

  await page.goto("/graves");
  await searchColumn(page, "Empfänger", `Suchname ${marker}`);
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.locator("tbody tr")).toContainText(marker);
});

test("the overview searches each column on its own", async ({ page }) => {
  const marker = `GK-${unique()}`;
  // a cemetery of its own, so that picking it leaves this test's grave alone
  const cemetery = `Spaltenfriedhof ${unique()}`;
  const cemeteryId = await ensureCemetery(page, cemetery);
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  // the street carries the marker and the names do not: what finds the grave
  // is its recipient's address
  const ida = await createRow(page, "/odata/Recipients", {
    Name: "Ida Spaltensuche",
    Address: `${marker}-Weg 5\n12345 Musterstadt`,
  });
  const jan = await createRow(page, "/odata/Recipients", {
    Name: "Jan Spaltensuche",
    Address: "Birkenweg 7\n12345 Musterstadt",
  });
  // due in the first half of 2026 alone, which no test generates: both stay open
  await saveGrave(page, {
    graveNumber: `${marker}-A`,
    cemeteryId,
    startDate: "2026-01",
    endDate: "2026-06",
    items: [{ itemId: item.Id }],
    recipientIds: [ida.Id],
  });
  await saveGrave(page, {
    graveNumber: `${marker}-B`,
    startDate: "2026-03",
    endDate: "2026-06",
    items: [{ itemId: item.Id }],
    recipientIds: [jan.Id],
  });

  await page.goto("/graves");
  await fillYear(page, page.locator("#gravesYear"), "2026");
  await halfButton(page, 1).click();
  await expect(halfButton(page, 1)).toHaveAttribute("aria-pressed", "true");
  const rows = page.locator("tbody tr");

  // the recipient's street, in another case, finds the grave -- and only the
  // recipients are searched: the other grave's number holds the marker too
  await searchColumn(page, "Empfänger", `${marker.toLowerCase()}-weg 5`);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-A`);
  // a grave with one recipient names it
  await expect(rows).toContainText("Ida Spaltensuche");
  await searchColumn(page, "Empfänger", "");

  await filterColumn(page, "Friedhof", cemetery);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-A`);
  await filterColumn(page, "Friedhof", "Alle");

  await searchColumn(page, "Grabnummer", marker);
  await expect(rows).toHaveCount(2);
  await filterColumn(page, "Status", "offen");
  await expect(rows).toHaveCount(2);
  await filterColumn(page, "Status", "erstellt");
  await expect(page.getByRole("table")).toContainText("Keine Daten");
  await filterColumn(page, "Status", "Alle");
  await expect(rows).toHaveCount(2);

  // the month as the column shows it: B starts in March
  await searchColumn(page, "Beginn", "01/2026");
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-A`);
});

test("a grave with several recipients lists them in a popover", async ({ page }) => {
  const marker = `GP-${unique()}`;
  const names = [`Erste ${marker}`, `Zweite ${marker}`];
  await seedGrave(page, marker, names, [{}], term(2026, 1));

  await page.goto(`/graves?period=2026-1&number=${marker}`);
  const row = page.locator("tbody tr").filter({ hasText: marker });
  const button = row.getByRole("button", { name: "2 Empfänger" });
  await expect(button).toBeVisible();
  await expect(row).not.toContainText(names[0]);

  const popover = await openPopover(page, button);
  await expect(popover.getByRole("listitem")).toHaveText(names);

  // pressed again, the button closes it
  await button.click();
  await expect(popover).toHaveCount(0);
  await expect(button).toHaveAttribute("aria-expanded", "false");
});

test("the overview fits its card on a laptop and on a wide screen", async ({ page }) => {
  const marker = `GW-${unique()}`;
  const cemetery = `Waldfriedhof am Schwarzenstein ${unique()}`;
  const cemeteryId = await ensureCemetery(page, cemetery);
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = async (name: string) =>
    (
      await createRow(page, "/odata/Recipients", {
        Name: name,
        Address: "Hohenberger Landstraße 117\n12345 Musterstadt-Schwarzenstein",
      })
    ).Id;
  // as long as real ones get: a site and a name that take several lines
  const siteName = "Schineller Bäcke an der alten Friedhofsmauer, Nordseite";
  await saveGrave(page, {
    graveNumber: `${marker}-EINZELGRABSTELLE-NORD`,
    siteName,
    cemeteryId,
    startDate: "2026-01",
    endDate: "2026-06",
    items: [{ itemId: item.Id }],
    recipientIds: [await recipient(`Maria-Theresia von Hohenberg-Schwarzenstein ${marker}`)],
  });
  await saveGrave(page, {
    graveNumber: `${marker}-DOPPELGRABSTELLE-SUED`,
    siteName,
    cemeteryId,
    startDate: "2026-01",
    endDate: "2026-06",
    items: [{ itemId: item.Id }],
    recipientIds: [
      await recipient(`Friedrich-Wilhelm Schwarzenstein ${marker}`),
      await recipient(`Elisabeth Schwarzenstein-Hohenberg ${marker}`),
    ],
  });

  for (const width of [1280, 1536]) {
    await page.setViewportSize({ width, height: 800 });
    await page.goto(`/graves?period=2026-1&number=${marker}`);
    const rows = page.locator("tbody tr");
    await expect(rows).toHaveCount(2);
    await expect(rows.first()).toContainText(siteName);

    // the filter row and the months come with the room for them
    const filterRow = page.getByRole("textbox", { name: "Beginn durchsuchen" });
    const months = page.getByRole("columnheader", { name: "Beginn", exact: true });
    if (width < 1536) {
      await expect(filterRow).toBeHidden();
      await expect(months).toBeHidden();
    } else {
      await expect(filterRow).toBeVisible();
      await expect(months).toBeVisible();
    }

    // the table needs no sideways scrolling inside its card
    const container = page.locator(".p-datatable-table-container");
    const sizes = await container.evaluate((el) => ({
      scroll: el.scrollWidth,
      client: el.clientWidth,
      left: el.getBoundingClientRect().left,
    }));
    expect(sizes.scroll, `${width}px`).toBeLessThanOrEqual(sizes.client + 1);

    // and every row's actions are inside the card
    for (const row of await rows.all()) {
      const actions = row.locator("td").last().getByRole("button");
      expect(await actions.count()).toBe(2);
      for (const action of await actions.all()) {
        const box = (await action.boundingBox())!;
        const name = `${width}px ${await action.getAttribute("aria-label")}`;
        expect(box.x, name).toBeGreaterThanOrEqual(sizes.left - 1);
        expect(box.x + box.width, name).toBeLessThanOrEqual(sizes.left + sizes.client + 1);
      }
    }
  }
});

test("coming back from a grave keeps the half-year and the filters", async ({ page }) => {
  const marker = `GB-${unique()}`;
  // not today's half-year, and years ahead
  const year = CURRENT_YEAR + 7;
  const half = CURRENT_HALF === 1 ? 2 : 1;
  await seedGrave(page, marker, [`Rückweg ${marker}`], [{}], term(year, half));

  await page.goto("/graves");
  await fillYear(page, page.locator("#gravesYear"), String(year));
  await halfButton(page, half).click();
  await expect(halfButton(page, half)).toHaveAttribute("aria-pressed", "true");
  await searchColumn(page, "Grabnummer", marker);
  const rows = page.locator("tbody tr");
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(marker);

  const kept = async () => {
    await expect(page.locator("#gravesYear")).toHaveValue(String(year));
    await expect(halfButton(page, half)).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("textbox", { name: "Grabnummer durchsuchen" })).toHaveValue(marker);
    await expect(rows).toHaveCount(1);
    await expect(rows).toContainText(marker);
  };

  // through the browser's back button
  await rows.getByRole("link", { name: marker }).click();
  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
  await page.goBack();
  await kept();

  // through the breadcrumb
  await rows.getByRole("link", { name: marker }).click();
  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
  await page
    .getByRole("navigation", { name: "Seitenpfad" })
    .getByRole("link", { name: "Gräber" })
    .click();
  await kept();
});

test("a grave lies in a cemetery, which the overview shows and searches", async ({ page }) => {
  const marker = `GF-${unique()}`;
  // a cemetery of its own: picked in the overview, it leaves this grave alone
  const cemetery = `Waldfriedhof ${unique()}`;
  const graveNumber = `F-${marker}`;
  const recipientName = `Friedhof Beispiel ${marker}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: graveNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Weg 3" });
  await ensureCemetery(page, cemetery);
  await page.goto("/graves");

  // the cemetery is required, and asked for before anything goes to the server
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await editor.getByRole("button", { name: "Speichern" }).click();
  await expect(toasts(page).filter({ hasText: "Bitte wählen Sie einen Friedhof." })).toBeVisible();
  await expect(editor).toBeVisible();

  // the search in the field leaves the cemeteries that hold what is typed
  const field = page.locator("#graveCemetery");
  await field.click();
  const overlay = page.locator(".p-select-overlay");
  await expect(overlay).not.toHaveClass(/p-connected-overlay-enter-active/);
  await overlay.locator("input.p-select-filter").fill(cemetery.toUpperCase());
  await expect(overlay.getByRole("option")).toHaveCount(1);
  await expect(overlay.getByRole("option", { name: cemetery })).toBeVisible();
  await field.click();
  await expect(overlay).toHaveCount(0);
  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);

  await startNewGrave(page, graveNumber, { cemetery });
  await expect(page.locator("#graveCemetery")).toHaveText(cemetery);
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, graveNumber);
  await addRecipient(page, editor, recipientName);
  await saveEditor(editor);

  const row = page.getByRole("row").filter({ hasText: graveNumber });
  await expect(row.getByRole("cell", { name: cemetery, exact: true })).toBeVisible();

  // the overview's cemetery filter finds the grave as well
  await filterColumn(page, "Friedhof", cemetery);
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.locator("tbody tr")).toContainText(graveNumber);

  // and the grave's page names it
  await row.getByRole("link", { name: graveNumber }).click();
  await expect(page.getByRole("heading", { level: 1, name: graveNumber })).toBeVisible();
  const cemeteryTerm = page.locator("dt", { hasText: /^Friedhof$/ });
  await expect(cemeteryTerm.locator("xpath=following-sibling::dd")).toHaveText(cemetery);
});

test("a grave number cannot be given to two graves at the same time", async ({ page }) => {
  const itemNumber = `OV-${RUN}`;
  const recipientName = `Olga Overlap ${RUN}`;
  const graveNumber = `OV-${RUN}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Weg 1",
  });
  await saveGrave(page, {
    graveNumber,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber, { end: null });
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  await addRecipient(page, editor, recipientName);
  await editor.getByRole("button", { name: "Speichern" }).click();

  await expect(
    page.getByText(`Die Grabnummer „${graveNumber}“ ist von 01/2026 bis 12/2040 bereits vergeben.`),
  ).toBeVisible();
  await expect(editor).toBeVisible();
});

test("a grave can be saved without an end", async ({ page }) => {
  const itemNumber = `OE-${RUN}`;
  const recipientName = `Otto Openend ${RUN}`;
  const graveNumber = `OE-${RUN}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Pflege",
    VatRate: 7,
    DefaultPrice: 50,
  });
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Weg 2" });

  await page.goto("/graves");
  const editor = await startNewGrave(page, graveNumber, { end: null });
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 1, itemNumber);
  await addRecipient(page, editor, recipientName);
  await saveEditor(editor);

  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=StartDate,EndDate`,
    )
  ).json();
  expect(graves.value[0].StartDate).toBe("2026-01-01");
  expect(graves.value[0].EndDate).toBeNull();

  // reopened, the open end reads as an empty field
  await page
    .getByRole("row")
    .filter({ hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);
  await expect(editor.getByLabel("Ende", { exact: true })).toHaveValue("");
  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editor).toHaveCount(0);
});

// The half-year today falls in is the one piece of state these tests share
// and cannot divide between them: the overview and the grave page show it
// without being asked, and a generation for it invoices every grave due in it
// at once, whoever seeded the grave. A test that asserts its fresh grave is
// still open fails whenever another test generates this half-year between its
// seeding and its assertion. So the tests that generate the current half-year
// and the tests that read a grave's state in it must not run at the same time
// as each other: they run one after another, in one worker, while everything
// else runs beside them. Their order does not matter: each seeds its own
// graves and generates what it needs itself, and a grave seeded after a
// generation is open again. Every other test keeps away from this half-year's
// state.
//
// "default" rather than "serial": the group still runs in order on one worker
// despite fullyParallel, but a failing test does not skip the ones after it —
// none of them depends on it.
test.describe("the half-year today falls in", () => {
  test.describe.configure({ mode: "default" });

  test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({
    page,
  }) => {
    const firstGraveNumber = await seedGraveGenerationData(page, 60);
    await page.goto("/graves");
    const table = page.getByRole("table");
    await expect(table).toContainText(firstGraveNumber);

    await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
    await waitForOverlayOpen(page);
    await withGenerationLock(async () => {
      await dialog(page, "Rechnungen erzeugen")
        .getByRole("button", { name: "Jetzt erzeugen" })
        .click();

      // the blocking progress overlay is visible while the job runs
      await expect(blocking(page)).toBeVisible({ timeout: 5000 });
      await expect(blocking(page)).toBeHidden({ timeout: 90000 });

      // the result is reported, and the status column now shows graves as invoiced
      const result = dialog(page, "Rechnungen erzeugen");
      await expect(result).toContainText(/Erstellt: [1-9]/);
      await expect(table.locator(".p-tag-success", { hasText: "erstellt" }).first()).toBeVisible();
      await result.getByRole("button", { name: "OK" }).click();
      await expect(result).toHaveCount(0);
      expect(await periodInvoices(page)).not.toHaveLength(0);

      await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
      await waitForOverlayOpen(page);
      await dialog(page, "Rechnungen erzeugen")
        .getByRole("button", { name: "Jetzt erzeugen" })
        .click();
      // either the "already invoiced" refusal (when every grave is invoiced) or the
      // result of a second run, for graves other specs created in the meantime
      const alreadyInvoiced = toasts(page).filter({
        hasText: "Für dieses Halbjahr wurden bereits Rechnungen erstellt.",
      });
      await expect(
        alreadyInvoiced.or(dialog(page, "Rechnungen erzeugen").getByText(/Erstellt:/)),
      ).toBeVisible({ timeout: 90000 });
    });

    // FR-027: no grave and recipient may end up with two invoices for the same
    // half-year. Asserting the pairs rather than a total keeps this immune to the
    // graves other specs create in parallel, which a second run may legitimately
    // invoice for the first time.
    const pairs = await periodInvoices(page);
    const seen = pairs.map((i) => `${i.GraveId}:${i.RecipientId}`);
    expect(new Set(seen).size).toBe(seen.length);
  });

  // A poll that got no answer -- a dropped connection, a server restarting
  // under the page -- used to end the wait: the overlay closed while the job
  // ran on, the browser's English "Failed to fetch" was shown, and the result
  // never was.
  test("a generation outlasts answers lost while it runs (Story 13)", async ({ page }) => {
    const marker = `GB-${unique()}`;
    await seedGrave(page, marker, [`Anna ${marker}`]);
    let lost = 0;
    await page.route(/\/odata\/Jobs\(\d+\)/, (route) =>
      lost++ < 2 ? route.abort("connectionreset") : route.continue(),
    );
    await page.goto("/graves");

    await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
    await waitForOverlayOpen(page);
    await withGenerationLock(async () => {
      await dialog(page, "Rechnungen erzeugen")
        .getByRole("button", { name: "Jetzt erzeugen" })
        .click();

      const result = dialog(page, "Rechnungen erzeugen");
      await expect(result).toContainText(/Erstellt: [1-9]/, { timeout: 90000 });
      expect(lost).toBeGreaterThanOrEqual(2);
      await expect(toasts(page)).toHaveCount(0);
      await result.getByRole("button", { name: "OK" }).click();
    });
    await expect(page.getByRole("row").filter({ hasText: marker })).toContainText("erstellt");
  });

  test("the grave detail page shows the grave, its status and its invoices", async ({ page }) => {
    const marker = `GD-${unique()}`;
    const names = [`Anna ${marker}`, `Bernd ${marker}`];
    // due today, which the page shows, and in the half-year after it, which this
    // test generates and no other test does — and in no half-year beyond, so
    // another test's generation never reaches it
    const { graveId } = await seedGrave(
      page,
      marker,
      names,
      [{}],
      term(CURRENT_YEAR, CURRENT_HALF, { year: NEXT_YEAR, half: NEXT_HALF }),
    );

    await page.goto(`/graves/${graveId}`);

    await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
    // this grave was just created, and no test generates the current half-year
    // while this one runs, so nothing has invoiced it yet
    await expect(page.getByText(`Halbjahr H${CURRENT_HALF} ${CURRENT_YEAR}`)).toBeVisible();
    await expect(page.locator(".p-tag-danger", { hasText: "offen" })).toBeVisible();
    for (const name of names) {
      await expect(card(page, "Empfänger").getByText(name)).toBeVisible();
    }
    await expect(
      page.getByText("Für dieses Grab wurden noch keine Rechnungen erstellt."),
    ).toBeVisible();

    // a period no other test uses, so the rows below are exactly this generation
    await generateFor(page, NEXT_YEAR, NEXT_HALF);
    await page.reload();

    await expect(graveInvoiceRows(page)).toHaveCount(2);
    await expect(card(page, "Rechnungen")).toContainText(`H${NEXT_HALF} ${NEXT_YEAR}`);
    // each recipient pays half of 1 × 100.00 + 7% = 53,50
    await expect(graveInvoiceRows(page).filter({ hasText: "53,50 €" })).toHaveCount(2);

    // both documents are reachable per row
    const row = graveInvoiceRows(page).filter({ hasText: names[0] });
    await expect(row.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
    await expect(row.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
  });

  test("the grave list colours its status and offers the invoice mass download", async ({
    page,
  }) => {
    const marker = `GL-${unique()}`;
    await seedGrave(page, marker, [`Liste ${marker}`]);

    await page.goto("/graves");
    const row = page.getByRole("row").filter({ hasText: marker });
    await expect(row).toBeVisible();

    // just created, and no test generates the current half-year while this one
    // runs, so this grave is still open — and an open half-year reads red
    await expect(row.locator(".p-tag-danger", { hasText: "offen" })).toBeVisible();

    // the details action reaches the grave's own page
    await row.getByRole("button", { name: rowAction("Details") }).click();
    await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
    await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();

    // and so does the grave's number
    await page.goto("/graves");
    await page.getByRole("link", { name: marker, exact: true }).click();
    await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();

    // the mass download hands back a ZIP of the half-year the overview shows —
    // once there is something in it: with no invoice for that half-year the page
    // says so instead of saving an empty archive. This test's own grave is the
    // one it can count on, so it invoices it.
    await generateFor(page, CURRENT_YEAR, CURRENT_HALF);
    await page.goto("/graves");

    // invoiced now, the grave is among the "erstellt" ones and no longer an open one
    await searchColumn(page, "Grabnummer", marker);
    await filterColumn(page, "Status", "erstellt");
    await expect(page.locator("tbody tr")).toHaveCount(1);
    await expect(page.locator("tbody tr")).toContainText(marker);
    await filterColumn(page, "Status", "offen");
    await expect(page.getByRole("table")).toContainText("Keine Daten");

    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Rechnungen (ZIP)" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe("rechnungen.zip");
  });

  test("the half-year status announces its meaning, not a validation error", async ({ page }) => {
    const marker = `GA-${unique()}`;
    await seedGrave(page, marker, [`Ansage ${marker}`]);

    await page.goto("/graves");
    const status = page.getByRole("row").filter({ hasText: marker }).locator(".p-tag").first();
    await expect(status).toHaveText("offen");

    // The red state could make a screen reader call an open half-year an invalid
    // entry; the tag carries its own announcement instead.
    await expect(status).toHaveAttribute("aria-label", "Rechnungen offen");
  });

  // The overview lists the graves the business bills. A cooperative grave is
  // billed by the cooperative: it has a list of its own and is not shown here.
  test("the overview leaves cooperative graves to their own list", async ({ page }) => {
    const marker = `CO-${unique()}`;
    await seedCooperativeGrave(page, `${marker}-G`, {
      startDate: `${CURRENT_YEAR - 1}-01`,
      endDate: null,
    });
    await seedGrave(page, `${marker}-E`, [`Empfänger ${marker}`]);

    await page.goto("/graves");
    await searchColumn(page, "Grabnummer", marker);
    await expect(page.locator("tbody tr")).toHaveCount(1);
    await expect(page.locator("tbody tr")).toContainText(`${marker}-E`);
    await expect(page.locator("tbody")).not.toContainText(`${marker}-G`);

    await page.goto("/cooperative-graves");
    await searchColumn(page, "Grabnummer", marker);
    await expect(page.locator("tbody tr")).toHaveCount(1);
    await expect(page.locator("tbody tr")).toContainText(`${marker}-G`);
  });

  test("the detail page names a cooperative grave", async ({ page }) => {
    const marker = `CD-${unique()}`;
    const graveId = await seedCooperativeGrave(page, marker, {
      startDate: `${CURRENT_YEAR - 1}-01`,
      endDate: null,
    });

    await page.goto(`/graves/${graveId}`);
    await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
    await expect(page.locator(".p-tag", { hasText: "Genossenschaftsgrab" })).toBeVisible();
    // the current half-year is not open, it is the cooperative's
    await expect(page.getByText(`Halbjahr H${CURRENT_HALF} ${CURRENT_YEAR}`)).toBeVisible();
    await expect(page.locator(".p-tag-secondary", { hasText: /^Genossenschaft$/ })).toBeVisible();
    await expect(page.locator(".p-tag-danger", { hasText: "offen" })).toHaveCount(0);
    // the cooperative bills it, so there is no billing preview
    await expect(page.getByText("Beispielrechnungen")).toHaveCount(0);
  });

  test("the detail page shows an ended cooperative grave as outside its term", async ({ page }) => {
    const marker = `CE-${unique()}`;
    const graveId = await seedCooperativeGrave(page, marker, {
      startDate: `${CURRENT_YEAR - 2}-01`,
      endDate: `${CURRENT_YEAR - 1}-12`,
    });

    await page.goto(`/graves/${graveId}`);
    await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
    await expect(
      page.locator(".p-tag-secondary", { hasText: "außerhalb der Laufzeit" }),
    ).toBeVisible();
    await expect(page.locator(".p-tag", { hasText: /^Genossenschaft$/ })).toHaveCount(0);
  });

  test("the grave detail page shows this half-year's billed price once it is today's", async ({
    page,
  }) => {
    const marker = `GC-${unique()}`;
    const names = [`Uwe ${marker}`, `Vroni ${marker}`, `Wanda ${marker}`];
    const { graveId } = await seedGrave(page, marker, names, [
      { description: "Grabpflege", vatRate: 7, price: 100, count: 1 },
      { description: "Blumenschmuck", vatRate: 19, price: 24.95, count: 2 },
      { description: "Grabstein reinigen", vatRate: 19, price: 47.81, count: 1 },
      { description: "Gießdienst", vatRate: 7, price: 3.7, count: 5 },
    ]);

    // Another test may already have invoiced other graves for the current
    // half-year, but GenerateGraveInvoices only refuses a request once *every*
    // grave has a run for that period — this one, seeded just above, does not.
    await generateFor(page, CURRENT_YEAR, CURRENT_HALF);

    await page.goto(`/graves/${graveId}`);
    // The page has no half-year selector: it always shows what was billed for
    // the half-year today falls in, which is now invoiced.
    await expect(page.locator(".p-tag-success", { hasText: "erstellt" })).toBeVisible();
    // The 19 % subtotal is 49,90 + 47,81 = 97,71, which divides by three; the
    // page shows the grave's own price, unadjusted.
    const stoneRow = card(page, "Leistungen")
      .locator("tbody tr")
      .filter({ hasText: "Grabstein reinigen" });
    await expect(stoneRow).toContainText("47,81 €");
  });

  test("a half-year reads partly paid until every recipient has paid (Story 6)", async ({
    page,
  }) => {
    // a grave billed to more than one recipient is what makes "teilweise
    // bezahlt" observable at all; the overview shows the current half-year, so
    // that is the one generated for it
    const graveNumber = `GZ-${unique()}`;
    const { graveId } = await seedGrave(page, graveNumber, [
      `Anna ${graveNumber}`,
      `Clara ${graveNumber}`,
    ]);
    await generateFor(page, CURRENT_YEAR, CURRENT_HALF);

    const invoices = await (
      await page.request.get(
        `/odata/Invoices?$filter=GraveId eq ${graveId} and PeriodYear eq ${CURRENT_YEAR}` +
          ` and PeriodHalf eq ${CURRENT_HALF} and IsLatestRevision eq true&$orderby=Id`,
      )
    ).json();
    const invoiceIds = (invoices.value as { Id: number }[]).map((invoice) => invoice.Id);
    expect(invoiceIds.length).toBeGreaterThan(1);

    await page.request.post("/odata/SetInvoicePaid", {
      data: { invoiceId: invoiceIds[0], paid: true },
    });
    await page.goto("/graves");
    const row = page.getByRole("row").filter({ hasText: graveNumber });
    // the count says how many of the grave's recipients have settled
    await expect(row).toContainText(`teilweise bezahlt (1 / ${invoiceIds.length})`);

    for (const invoiceId of invoiceIds.slice(1)) {
      await page.request.post("/odata/SetInvoicePaid", { data: { invoiceId, paid: true } });
    }
    await page.reload();
    // "bezahlt" alone would also match "teilweise bezahlt", so this would pass
    // even if the last recipient's mark did nothing
    await expect(row).toContainText("bezahlt");
    await expect(row).not.toContainText("teilweise");
    await expect(row).not.toContainText("unbezahlt");

    // fully paid, so the unpaid filter drops it
    await filterColumn(page, "Zahlung", "Nur unbezahlte");
    await expect(page.getByRole("row").filter({ hasText: graveNumber })).toHaveCount(0);
  });

  test("the box is locked once the grave has invoices", async ({ page }) => {
    const marker = `CL-${unique()}`;
    const { graveId } = await seedGrave(page, marker, [`Sperre ${marker}`]);
    await generateFor(page, CURRENT_YEAR, CURRENT_HALF);

    const editor = await editGraveOnItsPage(page, graveId);
    await expect(editor.getByLabel("Genossenschaftsgrab")).toBeDisabled();
    await expect(editor).toContainText(
      "Das Grab hat Rechnungen; ein Wechsel geht nur ab einem Monat.",
    );
    // the change is offered from a month on instead
    await expect(
      editor.getByRole("button", { name: "Ab Monat … über die Genossenschaft" }),
    ).toBeVisible();
    await expect(editor.getByRole("button", { name: "Ab Monat … selbst abrechnen" })).toHaveCount(
      0,
    );
  });

  test("a cooperative grave hands over from a month on", async ({ page }) => {
    const marker = `CH-${unique()}`;
    const graveId = await seedCooperativeGrave(page, marker, {
      startDate: `${CURRENT_YEAR - 1}-01`,
      endDate: null,
    });
    const recipient = await createRow(page, "/odata/Recipients", {
      Name: `Neu ${marker}`,
      Address: "Musterweg 1",
    });
    // the month before the hand-over month is the predecessor's last
    const month = CURRENT_HALF === 1 ? "01" : "07";
    const lastMonth = CURRENT_HALF === 1 ? `12/${CURRENT_YEAR - 1}` : `06/${CURRENT_YEAR}`;

    const editor = await editGraveOnItsPage(page, graveId);
    await editor.getByRole("button", { name: "Ab Monat … selbst abrechnen" }).click();
    await waitForOverlayOpen(page);
    const chooser = dialog(page, "Grab ab einem Monat übergeben");
    await fillDate(page, chooser.getByLabel("Ab Monat"), `${month}/${CURRENT_YEAR}`);
    await chooser.getByRole("button", { name: "Weiter" }).click();

    // the successor's editor: prefilled from the grave, of the other kind
    await expect(editor.getByLabel("Grabnummer")).toHaveValue(marker);
    await waitForOverlayOpen(page);
    await expect(editor.getByLabel("Genossenschaftsgrab")).not.toBeChecked();
    await expect(editor.getByLabel("Beginn")).toHaveValue(`${month}/${CURRENT_YEAR}`);
    await expect(editor.getByLabel("Ende", { exact: true })).toHaveValue("");
    await expect(editor.getByLabel("Feld-Nr.")).toHaveValue("1");
    await addRecipient(page, editor, recipient.Name);
    await saveEditor(editor);

    // the overview of this half-year lists the successor as still to be invoiced ...
    await page.goto("/graves");
    await searchColumn(page, "Grabnummer", marker);
    const rows = page.getByRole("row").filter({ hasText: marker });
    await expect(rows).toHaveCount(1);
    await expect(rows.locator(".p-tag-danger", { hasText: "offen" })).toBeVisible();

    // ... and the predecessor ends the month before it
    await page.goto(`/graves/${graveId}`);
    await expect(page.getByText(lastMonth)).toBeVisible();
  });
});

// The job's id lived only in the page that started it. Reloaded mid-run, the
// page showed the overview as if nothing were running -- no block, "Jetzt
// erzeugen" offered again -- and the result, the one place skipped graves are
// named, was never shown. A real job cannot be held running until a reload has
// landed, so the browser is given the job it started, as the page keeps it,
// and the job's answers are stood in for: still running when the page loads,
// finished on the third look.
test("a generation still running when the page loads is shown and reported (Story 13)", async ({
  page,
}) => {
  const job = (status: string, percent: number, resultJson: string | null) => ({
    Id: 424242,
    Type: "GraveInvoices",
    Status: status,
    ProgressPercent: percent,
    TotalCount: 7,
    ProcessedCount: Math.round((percent / 100) * 7),
    PayloadJson: `{"Year":${CURRENT_YEAR},"Half":${CURRENT_HALF}}`,
    ResultJson: resultJson,
    Error: null,
    CreatedAt: "2026-10-08T08:00:00Z",
    StartedAt: "2026-10-08T08:00:01Z",
    FinishedAt: null,
  });
  let looks = 0;
  await page.route(/\/odata\/Jobs\(424242\)/, (route) =>
    route.fulfill({
      json:
        ++looks < 3
          ? job("Running", 40 + looks * 20, null)
          : job("Succeeded", 100, '{"InvoicedGraves":7,"SkippedGraves":[]}'),
    }),
  );
  // what the page that started the job kept, before it was reloaded
  await page.goto("/");
  await page.evaluate(() => localStorage.setItem("followedGenerationJob", "424242"));

  await page.goto("/graves");

  await expect(blocking(page)).toBeVisible();
  const result = dialog(page, "Rechnungen erzeugen");
  await expect(result).toContainText("Erstellt: 7");
  await expect(blocking(page)).toBeHidden();
  await result.getByRole("button", { name: "OK" }).click();

  // shown once: the next visit has nothing left to report
  const looked = looks;
  await page.reload();
  await expect(page.getByRole("table")).toBeVisible();
  await expect(result).toHaveCount(0);
  expect(looks).toBe(looked);
});

// The grave editor is the long form of the app, and every way out of it used
// to drop what was typed without a word: a click beside the panel, Escape --
// even an Escape meant for a list's search field -- and Cancel.
test("the grave editor asks before it drops what was typed", async ({ page }) => {
  await page.goto("/graves");
  const editor = drawer(page);
  const number = editor.getByLabel("Grabnummer");

  // nothing typed yet: Escape just closes
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  await number.press("Escape");
  await expect(editor).toHaveCount(0);

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  await number.fill(`ASK-${unique()}`);

  // Escape in a list's search field closes the list, not the editor
  await page.locator("#graveRecipient").click();
  const list = page.locator(".p-select-overlay");
  await list.locator("input.p-select-filter").press("Escape");
  await expect(list).toHaveCount(0);
  await expect(confirmation(page)).toHaveCount(0);
  await expect(editor).toBeVisible();

  // every way out asks, and "Abbrechen" keeps what was typed
  const typed = await number.inputValue();
  const ways: [string, () => Promise<void>][] = [
    ["Escape", () => number.press("Escape")],
    ["a click beside it", () => page.mouse.click(40, 360)],
    ["Cancel", () => editor.getByRole("button", { name: "Abbrechen" }).click()],
  ];
  for (const [way, leave] of ways) {
    await leave();
    await expect(confirmation(page), way).toBeVisible();
    await confirmation(page).getByRole("button", { name: "Abbrechen" }).click();
    await expect(confirmation(page)).toHaveCount(0);
    await expect(editor, way).toBeVisible();
    await expect(number).toHaveValue(typed);
  }

  // and "Ja" lets it go
  await editor.getByRole("button", { name: "Abbrechen" }).click();
  await confirmation(page).getByRole("button", { name: "Ja" }).click();
  await expect(editor).toHaveCount(0);
});
