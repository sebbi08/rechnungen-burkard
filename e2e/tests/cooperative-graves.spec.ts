import { expect, test } from "@playwright/test";
import { createRow, ensureCemetery, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";
import {
  drawer,
  fillDate,
  filterColumn,
  pickOption,
  saveEditor,
  searchColumn,
  waitForOverlayOpen,
} from "../vue";

// The page lists the cooperative graves and nothing else. Every test seeds
// graves of its own and narrows the list to them by their number's marker, so
// that the graves other tests leave behind do not matter.

/** Seeds a cooperative grave with no items, as the editor saves one. */
async function seedCooperative(
  page: Parameters<typeof seedGrave>[0],
  graveNumber: string,
  rest: Partial<Parameters<typeof seedGrave>[1]> = {},
): Promise<number> {
  return seedGrave(page, {
    graveNumber,
    items: [],
    recipientIds: [],
    isCooperative: true,
    ...rest,
  });
}

test("the list shows active cooperative graves by default", async ({ page }) => {
  const marker = `GG-${unique()}`;
  await seedCooperative(page, `${marker}-active`, { startDate: "2025-01", endDate: null });
  await seedCooperative(page, `${marker}-ended`, { startDate: "2024-01", endDate: "2025-12" });
  // a grave of the business is no cooperative one
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Geschäft ${marker}`,
    Address: "Weg 1",
  });
  await seedGrave(page, {
    graveNumber: `${marker}-business`,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/cooperative-graves");
  await expect(
    page.getByRole("heading", { level: 1, name: "Genossenschaftsgräber" }),
  ).toBeVisible();
  await searchColumn(page, "Grabnummer", marker);
  const rows = page.locator("tbody tr");
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-active`);

  const term = page.getByRole("combobox", { name: "Laufzeit" });
  await pickOption(page, term, "Alle");
  await expect(rows).toHaveCount(2);

  await pickOption(page, term, "Beendet");
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-ended`);

  // the term is kept in the URL, like the column filters
  await expect(page).toHaveURL(/term=ended/);
});

test("the list filters by column", async ({ page }) => {
  const marker = `GF-${unique()}`;
  const cemetery = `Friedhof ${marker}`;
  const cemeteryId = await ensureCemetery(page, cemetery);
  const otherCemeteryId = await ensureCemetery(page);
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Genossen ${marker}`,
    Address: "Musterweg 5\n12345 Musterstadt",
  });
  await seedCooperative(page, `${marker}-a`, {
    fieldNumber: `F${marker}`,
    siteName: `Nord ${marker}`,
    cemeteryId,
    recipientIds: [recipient.Id],
    endDate: null,
  });
  await seedCooperative(page, `${marker}-b`, {
    fieldNumber: "9",
    siteName: "Süd",
    cemeteryId: otherCemeteryId,
    endDate: null,
  });

  await page.goto("/cooperative-graves");
  const rows = page.locator("tbody tr");
  await searchColumn(page, "Grabnummer", marker);
  await expect(rows).toHaveCount(2);

  await searchColumn(page, "Grabnummer", `${marker}-a`);
  await expect(rows).toHaveCount(1);
  await searchColumn(page, "Grabnummer", marker);
  await expect(rows).toHaveCount(2);

  await searchColumn(page, "Grabstätte", `nord ${marker}`);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-a`);
  await searchColumn(page, "Grabstätte", "");

  await searchColumn(page, "Feld-Nr.", `F${marker}`);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-a`);
  await searchColumn(page, "Feld-Nr.", "");

  await searchColumn(page, "Empfänger", `musterweg 5`);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-a`);
  await searchColumn(page, "Empfänger", "");

  await filterColumn(page, "Friedhof", cemetery);
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText(`${marker}-a`);
});

test("a new cooperative grave starts with the box ticked", async ({ page }) => {
  const marker = `GN-${unique()}`;
  const cemetery = await ensureCemetery(page);
  expect(cemetery).toBeGreaterThan(0);
  await page.goto("/cooperative-graves");
  await page.getByRole("button", { name: "Neues Genossenschaftsgrab" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor.getByRole("checkbox", { name: "Genossenschaftsgrab" })).toBeChecked();

  await editor.getByLabel("Grabnummer").fill(marker);
  await editor.getByLabel("Feld-Nr.").fill("27");
  await editor.getByLabel("Grabstätte").fill(`Schineller Bäcke ${marker}`);
  await pickOption(page, page.locator("#graveCemetery"), "Bergfriedhof", {
    filter: "Bergfriedhof",
  });
  await pickOption(page, page.locator("#gravePosition"), "Doppelgrab außen");
  await fillDate(page, editor.getByLabel("Beginn"), "01/2026");
  await saveEditor(editor);

  await searchColumn(page, "Grabnummer", marker);
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(page.locator("tbody tr")).toContainText(marker);
});

test("a row opens the grave's detail page", async ({ page }) => {
  const marker = `GD-${unique()}`;
  await seedCooperative(page, marker, { endDate: null });
  await page.goto("/cooperative-graves");
  await searchColumn(page, "Grabnummer", marker);
  await page.locator("tbody tr").getByRole("link", { name: marker }).click();
  await expect(page.getByRole("heading", { level: 1, name: marker })).toBeVisible();
  await expect(page).toHaveURL(/\/graves\/\d+$/);
});
