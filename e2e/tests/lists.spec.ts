import { readFile } from "node:fs/promises";
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

test("the care list downloads as a PDF", async ({ page }) => {
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
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Pflegeliste als PDF herunterladen" }).click();
  const file = await download;

  expect(file.suggestedFilename()).toBe(`Pflegeliste-${YEAR}.pdf`);
  const content = await readFile(await file.path());
  expect(content.subarray(0, 4).toString()).toBe("%PDF");
});

test("the planting list downloads as a PDF", async ({ page }) => {
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
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Pflanzliste als PDF herunterladen" }).click();
  const file = await download;

  expect(file.suggestedFilename()).toBe(`Pflanzliste-${YEAR}-${QUARTER.file}.pdf`);
  const content = await readFile(await file.path());
  expect(content.subarray(0, 4).toString()).toBe("%PDF");
});

test("a list with nothing on it says so", async ({ page }) => {
  await page.goto("/lists");

  // 2000, not a year far out: graves without an end run on every later year
  await fillYear(page, page.locator("#careListYear"), "2000");
  await page.getByRole("button", { name: "Pflegeliste als PDF herunterladen" }).click();
  await expect(toasts(page).filter({ hasText: "Für 2000 gibt es keine Gräber." })).toBeVisible();

  await fillYear(page, page.locator("#plantingListYear"), "2000");
  await page
    .getByRole("group", { name: "Quartal" })
    .getByRole("button", { name: "Frühling" })
    .click();
  await page.getByRole("button", { name: "Pflanzliste als PDF herunterladen" }).click();
  await expect(
    toasts(page).filter({
      hasText: "Für Frühling 2000 gibt es keine Gräber mit Zusatzleistungen.",
    }),
  ).toBeVisible();
});
