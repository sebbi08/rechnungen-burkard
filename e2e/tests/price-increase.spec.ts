import { expect, type Page, type Route, test } from "@playwright/test";
import { createRow, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";
import { dialog, fillNumber, searchColumn, waitForOverlayOpen } from "../vue";

/** An all-year item at 10,10 € with a grave at the default, one at its own
 *  150,00 and one at its own 95,00 shared by two recipients. */
async function seedItem(page: Page) {
  const run = unique();
  const number = `ERH-${run}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: number,
    Description: `Erhöhung ${run}`,
    VatRate: 19,
    DefaultPrice: 10.1,
  });
  const recipients: number[] = [];
  for (let index = 1; index <= 3; index += 1) {
    const recipient = await createRow(page, "/odata/Recipients", {
      Name: `Erhöhung ${index} ${run}`,
      Address: "Musterweg 1",
    });
    recipients.push(recipient.Id as number);
  }
  const prices = [null, 150, 95];
  for (const [index, unitPrice] of prices.entries()) {
    await seedGrave(page, {
      graveNumber: `ERH-G${index}-${run}`,
      items: [{ itemId: item.Id, count: 1, unitPrice }],
      recipientIds: index === 2 ? [recipients[1], recipients[2]] : [recipients[0]],
    });
  }
  return { number, itemId: item.Id as number };
}

async function openItem(page: Page, number: string) {
  await page.goto("/items");
  // the list pages by 10 and parallel tests keep adding items: search for this one
  await searchColumn(page, "Nummer", number);
  await page
    .getByRole("row")
    .filter({ hasText: number })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);
  return dialog(page, "Leistung bearbeiten");
}

test("price increase: proposes, checks and keeps a pending increase in the item dialog", async ({
  page,
}) => {
  const { number } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  await itemDialog.getByRole("button", { name: "Preise erhöhen" }).click();
  await waitForOverlayOpen(page);

  const popover = dialog(page, "Preise erhöhen");

  // Escape closes the popover only, not the item dialog beneath it
  await page.keyboard.press("Escape");
  await expect(popover).toHaveCount(0);
  await expect(itemDialog).toBeVisible();
  await itemDialog.getByRole("button", { name: "Preise erhöhen" }).click();
  await waitForOverlayOpen(page);

  const price = (label: string) => popover.getByRole("spinbutton", { name: `Neu ${label}` });
  await fillNumber(popover.getByRole("spinbutton", { name: "Erhöhung in %" }), "5");

  await expect(price("Standardpreis 10,10 €")).toHaveValue("10,61 €");
  await expect(price("Eigener Preis 95,00 €")).toHaveValue("99,75 €");
  await expect(price("Eigener Preis 150,00 €")).toHaveValue("157,50 €");
  // 99,75 over two recipients is odd in cents, so it is corrected to 99,76
  await expect(popover).toContainText("Rundungsanpassung bei 1 Gräbern");

  await fillNumber(price("Eigener Preis 95,00 €"), "100");
  await expect(price("Eigener Preis 95,00 €")).toHaveValue("100,00 €");
  // the server's answer for 100,00 has come in once "Übernehmen" is enabled
  await expect(popover.getByRole("button", { name: "Übernehmen" })).toBeEnabled();
  await expect(popover).not.toContainText("Rundungsanpassung");

  // leaving the percent field unchanged keeps the manual edit
  await popover.getByRole("spinbutton", { name: "Erhöhung in %" }).click();
  await price("Eigener Preis 150,00 €").click();
  await expect(price("Eigener Preis 95,00 €")).toHaveValue("100,00 €");

  // a new percentage proposes every price again and drops the manual edit
  await fillNumber(popover.getByRole("spinbutton", { name: "Erhöhung in %" }), "10");
  await expect(price("Eigener Preis 95,00 €")).toHaveValue("104,50 €");
  await expect(price("Standardpreis 10,10 €")).toHaveValue("11,11 €");

  await popover.getByRole("checkbox", { name: "Eigener Preis 150,00 €" }).uncheck();
  const apply = popover.getByRole("button", { name: "Übernehmen" });
  await expect(apply).toBeEnabled();
  await apply.click();
  await expect(popover).toHaveCount(0);

  await expect(itemDialog.locator("s").filter({ hasText: "10,10 €" })).toBeVisible();
  await expect(itemDialog.locator("s").filter({ hasText: "95,00 €" })).toBeVisible();
  await expect(itemDialog).toContainText("104,50 €");
  await expect(itemDialog.locator("s").filter({ hasText: "150,00 €" })).toHaveCount(0);
  const defaultField = itemDialog.locator("#itemDefaultPrice");
  await expect(defaultField).toHaveValue("11,11 €");
  await expect(defaultField).toBeDisabled();

  // nothing was saved
  await itemDialog.getByRole("button", { name: "Abbrechen" }).click();
  await expect(itemDialog).toHaveCount(0);
  const reopened = await openItem(page, number);
  await expect(reopened.locator("#itemDefaultPrice")).toHaveValue("10,10 €");
  await expect(reopened.locator("s")).toHaveCount(0);
  await expect(reopened).toContainText("Eigener Preis 95,00 €");
});

async function raise(page: Page, itemDialog: ReturnType<typeof dialog>, percent: string) {
  await itemDialog.getByRole("button", { name: "Preise erhöhen" }).click();
  await waitForOverlayOpen(page);
  const popover = dialog(page, "Preise erhöhen");
  await fillNumber(popover.getByRole("spinbutton", { name: "Erhöhung in %" }), percent);
  return popover;
}

async function apply(popover: ReturnType<typeof dialog>) {
  const button = popover.getByRole("button", { name: "Übernehmen" });
  await expect(button).toBeEnabled();
  await button.click();
  await expect(popover).toHaveCount(0);
}

async function ownPrices(page: Page, itemId: number): Promise<{ default: number; own: number[] }> {
  const response = await page.request.get(`/odata/ItemGraves?itemId=${itemId}`);
  expect(response.ok()).toBe(true);
  const body = await response.json();
  const own = (body.Groups as { IsDefault: boolean; UnitPrice: number }[])
    .filter((group) => !group.IsDefault)
    .map((group) => group.UnitPrice)
    .sort((a, b) => a - b);
  return { default: body.DefaultPrice, own };
}

test("saving the item applies the increase to every ticked price", async ({ page }) => {
  const { number, itemId } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  const popover = await raise(page, itemDialog, "5");
  await popover.getByRole("checkbox", { name: "Eigener Preis 150,00 €" }).uncheck();
  await fillNumber(popover.getByRole("spinbutton", { name: "Neu Eigener Preis 95,00 €" }), "100");
  await apply(popover);

  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(itemDialog).toHaveCount(0);

  expect(await ownPrices(page, itemId)).toEqual({ default: 10.61, own: [100, 150] });
});

test("a raised price that needs a rounding correction is stored with it", async ({ page }) => {
  const { number, itemId } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  const popover = await raise(page, itemDialog, "5");
  await expect(popover).toContainText("Rundungsanpassung bei 1 Gräbern");
  await apply(popover);

  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  // an unconfirmed correction would be refused and keep the dialog open
  await expect(itemDialog).toHaveCount(0);

  expect((await ownPrices(page, itemId)).own).toEqual([99.75, 157.5]);
});

test("discarding the increase saves the item as before", async ({ page }) => {
  const { number, itemId } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  const popover = await raise(page, itemDialog, "5");
  await apply(popover);
  await itemDialog.getByRole("button", { name: "Erhöhung verwerfen" }).click();
  await itemDialog.locator("#itemDescription").fill("Neue Beschreibung");

  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(itemDialog).toHaveCount(0);

  expect(await ownPrices(page, itemId)).toEqual({ default: 10.1, own: [95, 150] });
  const response = await page.request.get(`/odata/InvoiceItems(${itemId})`);
  expect((await response.json()).Description).toBe("Neue Beschreibung");
});

test("a default typed before the increase is not shown once the increase leaves it out", async ({
  page,
}) => {
  const { number } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  const defaultField = itemDialog.locator("#itemDefaultPrice");
  await fillNumber(defaultField, "12");
  await expect(defaultField).toHaveValue("12,00 €");

  const popover = await raise(page, itemDialog, "5");
  await popover.getByRole("checkbox", { name: "Standardpreis 10,10 €" }).uncheck();
  await apply(popover);

  // a save stores the stored default, so the field shows that
  await expect(defaultField).toBeDisabled();
  await expect(defaultField).toHaveValue("10,10 €");

  await itemDialog.getByRole("button", { name: "Erhöhung verwerfen" }).click();
  await expect(defaultField).toBeEnabled();
  await expect(defaultField).toHaveValue("12,00 €");
});

test("a refused save because the corrections changed is recovered by reopening the increase", async ({
  page,
}) => {
  const { number, itemId } = await seedItem(page);
  const itemDialog = await openItem(page, number);
  const popover = await raise(page, itemDialog, "5");
  await apply(popover);

  const handler = (route: Route) =>
    route.fulfill({
      status: 400,
      contentType: "application/json",
      body: JSON.stringify({ error: { code: "CorrectionsChanged", message: "x" } }),
    });
  await page.route("**/odata/ApplyItemPriceIncrease", handler);
  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(
    page.getByText("Die Rundungsanpassungen haben sich inzwischen geändert."),
  ).toBeVisible();
  await expect(itemDialog).toBeVisible();
  await page.unroute("**/odata/ApplyItemPriceIncrease", handler);

  await itemDialog.getByRole("button", { name: "Preise erhöhen" }).click();
  await waitForOverlayOpen(page);
  const reopened = dialog(page, "Preise erhöhen");
  await apply(reopened);
  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(itemDialog).toHaveCount(0);

  expect(await ownPrices(page, itemId)).toEqual({ default: 10.61, own: [99.75, 157.5] });
});
