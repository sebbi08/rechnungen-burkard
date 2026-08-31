import { expect, test } from "@playwright/test";
import { waitForDialogOpen } from "../dialog";
import { createRow, ensureRow } from "../seed";

// Unique per run: the specs share one stack and write into it in parallel.
const RUN = Date.now();

test("a line ticked as VAT-included bills the price that was entered", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VI-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 120,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });

  await page.goto("/index.html#/invoices");
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForDialogOpen(page);

  const dialog = page.locator("[id$='invoiceDialog']");
  // the item field is a typeahead: type the number, then pick the suggestion,
  // the same way invoices.spec.ts drives it — a plain click on the ComboBox
  // opens its dropdown but leaves the row unfilled, since nothing was typed
  // for it to match against
  const lineInput = dialog.locator("input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill(`VI-${RUN}`);
  await page
    .locator("[id*='lineItem'][id$='-popup-list'] li.sapMSLI", { hasText: `VI-${RUN}` })
    .click();
  await expect(dialog.getByLabel("Preis")).toHaveValue("120");

  // the price above is the gross the customer pays.
  // sap.m.CheckBox toggles aria-checked on its own async render tick rather
  // than synchronously with the click, so locator.check() reads it too soon
  // and reports no state change; click and then wait for the attribute like
  // every other control state this suite polls for.
  await dialog.getByRole("checkbox").click();
  await expect(dialog.getByRole("checkbox")).toBeChecked();
  await dialog.getByRole("button", { name: "Speichern" }).click();

  // the invoices list shows no totals, only the invoice detail page does — open
  // it from the row this test just created, the way invoices.spec.ts does
  const recipientName = `Brutto ${RUN}`;
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");
  const row = invoicesTable.locator("tbody tr", { hasText: recipientName });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Details", exact: true }).click();

  // 120,00 gross in, 100,84 net + 19 % = 120,00 out
  await expect(page.getByText("120,00 €").first()).toBeVisible();
});

test("a ticked line reopens on its gross and re-saves to the same net", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VII-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 120,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto zwei ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: null, priceIncludesVat: true }],
  });

  await page.goto(`/index.html#/invoices/${created.RootId ?? created.Id}`);
  await expect(page.getByText("120,00 €").first()).toBeVisible();

  // the dialog opens on the gross with the box still ticked, not on the stored 100,84
  await page.getByRole("button", { name: "Bearbeiten" }).first().click();
  await waitForDialogOpen(page);
  const dialog = page.locator("[id$='invoiceDialog']");
  await expect(dialog.getByLabel("Preis")).toHaveValue("120");
  await expect(dialog.getByRole("checkbox")).toBeChecked();

  // saved untouched, the VAT must not come out a second time
  await dialog.getByRole("button", { name: "Speichern" }).click();
  await expect(page.getByText("Revision 1", { exact: true })).toBeVisible();
  await expect(page.getByText("120,00 €").first()).toBeVisible();
});
