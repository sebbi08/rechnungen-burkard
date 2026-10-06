import { expect, type Locator, type Page, test } from "@playwright/test";
import { createRow, ensureRow, seedGrave as saveGrave, type SeedGraveLine } from "../seed";
import {
  dialog,
  drawer,
  fillDate,
  fillNumber,
  fillYear,
  pickItem,
  pickOption,
  pickRecipient,
  rowAction,
  saveEditor,
  search,
  toasts,
  waitForOverlayOpen,
} from "../vue";

// Unique per test run. `npm test` starts with an empty database, but CI runs the
// suite against an image it started once, and a rerun there must not collide
// with the rows the first attempt left or make assertions vacuous.
const RUN = Date.now();

// The overview and the generate dialog both open on the half-year today falls
// in, so a generation started without further input lands here. Derived from
// the same clock the app reads, not hardcoded, or every January would turn
// this file red.
const CURRENT_YEAR = new Date().getFullYear();
const CURRENT_HALF = new Date().getMonth() < 6 ? 1 : 2;

// How many graves the generation test below seeds. Shared so the payment test
// further down can name the exact grave `seedGraveGenerationData` gives two
// recipients (`G-${RUN}-<this, zero-padded>`) instead of searching across
// every grave invoiced for the current half-year: a full-suite run also
// invoices other spec files' graves for this same period, and at least one of
// them (import-export.spec.ts's `IMP-G1-*`) has two recipients too.
const GRAVE_GENERATION_COUNT = 60;

// A generation renders the documents of every grave not yet invoiced for its
// half-year, and a stack that kept the graves of earlier runs has more of them
// each time: generateFor waits up to 90s for its job, so a test has to be
// allowed longer than that.
test.describe.configure({ timeout: 150_000 });

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
  // linked to the last grave only (below): a grave billed to more than one
  // recipient is what makes "teilweise bezahlt" observable at all
  const secondRecipient = await createRow(page, "/odata/Recipients", {
    Name: `Clara Beispiel ${RUN}`,
    Address: "Seitenweg 2",
  });

  for (let i = 1; i <= count; i++) {
    await saveGrave(page, {
      graveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
      items: [{ itemId: item.Id, count: 1 }],
      recipientIds: i === count ? [recipient.Id, secondRecipient.Id] : [recipient.Id],
    });
  }
  return `G-${RUN}-001`;
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

test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({
  page,
}) => {
  const firstGraveNumber = await seedGraveGenerationData(page, GRAVE_GENERATION_COUNT);
  await page.goto("/graves");
  const table = page.getByRole("table");
  await expect(table).toContainText(firstGraveNumber);

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForOverlayOpen(page);
  await dialog(page, "Rechnungen erzeugen").getByRole("button", { name: "Jetzt erzeugen" }).click();

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
  await dialog(page, "Rechnungen erzeugen").getByRole("button", { name: "Jetzt erzeugen" }).click();
  // either the "already invoiced" refusal (when every grave is invoiced) or the
  // result of a second run, for graves other specs created in the meantime
  const alreadyInvoiced = toasts(page).filter({
    hasText: "Für dieses Halbjahr wurden bereits Rechnungen erstellt.",
  });
  await expect(
    alreadyInvoiced.or(dialog(page, "Rechnungen erzeugen").getByText(/Erstellt:/)),
  ).toBeVisible({ timeout: 90000 });

  // FR-027: no grave and recipient may end up with two invoices for the same
  // half-year. Asserting the pairs rather than a total keeps this immune to the
  // graves other specs create in parallel, which a second run may legitimately
  // invoice for the first time.
  const pairs = await periodInvoices(page);
  const seen = pairs.map((i) => `${i.GraveId}:${i.RecipientId}`);
  expect(new Set(seen).size).toBe(seen.length);
});

