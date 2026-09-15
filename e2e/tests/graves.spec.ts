import { expect, type Locator, type Page, test } from "@playwright/test";
import { pickSelectOption, waitForDialogOpen } from "../dialog";
import { createRow, ensureRow, seedGrave as saveGrave, type SeedGraveLine } from "../seed";

// Unique per test run. `npm test` starts with an empty database, but
// `npm run test:dev` runs against the dev stack, where fixed numbers would
// collide with pre-existing rows or make assertions vacuous.
const RUN = Date.now();

// The overview toolbar and the generate dialog both open on the half-year today
// falls in, so a generation started without further input lands here. Derived
// from the same clock the app reads, not hardcoded, or every January would turn
// this file red.
const CURRENT_YEAR = new Date().getFullYear();
const CURRENT_HALF = new Date().getMonth() < 6 ? 1 : 2;

// How many graves the generation test below seeds. Shared so the payment test
// further down can name the exact grave `seedGraveGenerationData` gives two
// recipients (`G-${RUN}-<this, zero-padded>`) instead of searching across
// every grave invoiced for the current half-year: a full-suite run also
// invoices other spec files' graves for this same period, and at least one of
// them (import-export.spec.ts's `IMP-G1-*`) has two recipients too, so an
// unscoped search could pick their grave instead of this run's own.
const GRAVE_GENERATION_COUNT = 60;

/**
 * One field's suggestion rows. The item and recipient fields of the grave
 * dialog are both ComboBoxes and both leave their list in the DOM after a pick,
 * so naming the field keeps the two apart — a ComboBox renders its list as
 * `<control id>-popup-list`. The item field sits in a table template and its id
 * therefore carries a clone suffix, which is why the field name is matched
 * anywhere in the id.
 */
function suggestions(page: Page, field: string): Locator {
  return page.locator(`[id*='${field}'][id$='-popup-list'] li.sapMSLI`);
}

async function seedGraveGenerationData(page: Page, count: number): Promise<string> {
  // VatRate 7 may already exist from previous runs; a 409 here is harmless.
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
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
  // recipient is what makes "teilweise bezahlt" observable at all, and none
  // of the single-recipient graves below produce that
  const secondRecipient = await createRow(page, "/odata/Recipients", {
    Name: `Clara Beispiel ${RUN}`,
    Address: "Seitenweg 2",
  });

  const firstGraveNumber = `G-${RUN}-001`;
  for (let i = 1; i <= count; i++) {
    await saveGrave(page, {
      graveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
      items: [{ itemId: item.Id, count: 1 }],
      // the last grave only: the payment test below names it directly by its
      // GraveNumber (G-${RUN}-<count>), so which one gets the second
      // recipient is arbitrary — this just keeps it to a single, fixed grave
      recipientIds: i === count ? [recipient.Id, secondRecipient.Id] : [recipient.Id],
    });
  }
  return firstGraveNumber;
}

test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({
  page,
}) => {
  const firstGraveNumber = await seedGraveGenerationData(page, GRAVE_GENERATION_COUNT);
  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  await expect(gravesTable).toContainText(firstGraveNumber);

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForDialogOpen(page);
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();

  // blocking progress indicator is visible while the job runs
  await expect(page.locator(".sapMBusyDialog")).toBeVisible({ timeout: 5000 });
  await expect(page.locator(".sapMBusyDialog")).not.toBeVisible({
    timeout: 90000,
  });

  // status column now shows graves as invoiced
  await expect(gravesTable.getByText("erstellt").first()).toBeVisible();
  await expect(gravesTable.locator(".sapMObjStatus.sapMObjStatusSuccess").first()).toBeVisible();

  // dismiss the success message, then run the second generation
  await page.locator(".sapMDialog").getByRole("button", { name: "OK" }).click();
  expect(await periodInvoices(page)).not.toHaveLength(0);

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForDialogOpen(page);
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();
  // the busy dialog only appears when a job actually runs (409 case: not at all)
  await page
    .locator(".sapMBusyDialog")
    .waitFor({ state: "visible", timeout: 3000 })
    .catch(() => {});
  await page
    .locator(".sapMBusyDialog")
    .waitFor({ state: "hidden", timeout: 90000 })
    .catch(() => {});
  // either the German "already invoiced" message (when every grave is invoiced)
  // or the result of a second run
  const resultBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(resultBox).toBeVisible();
  const resultText = await resultBox.innerText();
  expect(resultText.includes("bereits") || resultText.includes("Erstellt")).toBeTruthy();
  await page.locator(".sapMDialog").getByRole("button").first().click();

  // FR-027: no grave and recipient may end up with two invoices for the same
  // half-year. Asserting the pairs rather than a total keeps this immune to the
  // graves other specs create in parallel, which a second run may legitimately
  // invoice for the first time.
  const pairs = await periodInvoices(page);
  const seen = pairs.map((i) => `${i.GraveId}:${i.RecipientId}`);
  expect(new Set(seen).size).toBe(seen.length);
});

