import { expect, test } from "@playwright/test";
import { createRow, ensureRow } from "../seed";
import { unique } from "../unique";
import { drawer, pickItem, pickRecipient, saveEditor, waitForOverlayOpen } from "../vue";

// Unique per worker, and each test seeds under names of its own: every test
// shares the one stack and writes into it at the same time as the others.
const RUN = unique();

test("a line ticked as VAT-included bills the price that was entered", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: `VI-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 75,
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
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("75,00");

  // the price above is the gross the customer pays
  await editor.getByRole("checkbox", { name: "inkl. MwSt." }).check();
  // the estimate takes the price as a gross: 63,03 net + 19 % = 75,00
  await expect(editor).toContainText("Summe ca.: 63,03 € netto, 75,00 € brutto");
  const created = page.waitForResponse((response) =>
    response.url().endsWith("/odata/CreateInvoice"),
  );
  await saveEditor(editor);
  const { InvoiceNumber } = await (await created).json();

  // the invoices list shows no totals, only the invoice detail page does — open
  // it from the row of the invoice this test just created, found by its number
  await page.getByRole("button", { name: `Details: ${InvoiceNumber}` }).click();

  // 75,00 gross in, exactly 75,00 out
  await expect(page.locator("dl")).toContainText("Gesamt75,00 €");
});

test("a ticked line reopens on its gross and re-saves unchanged", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VII-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 75,
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
  await expect(page.getByText("75,00 €").first()).toBeVisible();

  // the editor opens on the gross with the box still ticked, not on the stored net
  await page.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("75,00");
  await expect(editor.getByRole("checkbox", { name: "inkl. MwSt." })).toBeChecked();

  // saved untouched, the VAT must not come out a second time
  await saveEditor(editor);
  await expect(page.locator(".p-accordionheader").first()).toContainText("Revision 1");
  const latest = page.locator(".p-accordionpanel").first();
  await expect(latest.locator("dl")).toContainText("Gesamt75,00 €");
});

test("a ticked line bills its count times the entered gross", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `VIC-${RUN}`,
    Description: "Grabpflege",
    VatRate: 19,
    DefaultPrice: 10,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Brutto drei ${RUN}`,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 3, unitPrice: null, priceIncludesVat: true }],
  });

  await page.goto(`/invoices/${created.RootId ?? created.Id}`);
  await expect(page.locator("dl")).toContainText("Gesamt30,00 €");
});

test("the grave editor offers no VAT-included checkbox", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await page.goto("/graves");
  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor.getByLabel("Grabnummer")).toBeVisible();
  // a new grave starts without a row, and the checkbox is drawn per row
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toBeVisible();
  await expect(editor.getByRole("checkbox", { name: "inkl. MwSt." })).toHaveCount(0);
  await expect(editor).not.toContainText("inkl. MwSt.");
});
