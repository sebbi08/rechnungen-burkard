import { expect, test } from "@playwright/test";
import { waitForDialogOpen } from "../dialog";
import { createRow, ensureRow } from "../seed";

// Unique per run: the specs share one stack and write into it in parallel.
const RUN = Date.now();

test("an item can be marked as priced VAT-included and stays marked", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const number = `VI-${RUN}`;

  await page.goto("/index.html#/items");
  await page.getByRole("button", { name: "Leistung anlegen" }).click();
  await waitForDialogOpen(page);

  const dialog = page.locator(".sapMDialog");
  // scope to the dialog: the page's search field placeholder also says "Nummer"
  await dialog.getByLabel("Nummer").fill(number);
  await dialog.getByLabel("Bezeichnung").fill("Grabpflege inklusive");
  await dialog.locator(".sapMSlt").click();
  await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "19" }).click();
  await dialog.getByLabel("Standardpreis").fill("120");
  await dialog.getByText("Preis inkl. MwSt.").click();
  await page.getByRole("button", { name: "Speichern" }).click();

  // the list says which prices are gross, so the price column is not ambiguous
  const itemsTable = page.locator("[id$='itemsTable-listUl']");
  const row = itemsTable.locator("tbody tr", { hasText: number });
  await expect(row).toContainText("inkl. MwSt.");

  // and the dialog reopens on what was saved, price and flag alike
  // (the OData v4 model preserves the server's Edm.Decimal literal verbatim,
  // which a round trip through the DB renders as "120.0" rather than "120" —
  // pre-existing on DefaultPrice, unrelated to the VAT flag under test here)
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForDialogOpen(page);
  await expect(page.locator(".sapMDialog").getByLabel("Standardpreis")).toHaveValue("120.0");
  await expect(page.locator(".sapMDialog input[type='checkbox']")).toBeChecked();
});