interface PeriodInvoice {
  GraveId: number;
  RecipientId: number;
}

/** Every grave invoice of the half-year the generation above ran for, paged
 *  past the service's 200-row limit so the duplicate check sees all of them. */
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

test("create a grave with item, price and recipient in one save (Story 5)", async ({ page }) => {
  const itemNumber = `8002-${RUN}`;
  const recipientName = `Bert Beispiel ${RUN}`;
  const graveNumber = `X-${RUN}`;

  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("/odata/InvoiceItems", {
    data: {
      Number: itemNumber,
      Description: "Pflege",
      VatRate: 7,
      DefaultPrice: 50,
    },
  });
  await page.request.post("/odata/Recipients", {
    data: { Name: recipientName, Address: "Nebenweg 2" },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForDialogOpen(page);
  // scope to the dialog: the toolbar search field's accessible name also
  // contains "Grabnummer" and would win the race while the dialog loads
  const graveDialog = page.locator(".sapMDialog");
  await graveDialog.getByLabel("Grabnummer").fill(graveNumber);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");

  // the grave does not exist yet: item and recipient are collected in the dialog
  await page.getByRole("button", { name: "Zeile hinzufügen" }).click();
  const itemInput = page.locator(".sapMDialog input[id*='graveItem-'][id$='-inner']");
  await itemInput.click();
  await itemInput.fill(itemNumber);
  await suggestions(page, "graveItem").filter({ hasText: itemNumber }).click();
  // selecting the item pre-fills its default price, as the invoice dialog does;
  // typing another one overrides it for this grave
  await expect(graveDialog.getByLabel("Preis")).toHaveValue("50");
  await graveDialog.getByLabel("Anzahl").fill("2");
  await graveDialog.getByLabel("Preis").fill("55");

  // the recipient field is the same typeahead as the item field above
  const recipientInput = page.locator(".sapMDialog input[id$='--linkRecipient-inner']");
  await recipientInput.click();
  await recipientInput.fill(recipientName);
  await suggestions(page, "linkRecipient").filter({ hasText: recipientName }).click();
  // the pick lands after the click returns: the popover closes and focus goes
  // back to the field. "Hinzufügen" reads the draft, so wait for it to settle
  await expect(recipientInput).toHaveValue(recipientName);
  await expect(page.locator("[id*='linkRecipient'][id$='-popup-list']")).toBeHidden();
  // "Hinzufügen" alone also matches the item table's "Zeile hinzufügen"
  await page.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  await expect(page.locator("[id$='graveRecipientsTable-listUl']")).toContainText(recipientName);

  // nothing has been persisted so far — one save writes the grave and both links
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(gravesTable).toContainText(graveNumber);

  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`)
  ).json();
  const graveId = graves.value[0].Id;
  const links = await (
    await page.request.get(`/odata/GraveItems?$filter=GraveId eq ${graveId}`)
  ).json();
  expect(links.value).toHaveLength(1);
  expect(Number(links.value[0].Count)).toBe(2);
  expect(Number(links.value[0].UnitPrice)).toBe(55);
  const recipientLinks = await (
    await page.request.get(`/odata/GraveRecipients?$filter=GraveId eq ${graveId}`)
  ).json();
  expect(recipientLinks.value).toHaveLength(1);

  // reopening the grave shows the saved count and price again
  await gravesTable
    .locator("tbody tr", { hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForDialogOpen(page);
  await expect(graveDialog.getByLabel("Anzahl")).toHaveValue("2");
  await expect(graveDialog.getByLabel("Preis")).toHaveValue("55");
  // the dialog is built once by loadFragment and reused: the first open above
  // was in create mode (Id === null, warnings hidden, recipient controls
  // shown), and this is the same fragment instance reopened in edit mode
  // (Id !== null) with a fresh graveDraft model. Asserting the edit-mode
  // state here — not just in a fresh-page test — proves the visibility
  // bindings re-evaluate when the draft model is replaced, not merely on
  // first render.
  await expect(graveDialog).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(graveDialog).toContainText("Empfänger eines Grabs");
  // scoped to the recipients section: the Items panel has its own always-visible
  // "Zeile hinzufügen" (contains "Hinzufügen" as a substring) and "Löschen" controls
  const recipientsTable = graveDialog.locator("[id$='graveRecipientsTable']");
  const recipientsLink = graveDialog.locator("[id$='graveRecipientsLink']");
  await expect(recipientsLink.getByRole("button", { name: "Hinzufügen" })).toBeHidden();
  await expect(recipientsTable.getByRole("button", { name: "Löschen" })).toBeHidden();
  await page.getByRole("button", { name: "Abbrechen" }).click();
});

test("an item without a default price needs an explicit price (Story 6)", async ({ page }) => {
  const noPriceItemNumber = `NP-${RUN}`;
  const graveNumber = `NPG-${RUN}`;
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("/odata/InvoiceItems", {
    data: {
      Number: noPriceItemNumber,
      Description: "Ohne Preis",
      VatRate: 7,
      DefaultPrice: null,
    },
  });
  // a grave needs a recipient before it can be saved at all, so the refusal
  // below is about the price alone; created before the page loads its list
  const recipientName = `Preis Beispiel ${RUN}`;
  await createRow(page, "/odata/Recipients", { Name: recipientName, Address: "Preisweg 1" });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const graveDialog = page.locator(".sapMDialog");

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForDialogOpen(page);
  await graveDialog.getByLabel("Grabnummer").fill(graveNumber);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");
  await page.getByRole("button", { name: "Zeile hinzufügen" }).click();
  const itemInput = page.locator(".sapMDialog input[id*='graveItem-'][id$='-inner']");
  await itemInput.click();
  await itemInput.fill(noPriceItemNumber);
  await suggestions(page, "graveItem").filter({ hasText: noPriceItemNumber }).click();

  const recipientInput = page.locator(".sapMDialog input[id$='--linkRecipient-inner']");
  await recipientInput.click();
  await recipientInput.fill(recipientName);
  await suggestions(page, "linkRecipient").filter({ hasText: recipientName }).click();
  await expect(recipientInput).toHaveValue(recipientName);
  await expect(page.locator("[id*='linkRecipient'][id$='-popup-list']")).toBeHidden();
  await page.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  await expect(page.locator("[id$='graveRecipientsTable-listUl']")).toContainText(recipientName);

  // no price anywhere: saving is rejected with the German "missing price" message
  await page.getByRole("button", { name: "Speichern" }).click();
  const errorBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(errorBox).toContainText("fehlt ein Preis");
  await errorBox.getByRole("button").first().click();
  await expect(gravesTable).not.toContainText(graveNumber);

  // entering the price in the dialog makes the same grave saveable
  await graveDialog.getByLabel("Preis").fill("40");
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(gravesTable).toContainText(graveNumber);

  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`)
  ).json();
  const links = await (
    await page.request.get(`/odata/GraveItems?$filter=GraveId eq ${graves.value[0].Id}`)
  ).json();
  expect(Number(links.value[0].UnitPrice)).toBe(40);
});

