import { expect, type Page, test } from "@playwright/test";
import { createRow, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";
import { fillYear, toasts } from "../vue";

const RUN = unique();

// the clock the app reads: the quarter is the calendar one, Spring being January to March
const NOW = new Date();
const YEAR = NOW.getFullYear();
const QUARTERS = [
  { api: "Spring", label: "Frühling", file: "Fruehling" },
  { api: "Summer", label: "Sommer", file: "Sommer" },
  { api: "Autumn", label: "Herbst", file: "Herbst" },
  { api: "Winter", label: "Winter", file: "Winter" },
];
const QUARTER = QUARTERS[Math.floor(NOW.getMonth() / 3)];

async function seedRecipient(page: Page, name: string): Promise<number> {
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: name,
    Address: "Hauptstraße 1",
  });
  return recipient.Id;
}

async function seedItem(page: Page, number: string, quarter?: string): Promise<number> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: number,
    Description: quarter ? "Bepflanzung" : "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
    ...(quarter ? { Quarter: quarter } : {}),
  });
  return item.Id;
}

/** Clicks a list's button and answers the address its new tab loads. What the
 *  tab renders is up to the browser — a headless one has no PDF viewer — so
 *  the assertions are on the address and on what the server answers there. */
async function openList(page: Page, button: string, path: string): Promise<string> {
  const tabPromise = page.context().waitForEvent("page");
  const listRequest = page
    .context()
    .waitForEvent(
      "request",
      (request) => request.url().includes(path) && request.frame().page() !== page,
    );
  await page.getByRole("button", { name: button }).click();
  const tab = await tabPromise;
  const url = (await listRequest).url();
  await tab.close();
  return url;
}

/** The list at that address is a PDF, served for display under that name. */
async function expectPdfForDisplay(page: Page, url: string, fileName: string): Promise<void> {
  const response = await page.request.get(url);
  expect(response.headers()["content-type"]).toBe("application/pdf");
  const disposition = response.headers()["content-disposition"] ?? "";
  expect(disposition).toContain("inline");
  expect(disposition).toContain(fileName);
  expect((await response.body()).subarray(0, 4).toString()).toBe("%PDF");
}

test("the care list opens as a PDF in a new tab", async ({ page }) => {
  const itemId = await seedItem(page, `L1-${RUN}`);
  const recipientId = await seedRecipient(page, `Pflege Beispiel ${RUN}`);
  await seedGrave(page, {
    graveNumber: `L1-${RUN}`,
    startDate: `${YEAR}-01`,
    endDate: `${YEAR}-12`,
    items: [{ itemId }],
    recipientIds: [recipientId],
  });

  await page.goto("/lists");
  const url = await openList(page, "Pflegeliste als PDF öffnen", "/odata/CareList");

  await expectPdfForDisplay(page, url, `Pflegeliste-${YEAR}.pdf`);
});

test("the planting list opens as a PDF in a new tab", async ({ page }) => {
  const careId = await seedItem(page, `L2-${RUN}`);
  const itemId = await seedItem(page, `L2-${RUN}-seasonal`, QUARTER.api);
  const recipientId = await seedRecipient(page, `Pflanz Beispiel ${RUN}`);
  await seedGrave(page, {
    graveNumber: `L2-${RUN}`,
    startDate: `${YEAR}-01`,
    endDate: `${YEAR}-12`,
    items: [{ itemId: careId }, { itemId }],
    recipientIds: [recipientId],
  });

  await page.goto("/lists");
  const url = await openList(page, "Pflanzliste als PDF öffnen", "/odata/PlantingList");

  await expectPdfForDisplay(page, url, `Pflanzliste-${YEAR}-${QUARTER.file}.pdf`);
});

test("the chosen variant and quarter go to the server", async ({ page }) => {
  // not the current quarter, so the default would be a different request
  const other = QUARTERS[(QUARTERS.indexOf(QUARTER) + 1) % 4];
  const careId = await seedItem(page, `L3-${RUN}`);
  const itemId = await seedItem(page, `L3-${RUN}-seasonal`, other.api);
  const recipientId = await seedRecipient(page, `Auswahl Beispiel ${RUN}`);
  await seedGrave(page, {
    graveNumber: `L3-${RUN}`,
    startDate: `${YEAR}-01`,
    endDate: `${YEAR}-12`,
    items: [{ itemId: careId }, { itemId }],
    recipientIds: [recipientId],
  });

  await page.goto("/lists");
  await page
    .getByRole("group", { name: "Umfang" })
    .getByRole("button", { name: "Mit allen Leistungen" })
    .click();
  const careUrl = await openList(page, "Pflegeliste als PDF öffnen", "/odata/CareList");
  expect(careUrl).toContain("services=all");
  await expectPdfForDisplay(page, careUrl, `Pflegeliste-${YEAR}.pdf`);

  await page
    .getByRole("group", { name: "Quartal" })
    .getByRole("button", { name: other.label })
    .click();
  const plantingUrl = await openList(page, "Pflanzliste als PDF öffnen", "/odata/PlantingList");
  expect(plantingUrl).toContain(`quarter=${other.api}`);
  await expectPdfForDisplay(page, plantingUrl, `Pflanzliste-${YEAR}-${other.file}.pdf`);
});

test("a list with nothing on it says so", async ({ page }) => {
  await page.goto("/lists");

  // 2000, not a year far out: graves without an end run on every later year
  await fillYear(page, page.locator("#careListYear"), "2000");
  await page.getByRole("button", { name: "Pflegeliste als PDF öffnen" }).click();
  await expect(toasts(page).filter({ hasText: "Für 2000 gibt es keine Gräber." })).toBeVisible();
  // the tab opened for the list is closed again rather than left showing the refusal
  await expect.poll(() => page.context().pages().length).toBe(1);

  await fillYear(page, page.locator("#plantingListYear"), "2000");
  await page
    .getByRole("group", { name: "Quartal" })
    .getByRole("button", { name: "Frühling" })
    .click();
  await page.getByRole("button", { name: "Pflanzliste als PDF öffnen" }).click();
  await expect(
    toasts(page).filter({
      hasText: "Für Frühling 2000 gibt es keine Gräber mit Zusatzleistungen.",
    }),
  ).toBeVisible();
});
