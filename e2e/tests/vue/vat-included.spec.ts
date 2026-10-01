import { expect, test } from "@playwright/test";
import { createRow, ensureRow } from "../../seed";
import {
  drawer,
  pickItem,
  pickRecipient,
  rowAction,
  saveEditor,
  waitForOverlayOpen,
} from "../../vue";

// Unique per run: the specs share one stack and write into it in parallel.
const RUN = Date.now();

test("a line ticked as VAT-included bills the price that was entered", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `VI-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 120,
  });
  const recipientName = `Brutto ${RUN}`;
  await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });

  await page.goto("/invoices");
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForOverlayOpen(page);

  // pick the recipient explicitly rather than relying on the editor's default
  // (the newest recipient): another spec's recipient can land in between
  await pickRecipient(page, "invoiceRecipient", recipientName);
  await pickItem(page, 1, `VI-${RUN}`);
  // picking the item fills in its default price
  const editor = drawer(page);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("120,00");

  // the price above is the gross the customer pays
  await editor.getByRole("checkbox", { name: "inkl. MwSt." }).check();
  // the estimate takes the price as a gross now: 100,84 net + 19 % = 120,00
  await expect(editor).toContainText("Summe ca.: 100,84 € netto, 120,00 € brutto");
  await saveEditor(editor);

  // the invoices list shows no totals, only the invoice detail page does — open
  // it from the row this test just created
  const row = page.getByRole("row").filter({ hasText: recipientName });
  await row.getByRole("button", { name: rowAction("Details") }).click();

  // 120,00 gross in, 100,84 net + 19 % = 120,00 out
  await expect(page.locator("dl")).toContainText("Netto100,84 €");
  await expect(page.locator("dl")).toContainText("Gesamt120,00 €");
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

  await page.goto(`/invoices/${created.RootId ?? created.Id}`);
  await expect(page.getByText("120,00 €").first()).toBeVisible();

  // the editor opens on the gross with the box still ticked, not on the stored 100,84
  await page.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("120,00");
  await expect(editor.getByRole("checkbox", { name: "inkl. MwSt." })).toBeChecked();

  // saved untouched, the VAT must not come out a second time
  await saveEditor(editor);
  await expect(page.locator(".p-accordionheader").first()).toContainText("Revision 1");
  const latest = page.locator(".p-accordionpanel").first();
  await expect(latest.locator("dl")).toContainText("Netto100,84 €");
  await expect(latest.locator("dl")).toContainText("Gesamt120,00 €");
});