// No ItemWithoutPrice skip test here: SaveGrave refuses an unpriced item, so nothing can build
// that grave any more; the backend generation tests seed it into the database directly.

test("editing a grave warns about future invoices and freezes its recipients", async ({ page }) => {
  const marker = Date.now();
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
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

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const row = gravesTable.locator("tbody tr", { hasText: `F-${marker}` });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForDialogOpen(page);

  const dialog = page.locator("[id$='graveDialog']");
  await expect(dialog).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(dialog).toContainText("Empfänger eines Grabs");
  // the recipient stays visible but cannot be removed or added to; the item
  // table has its own "Löschen"/"Hinzufügen" controls (always visible), so
  // these are scoped to the recipients section specifically
  await expect(dialog.getByText(`Fest ${marker}`)).toBeVisible();
  const recipientsTable = dialog.locator("[id$='graveRecipientsTable']");
  const recipientsLink = dialog.locator("[id$='graveRecipientsLink']");
  await expect(recipientsLink.getByRole("button", { name: "Hinzufügen" })).toBeHidden();
  await expect(recipientsTable.getByRole("button", { name: "Löschen" })).toBeHidden();
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

/** One item line `seedGrave` links to the grave. Every field defaults to what
 *  every caller needed before `seedGrave` took a list of these rather than one. */
interface SeedGraveItem {
  description?: string;
  vatRate?: number;
  price?: number;
  priceIncludesVat?: boolean;
  count?: number;
}

/** A grave with the given items and recipients, unique per run. Defaults to
 *  one 100.00 net item, count 1, at 7 %, matching every caller from before
 *  `seedGrave` took a list of items rather than one. */
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
    // a single item keeps the old, suffix-free number; several get one each
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
  // the grave comes last and whole, as the dialog saves it — lines and
  // recipients in one request, which is where the split check runs
  const graveId = await saveGrave(page, { graveNumber: marker, items: lines, recipientIds });
  return { graveId, recipientNames };
}

test("the grave detail page shows the grave, its status and its invoices", async ({ page }) => {
  const marker = `GD-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  await page.goto(`/index.html#/graves/${graveId}`);

  await expect(page.getByText(marker, { exact: true })).toBeVisible();
  // this grave was created after the generation tests above ran, so nothing has
  // invoiced it for the current half-year
  await expect(page.getByText("offen").first()).toBeVisible();
  for (const name of names) {
    await expect(page.getByText(name)).toBeVisible();
  }
  await expect(page.getByText("Für dieses Grab wurden noch keine")).toBeVisible();

  // a period no other test uses, so the rows below are exactly this generation
  await generateFor(page, 2039, 2);
  await page.reload();

  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable).toContainText("H2 2039");
  // each recipient pays half of 1 × 100.00 + 7% = 53,50
  await expect(invoicesTable).toContainText("53,50 €");

  // both documents are reachable per row
  const row = invoicesTable.locator("tbody tr", { hasText: names[0] });
  await expect(row.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
  await expect(row.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
});

test("editing a grave invoice revises every recipient", async ({ page }) => {
  const marker = `GE-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2038, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);

  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForDialogOpen(page);

  // the dialog names the half-year and both recipients it is about to revise
  const dialog = page.locator("[id$='graveInvoiceDialog']");
  await expect(dialog).toContainText("H2 2038");
  await expect(dialog).toContainText(names[0]);
  await expect(dialog).toContainText(names[1]);
  // it opens on the grave's price, not on the 50.00 share each recipient pays
  await expect(dialog.getByLabel("Preis")).toHaveValue("100");

  // A layout check, which normally belongs to layout.spec.ts — but reaching this
  // dialog needs a generated grave invoice, and GenerateGraveInvoices runs over
  // every grave at once and refuses a second concurrent job, so generation stays
  // in this file. The item's number field used to be a fixed 10rem inside a
  // column that shares the row evenly with four others: 75px on a tablet, and
  // still too narrow to hold a long number here on the desktop this test runs
  // at. It has to be about as wide as its 16rem column, bar the arrow.
  const numberField = dialog.locator("input[id*='graveInvoiceItem-'][id$='-inner']").first();
  const rem = await page.evaluate(() =>
    parseFloat(getComputedStyle(document.documentElement).fontSize),
  );
  const numberWidth = await numberField.evaluate((el) => el.getBoundingClientRect().width);
  expect(numberWidth).toBeGreaterThan(12 * rem);

  await dialog.getByLabel("Preis").fill("200");
  await dialog.getByRole("button", { name: "Speichern" }).click();

  // both recipients moved to R1, each at half of 1 × 200.00 + 7% = 107,00 —
  // asserted per row, not just "at least one", so a revision that reached
  // only one recipient would fail this
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable.locator("tbody tr", { hasText: "R1" })).toHaveCount(2);
  await expect(invoicesTable.locator("tbody tr", { hasText: "107,00 €" })).toHaveCount(2);
});

test("the grave list colours its status and offers the invoice mass download", async ({ page }) => {
  const marker = `GL-${Date.now()}`;
  await seedGrave(page, marker, [`Liste ${marker}`]);

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const row = gravesTable.locator("tbody tr", { hasText: marker });
  await expect(row).toBeVisible();

  // created after the generation tests above, so this grave is still open —
  // and an open half-year reads red
  await expect(row.locator(".sapMObjStatus.sapMObjStatusError")).toBeVisible();

  // the details action reaches the grave's own page. sap.m.NavContainer keeps
  // the graves list page parked in the DOM behind the detail page, so its row
  // still carries the same marker text — scope to the detail page itself
  // rather than the whole document, or the marker would match twice.
  await row.getByRole("button", { name: "Details", exact: true }).click();
  const detailPage = page.locator("[id$='graveDetailPage']");
  await expect(detailPage.getByText(marker, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();

  // the mass download hands back a ZIP of the half-year the toolbar shows
  await page.goto("/index.html#/graves");
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

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Details", exact: true })
    .click();

  // sap.m.NavContainer keeps the grave detail page parked in the DOM behind
  // this one, so an unscoped assertion could match text on either page
  const detailPage = page.locator("[id$='graveInvoiceDetailPage']");
  await expect(detailPage.getByText("Original", { exact: true })).toBeVisible();
  await expect(detailPage.getByText(marker, { exact: true })).toBeVisible();
  await expect(detailPage.getByText("H2 2037")).toBeVisible();
  // the other recipient of the same half-year is listed
  await expect(detailPage.getByText(names[1])).toBeVisible();

  // clicking that sibling opens their own grave-invoice detail page — this is
  // the normal navigation path (as opposed to pasting a URL) and was never
  // exercised before, so a route param regression (e.g. passing an id the
  // route's RootId filter can't match) would have gone unnoticed
  await detailPage.locator(".sapMSLI", { hasText: names[1] }).click();
  await expect(detailPage.getByText(names[1], { exact: true })).toBeVisible();
  await expect(detailPage.getByText(names[0])).toBeVisible();

  // back navigation returns to the grave, not to the home screen — via the
  // page's own nav-back button, not a positional lookup (the header also
  // carries an edit button)
  await page.locator("[id$='graveInvoiceDetailPage-navButton']").click();
  await expect(page.getByRole("button", { name: "Grab bearbeiten" })).toBeVisible();
});

test("the half-year status announces its meaning, not a validation error", async ({ page }) => {
  const marker = `GA-${Date.now()}`;
  await seedGrave(page, marker, [`Ansage ${marker}`]);

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const status = gravesTable.locator("tbody tr", { hasText: marker }).locator(".sapMObjStatus");
  await expect(status).toBeVisible();

  // The red state otherwise makes a screen reader call an open half-year an
  // invalid entry. The status carries its own announcement instead, which lives
  // in a hidden span, so this reads the markup rather than the visible text.
  const markup = await status.innerHTML();
  expect(markup).toContain("Rechnungen offen");
});

test("overview and generate dialog open on the half-year today falls in", async ({ page }) => {
  await page.goto("/index.html#/graves");

  await expect(page.locator(".sapMSlt[id$='halfYearSelect'] .sapMSltLabel")).toHaveText(
    `H${CURRENT_HALF} ${CURRENT_YEAR}`,
  );

  // the two are asserted together on purpose: a dialog defaulting to another
  // half-year still generates successfully, but leaves the status column the
  // toolbar is showing red, which reads as a failed run
  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await waitForDialogOpen(page);
  const dialog = page.locator(".sapMDialog");
  await expect(dialog.locator(".sapMSlt[id$='genYear'] .sapMSltLabel")).toHaveText(
    String(CURRENT_YEAR),
  );
  await expect(dialog.locator(".sapMSlt[id$='genHalf'] .sapMSltLabel")).toHaveText(
    `H${CURRENT_HALF}`,
  );
  await page.getByRole("button", { name: "Abbrechen" }).click();
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

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(3);

  // the 7 % subtotal (100,00 + 18,50 = 118,50) divides evenly by three, but the
  // 19 % one (49,90 + 47,80 = 97,70) does not — a cent goes onto the cheapest,
  // count-1 line (Grabstein reinigen) to make it 97,71. Each recipient then
  // pays 39,50 net at 7 % and 32,57 net at 19 %: 72,07 net, VAT 2,77 + 6,19 =
  // 8,96, total 81,03 — asserted per row, not just "at least one", so a split
  // that shortchanged even one recipient would fail this
  await expect(invoicesTable.locator("tbody tr", { hasText: "81,03 €" })).toHaveCount(3);
});

test("a grave splits a VAT-included price without taking its VAT out twice", async ({ page }) => {
  const marker = `GV-${Date.now()}`;
  const names = [`Vera ${marker}`, `Walter ${marker}`];
  const { graveId } = await seedGrave(page, marker, names, [
    { vatRate: 7, price: 117.02, priceIncludesVat: true },
  ]);
  await generateFor(page, 2036, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  // the grave's own item list shows the gross and says so — the incl.-VAT flag
  // has its own column now rather than a hint under the price
  const careRow = page.locator("tbody tr", { hasText: "Grabpflege" }).first();
  await expect(careRow).toContainText("Ja");
  // 117,02 gross at 7 % is a net of 109,36, whose 10 936 cents divide evenly by
  // two. The page shows the price exactly as it was typed: a grave whose prices
  // could not be split could not have been saved, so nothing here is adjusted.
  await expect(careRow).toContainText("117,02 €");

  // 54,68 net and 58,51 gross each — 117,02 together, the price that was
  // entered
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr", { hasText: "58,51 €" })).toHaveCount(2);

  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForDialogOpen(page);

  // the dialog opens on the grave's gross — not on the 109,36 net it stores,
  // and not on either recipient's share
  const dialog = page.locator("[id$='graveInvoiceDialog']");
  await expect(dialog.getByLabel("Preis")).toHaveValue("117.02");
  await expect(dialog).toContainText("inkl. MwSt.");
  await expect(dialog.getByRole("checkbox")).toBeChecked();

  // saved back untouched, every share stays where it was
  await dialog.getByRole("button", { name: "Speichern" }).click();
  await expect(invoicesTable.locator("tbody tr", { hasText: "58,51 €" })).toHaveCount(2);
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

  // The file's first test already invoiced most graves for CURRENT_YEAR/
  // CURRENT_HALF, but GenerateGraveInvoices only refuses a request once
  // *every* grave has a run for that period — this grave, seeded just above,
  // does not, so the request is accepted and the job invoices only the graves
  // still missing a run, this one included.
  await generateFor(page, CURRENT_YEAR, CURRENT_HALF);

  await page.goto(`/index.html#/graves/${graveId}`);
  // The grave detail page has no half-year selector: it always shows what was
  // billed for the half-year today falls in. The 19 % subtotal is 49,90 + 47,81
  // = 97,71, which divides by three; the page shows the grave's own price,
  // unadjusted, because nothing adjusts prices any more — a grave that did not
  // divide could not have been saved.
  const stoneRow = page.locator("tbody tr", { hasText: "Grabstein reinigen" }).first();
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

  await page.goto(`/index.html#/graves/${graveId}`);
  const row = page.locator("tbody tr", { hasText: "Grabpflege" }).first();

  // the price exactly as it was typed, with what qualifies it beside it. No net
  // or gross totals: a grave's page is about what was agreed, and those figures
  // belong to the invoices listed beneath it.
  await expect(row).toContainText("100,00 €");
  await expect(row).toContainText("19");
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
  // beside its own "7 %", which is the bug this pins.
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
  await page.goto(`/index.html#/graves/${graveId}/invoices/${invoices.value[0].RootId}`);

  // the lines are the whole grave, so the gross column is the grave's: 300,00
  // at 7 % is 321,00, and 99,00 at 19 % is 117,81
  await expect(page.locator("tbody tr", { hasText: "Grabpflege" }).first()).toContainText(
    "321,00 €",
  );
  await expect(page.locator("tbody tr", { hasText: "Blumenschmuck" }).first()).toContainText(
    "117,81 €",
  );
});

test("a grave whose prices do not divide is refused until a price is changed", async ({ page }) => {
  const marker = `GW-${Date.now()}`;
  // 47,80 at 19 % over three recipients: its 4 780 net cents leave a remainder,
  // so the three of them cannot be billed the same amount. Nothing raises the
  // price to make it fit — the save is refused and names what would work.
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

  await page.goto("/index.html#/graves");
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForDialogOpen(page);

  const graveDialog = page.locator(".sapMDialog");
  await graveDialog.getByLabel("Grabnummer").fill(marker);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");

  await page.getByRole("button", { name: "Zeile hinzufügen" }).click();
  const itemInput = page.locator(".sapMDialog input[id*='graveItem-'][id$='-inner']");
  await itemInput.click();
  await itemInput.fill(`${marker}-item`);
  await suggestions(page, "graveItem")
    .filter({ hasText: `${marker}-item` })
    .click();
  await expect(graveDialog.getByLabel("Preis")).toHaveValue("47.8");

  for (const name of names) {
    const recipientInput = page.locator(".sapMDialog input[id$='--linkRecipient-inner']");
    await recipientInput.click();
    await recipientInput.fill(name);
    await suggestions(page, "linkRecipient").filter({ hasText: name }).click();
    await expect(recipientInput).toHaveValue(name);
    await expect(page.locator("[id*='linkRecipient'][id$='-popup-list']")).toBeHidden();
    await page.getByRole("button", { name: "Hinzufügen", exact: true }).click();
    await expect(page.locator("[id$='graveRecipientsTable-listUl']")).toContainText(name);
  }

  await page.getByRole("button", { name: "Speichern" }).click();

  // refused, naming the rate and the nearest subtotals that would divide
  const refusal = page.locator(".sapMMessageBox");
  await expect(refusal).toContainText("47,80");
  await expect(refusal).toContainText("47,79");
  await expect(refusal).toContainText("47,82");
  // MessageBox.error closes with "Schließen", not "OK"
  await refusal.getByRole("button").first().click();

  // the dialog is still open with the draft intact, so the price can be fixed
  await expect(graveDialog.getByLabel("Grabnummer")).toHaveValue(marker);
  await graveDialog.getByLabel("Preis").fill("47.79");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(page.locator("[id$='gravesTable-listUl']")).toContainText(marker);
});

test("a half-year reads partly paid until every recipient has paid (Story 6)", async ({ page }) => {
  // the file's earlier tests have already generated this half-year's invoices;
  // this one only marks them, so it starts no job of its own
  await page.goto("/index.html#/graves");

  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  // the grave seedGraveGenerationData gave two recipients, named directly by
  // its GraveNumber rather than found by scanning every grave invoiced for
  // the current half-year: a full-suite run also invoices other spec files'
  // graves for this same period (a generation sweeps up every grave in the
  // stack), and at least one of them is billed to more than one recipient
  // too, so an unscoped search could resolve to their grave instead of ours
  const graveNumber = `G-${RUN}-${String(GRAVE_GENERATION_COUNT).padStart(3, "0")}`;
  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id,GraveNumber`,
    )
  ).json();
  const grave = graves.value[0];
  const invoices = await (
    await page.request.get(
      `/odata/Invoices?$filter=GraveId eq ${grave.Id} and PeriodYear eq ${CURRENT_YEAR}` +
        ` and PeriodHalf eq ${CURRENT_HALF} and IsLatestRevision eq true&$orderby=Id`,
    )
  ).json();
  const invoiceIds = (invoices.value as any[]).map((invoice) => invoice.Id);
  expect(invoiceIds.length).toBeGreaterThan(1);
  const row = gravesTable.locator("tbody tr", { hasText: grave.GraveNumber }).first();

  await page.request.post("/odata/SetInvoicePaid", {
    data: { invoiceId: invoiceIds[0], paid: true },
  });
  await page.reload();
  // the count says how many of the grave's recipients have settled
  await expect(row).toContainText(`teilweise bezahlt (1 / ${invoiceIds.length})`);

  for (const invoiceId of invoiceIds.slice(1)) {
    await page.request.post("/odata/SetInvoicePaid", { data: { invoiceId, paid: true } });
  }
  await page.reload();
  // strengthened from the brief: "bezahlt" alone would also match "teilweise
  // bezahlt", so this would pass even if the last recipient's mark did nothing
  await expect(row).toContainText("bezahlt");
  await expect(row).not.toContainText("teilweise");

  // fully paid, so the unpaid filter drops it
  await pickSelectOption(page, page.locator("[id$='paymentFilter']"), "Nur unbezahlte");
  await expect(gravesTable.locator("tbody tr", { hasText: grave.GraveNumber })).toHaveCount(0);
});