/** Opens the grave editor for a new grave and fills in its number, site and term. */
async function startNewGrave(
  page: Page,
  graveNumber: string,
  { end = "12/2040" }: { end?: string | null } = {},
): Promise<Locator> {
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await editor.getByLabel("Grabnummer").fill(graveNumber);
  await editor.getByLabel("Feld-Nr.").fill("27");
  await editor.getByLabel("Grabstätte").fill(`Schineller Bäcke ${graveNumber}`);
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
  const marker = Date.now();
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
 * caller uses a period no other test touches, so a run never collides with the
 * generation tests above or with another test's expectations.
 */
async function generateFor(page: Page, year: number, half: number): Promise<void> {
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
 *  one 100.00 net item, count 1, at 7 %. */
async function seedGrave(
  page: Page,
  marker: string,
  recipientNames: string[],
  items: SeedGraveItem[] = [{}],
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
  const graveId = await saveGrave(page, { graveNumber: marker, items: lines, recipientIds });
  return { graveId, recipientNames };
}

test("the grave detail page shows the grave, its status and its invoices", async ({ page }) => {
  const marker = `GD-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  await page.goto(`/graves/${graveId}`);

  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
  // this grave was created after the generation tests above ran, so nothing has
  // invoiced it for the current half-year
  await expect(page.getByText(`Halbjahr H${CURRENT_HALF} ${CURRENT_YEAR}`)).toBeVisible();
  await expect(page.locator(".p-tag-danger", { hasText: "offen" })).toBeVisible();
  for (const name of names) {
    await expect(card(page, "Empfänger").getByText(name)).toBeVisible();
  }
  await expect(
    page.getByText("Für dieses Grab wurden noch keine Rechnungen erstellt."),
  ).toBeVisible();

  // a period no other test uses, so the rows below are exactly this generation
  await generateFor(page, 2039, 2);
  await page.reload();

  await expect(graveInvoiceRows(page)).toHaveCount(2);
  await expect(card(page, "Rechnungen")).toContainText("H2 2039");
  // each recipient pays half of 1 × 100.00 + 7% = 53,50
  await expect(graveInvoiceRows(page).filter({ hasText: "53,50 €" })).toHaveCount(2);

  // both documents are reachable per row
  const row = graveInvoiceRows(page).filter({ hasText: names[0] });
  await expect(row.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
  await expect(row.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
});

test("editing a grave invoice revises every recipient", async ({ page }) => {
  const marker = `GE-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

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

test("the grave list colours its status and offers the invoice mass download", async ({ page }) => {
  const marker = `GL-${Date.now()}`;
  await seedGrave(page, marker, [`Liste ${marker}`]);

  await page.goto("/graves");
  const row = page.getByRole("row").filter({ hasText: marker });
  await expect(row).toBeVisible();

  // created after the generation tests above, so this grave is still open —
  // and an open half-year reads red
  await expect(row.locator(".p-tag-danger", { hasText: "offen" })).toBeVisible();

  // the details action reaches the grave's own page
  await row.getByRole("button", { name: rowAction("Details") }).click();
  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();

  // and so does the grave's number
  await page.goto("/graves");
  await page.getByRole("link", { name: marker, exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();

  // the mass download hands back a ZIP of the half-year the overview shows
  await page.goto("/graves");
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "Rechnungen (ZIP)" }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("rechnungen.zip");
});

test("a grave invoice's detail page shows its revisions and its siblings", async ({ page }) => {
  const marker = `GI-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

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
  await expect(facts).toContainText("50,0 %");
  await expect(page.getByText(names[0], { exact: true })).toBeVisible();

  // the other recipient of the same half-year is listed, and leads to their
  // own grave-invoice detail page
  const others = card(page, "Weitere Empfänger");
  await others.getByRole("link", { name: names[1] }).click();
  await expect(page.getByText(names[1], { exact: true })).toBeVisible();
  await expect(card(page, "Weitere Empfänger").getByRole("link", { name: names[0] })).toBeVisible();

  // the breadcrumb leads back to the grave, not to the grave list
  await page
    .getByRole("navigation", { name: "Breadcrumb" })
    .getByRole("link", { name: marker })
    .click();
  await expect(page).toHaveURL(new RegExp(`/graves/${graveId}$`));
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();
});

test("the half-year status announces its meaning, not a validation error", async ({ page }) => {
  const marker = `GA-${Date.now()}`;
  await seedGrave(page, marker, [`Ansage ${marker}`]);

  await page.goto("/graves");
  const status = page.getByRole("row").filter({ hasText: marker }).locator(".p-tag").first();
  await expect(status).toHaveText("offen");

  // The red state could make a screen reader call an open half-year an invalid
  // entry; the tag carries its own announcement instead.
  await expect(status).toHaveAttribute("aria-label", "Rechnungen offen");
});

/** The H1/H2 toggle of a form: the button of this half, pressed or not. */
function halfButton(scope: Page | Locator, half: number): Locator {
  return scope.getByRole("group", { name: "Halbjahr" }).getByRole("button", { name: `H${half}` });
}

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

test("a grave with three recipients bills every one of them the identical share (2035/2)", async ({
  page,
}) => {
  const marker = `GT-${Date.now()}`;
  const names = [`Erna ${marker}`, `Otto ${marker}`, `Rosa ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [
    { description: "Grabpflege", vatRate: 7, price: 100, count: 1 },
    { description: "Blumenschmuck", vatRate: 19, price: 24.95, count: 2 },
    { description: "Grabstein reinigen", vatRate: 19, price: 47.81, count: 1 },
    { description: "Gießdienst", vatRate: 7, price: 3.7, count: 5 },
  ]);
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
  const marker = `GV-${Date.now()}`;
  const names = [`Vera ${marker}`, `Walter ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [
    { vatRate: 7, price: 117.02, priceIncludesVat: true },
  ]);
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

test("the grave detail page shows this half-year's billed price once it is today's", async ({
  page,
}) => {
  const marker = `GC-${Date.now()}`;
  const names = [`Uwe ${marker}`, `Vroni ${marker}`, `Wanda ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [
    { description: "Grabpflege", vatRate: 7, price: 100, count: 1 },
    { description: "Blumenschmuck", vatRate: 19, price: 24.95, count: 2 },
    { description: "Grabstein reinigen", vatRate: 19, price: 47.81, count: 1 },
    { description: "Gießdienst", vatRate: 7, price: 3.7, count: 5 },
  ]);

  // The file's first test already invoiced most graves for the current
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

test("the grave's item table shows the price as entered, its rate and the flag", async ({
  page,
}) => {
  const marker = `GK-${Date.now()}`;
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
  const marker = `GR-${Date.now()}`;
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
  const marker = `GW-${Date.now()}`;
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
  // a grave that divides as it is, to be hidden by the overview filter
  const plainMarker = `GP-${Date.now()}`;
  await seedGrave(page, plainMarker, [`Dora ${plainMarker}`]);

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

  // the overview's switch keeps the graves with corrections
  await expect(page.getByRole("table")).toContainText(marker);
  // both graves are listed before the switch is on, so the absence below is the filter's
  await expect(page.getByRole("row").filter({ hasText: plainMarker })).toHaveCount(1);
  await page.getByLabel("Nur mit Rundungsanpassungen").check();
  const row = page.getByRole("row").filter({ hasText: marker });
  await expect(row).toHaveCount(1);
  await expect(row).toContainText("angepasst");
  await expect(page.getByRole("row").filter({ hasText: plainMarker })).toHaveCount(0);
});

test("the grave editor previews a prorated start and a seasonal item", async ({ page }) => {
  const marker = `GQ-${Date.now()}`;
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
  const marker = `GY-${Date.now()}`;
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
  const marker = `GC-${Date.now()}`;
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

test("a half-year reads partly paid until every recipient has paid (Story 6)", async ({ page }) => {
  // the file's first test has already generated this half-year's invoices;
  // this one only marks them, so it starts no job of its own. The grave is the
  // one seedGraveGenerationData gave two recipients, named directly rather
  // than found by scanning every grave invoiced for the current half-year.
  const graveNumber = `G-${RUN}-${String(GRAVE_GENERATION_COUNT).padStart(3, "0")}`;
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`)
  ).json();
  const invoices = await (
    await page.request.get(
      `/odata/Invoices?$filter=GraveId eq ${graves.value[0].Id} and PeriodYear eq ${CURRENT_YEAR}` +
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
  await pickOption(page, page.getByRole("combobox", { name: "Zahlung" }), "Nur unbezahlte");
  await expect(page.getByRole("row").filter({ hasText: graveNumber })).toHaveCount(0);
});

test("the overview searches graves by recipient name as well", async ({ page }) => {
  const marker = `GS-${Date.now()}`;
  await seedGrave(page, marker, [`Suchname ${marker}`]);

  await page.goto("/graves");
  await search(
    page,
    page.getByRole("textbox", { name: "Nach Grabnummer oder Empfänger suchen" }),
    `Suchname ${marker}`,
    (url) => url.includes(`/odata/SearchGraves?term=Suchname ${marker}`),
  );
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.locator("tbody tr")).toContainText(marker);
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
