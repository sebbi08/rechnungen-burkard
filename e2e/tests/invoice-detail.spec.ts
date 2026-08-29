import { expect, test } from "@playwright/test";
import { createRow } from "../seed";

// Unique per run: other specs write invoices into the same stack in parallel.
const RUN = Date.now();
const RECIPIENT_NAME = `Detail ${RUN}`;

test("the detail page shows every revision of an invoice", async ({ page }) => {
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await createRow(page, "/odata/InvoiceItems", { Number: `D${RUN}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 });
  const recipient = await createRow(page, "/odata/Recipients", { Name: RECIPIENT_NAME, Address: "Musterstraße 1\n12345 Musterstadt" });

  const created = await createRow(page, "/odata/CreateInvoice", { recipientId: recipient.Id, lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }] });
  await page.request.post("/odata/CreateRevision", {
    data: {
      invoiceId: created.Id,
      recipientId: recipient.Id,
      lines: [{ itemId: item.Id, count: 3, unitPrice: 100 }],
    },
  });

  // CreateInvoice's response is the specification's contract for RootId, but
  // that has never been exercised at runtime before this test — confirm it
  // rather than silently falling back to created.Id, which would hide a
  // routing bug behind a passing test.
  let rootId = created.RootId;
  if (rootId == null) {
    const fetched = await (
      await page.request.get(`/odata/Invoices(${created.Id})?$select=RootId`)
    ).json();
    rootId = fetched.RootId;
  }

  await page.goto(`/index.html#/invoices/${rootId}`);

  // the original and its revision are both on the page, newest first
  await expect(page.getByText("Revision 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Original", { exact: true })).toBeVisible();
  await expect(page.getByText(RECIPIENT_NAME)).toBeVisible();
  // 3 × 100.00 net + 7% = 321,00
  await expect(page.getByText("321,00 €").first()).toBeVisible();

  // the Original panel starts collapsed; expand it and check its own total
  // rather than just trusting the one panel that starts open. A bug where
  // every panel bound to the latest revision instead of its own row would
  // still show the right number above and would pass without this.
  await page.getByRole("button", { name: "Original", exact: true }).click();
  // 2 × 100.00 net + 7% = 214,00
  await expect(page.getByText("214,00 €").first()).toBeVisible();

  // both documents of a revision are reachable from its panel
  await expect(
    page.getByRole("button", { name: "Rechnung herunterladen" }).first(),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Kopie herunterladen" }).first(),
  ).toBeVisible();
});
