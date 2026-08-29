import { expect, test } from "@playwright/test";

// Unique per run: other specs write invoices into the same stack in parallel.
const RUN = Date.now();
const RECIPIENT_NAME = `Detail ${RUN}`;

test("the detail page shows every revision of an invoice", async ({ page }) => {
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: { Number: `D${RUN}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: RECIPIENT_NAME, Address: "Musterstraße 1\n12345 Musterstadt" },
    })
  ).json();

  const created = await (
    await page.request.post("/odata/CreateInvoice", {
      data: { recipientId: recipient.Id, lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }] },
    })
  ).json();
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

  // both documents of a revision are reachable from its panel
  await expect(
    page.getByRole("button", { name: "Rechnung herunterladen" }).first(),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Kopie herunterladen" }).first(),
  ).toBeVisible();
});
