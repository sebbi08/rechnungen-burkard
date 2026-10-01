import { expect, type Page, test } from "@playwright/test";
import { createRow, ensureRow } from "../seed";

// Unique per run: other specs write invoices into the same stack in parallel.
const RUN = Date.now();
const RECIPIENT_NAME = `Detail ${RUN}`;

/** One revision's panel of the detail page, found by its header. */
function revisionPanel(page: Page, label: RegExp) {
  return page
    .locator(".p-accordionpanel")
    .filter({ has: page.getByRole("button", { name: label }) });
}

test("the detail page shows every revision of an invoice", async ({ page }) => {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `D${RUN}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: RECIPIENT_NAME,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });

  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }],
  });
  await createRow(page, "/odata/CreateRevision", {
    invoiceId: created.Id,
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 3, unitPrice: 100 }],
  });
  // CreateInvoice's response is the specification's contract for RootId
  expect(created.RootId).toBe(created.Id);

  await page.goto(`/invoices/${created.RootId}`);

  // the chain's number heads the page, without the revision suffix
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(created.InvoiceNumber);
  await expect(page.getByText(RECIPIENT_NAME)).toBeVisible();

  // the original and its revision are both on the page, newest first
  const headers = page.locator(".p-accordionheader");
  await expect(headers).toHaveCount(2);
  await expect(headers.nth(0)).toContainText("Revision 1");
  await expect(headers.nth(1)).toContainText("Original");

  // the revision is open: 3 × 100.00 net + 7% = 321,00
  const revision = revisionPanel(page, /^Revision 1/);
  await expect(revision.locator("dl")).toBeVisible();
  await expect(revision.locator("dl")).toContainText("Gesamt321,00 €");

  // the Original panel starts collapsed; expand it and check its own total
  // rather than just trusting the one panel that starts open. A bug where
  // every panel bound to the latest revision instead of its own row would
  // still show the right number above and would pass without this.
  const original = revisionPanel(page, /^Original/);
  await expect(original.locator("dl")).toBeHidden();
  await original.getByRole("button", { name: /^Original/ }).click();
  // 2 × 100.00 net + 7% = 214,00
  await expect(original.locator("dl")).toBeVisible();
  await expect(original.locator("dl")).toContainText("Gesamt214,00 €");

  // both documents of a revision are reachable from its panel
  for (const panel of [revision, original]) {
    await expect(panel.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
  }
});

test("a revision's id opens the chain it belongs to", async ({ page }) => {
  // the page takes the root id every list row carries, and resolves any other
  // revision's id to its chain rather than showing nothing
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `DR${RUN}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Detail Kette ${RUN}`,
    Address: "Musterstraße 1",
  });
  const created = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
  });
  const revision = await createRow(page, "/odata/CreateRevision", {
    invoiceId: created.Id,
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }],
  });

  await page.goto(`/invoices/${revision.Id}`);
  await expect(page.locator(".p-accordionheader")).toHaveCount(2);
});
