import { expect, type Locator, type Page, test } from "@playwright/test";
import { createRow, ensureCemetery, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";
import {
  dialog,
  drawer,
  fillDate,
  fillNumber,
  pickRecipient,
  saveEditor,
  searchColumn,
  waitForOverlayOpen,
} from "../vue";

// The Verlauf of graves and invoices: what a change made through the app
// leaves on the page of what it changed. Every test seeds a grave or an invoice
// of its own, so the entries it reads are its own.

/** The entries of the page's Verlauf, newest first. */
function auditEntries(page: Page): Locator {
  return page.getByTestId("audit-trail").getByTestId("audit-entry");
}

/** A grave due in 2045 with one all-year item at 100,00 € net and one
 *  recipient, the item billed at its default price. No test generates 2045,
 *  so no generation adds an entry to the grave's Verlauf while a test reads it. */
async function seedSimpleGrave(
  page: Page,
  marker: string,
): Promise<{ graveId: number; itemNumber: string; itemId: number; recipientId: number }> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const itemNumber = `${marker}-item`;
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Anna ${marker}`,
    Address: "Musterweg 1",
  });
  const graveId = await seedGrave(page, {
    graveNumber: marker,
    startDate: "2045-01",
    endDate: "2045-12",
    items: [{ itemId: item.Id, count: 1 }],
    recipientIds: [recipient.Id],
  });
  return { graveId, itemNumber, itemId: item.Id, recipientId: recipient.Id };
}

test("editing a grave shows the change in its Verlauf", async ({ page }) => {
  const marker = `AV-${unique()}`;
  const { graveId } = await seedSimpleGrave(page, marker);

  await page.goto(`/graves/${graveId}`);
  // the grave's creation is its first entry
  await expect(auditEntries(page)).toHaveCount(1);
  await expect(auditEntries(page).first()).toContainText("Grab angelegt");

  await page.getByRole("button", { name: "Grab bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await fillDate(page, editor.getByLabel("Ende", { exact: true }), "12/2046");
  await saveEditor(editor);

  // newest first: the edit, with the end month from old to new, above the creation
  await expect(auditEntries(page)).toHaveCount(2);
  const edited = auditEntries(page).first();
  await expect(edited).toContainText("Grab bearbeitet");
  await expect(edited).toContainText("Ende: 12/2045 → 12/2046");
  await expect(auditEntries(page).nth(1)).toContainText("Grab angelegt");
});

test("a price increase shows on the grave's Verlauf", async ({ page }) => {
  const marker = `AP-${unique()}`;
  const { graveId, itemNumber } = await seedSimpleGrave(page, marker);

  await page.goto("/items");
  // the list pages by 10 and parallel tests keep adding items: search for this one
  await searchColumn(page, "Nummer", itemNumber);
  await page
    .getByRole("row")
    .filter({ hasText: itemNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);
  const itemDialog = dialog(page, "Leistung bearbeiten");
  await itemDialog.getByRole("button", { name: "Preise erhöhen" }).click();
  await waitForOverlayOpen(page);
  const increase = dialog(page, "Preise erhöhen");
  await fillNumber(increase.getByRole("spinbutton", { name: "Erhöhung in %" }), "10");
  await expect(
    increase.getByRole("spinbutton", { name: "Neu Standardpreis 100,00 €" }),
  ).toHaveValue("110,00 €");
  const apply = increase.getByRole("button", { name: "Übernehmen" });
  await expect(apply).toBeEnabled();
  await apply.click();
  await expect(increase).toHaveCount(0);
  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(itemDialog).toHaveCount(0);

  // the grave bills the default, so the increase reached it without a link of
  // its own changing — and its Verlauf says so, naming the item
  await page.goto(`/graves/${graveId}`);
  const raised = auditEntries(page).first();
  await expect(raised).toContainText(`Preiserhöhung ${itemNumber} Grabpflege`);
  await expect(raised).toContainText("Preis: 100,00 € → 110,00 €");
});

test("revising an invoice shows the new recipient in its Verlauf", async ({ page }) => {
  const marker = `AR-${unique()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${marker}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: null,
  });
  const [anna, bernd] = await Promise.all(
    [`Anna ${marker}`, `Bernd ${marker}`].map((name) =>
      createRow(page, "/odata/Recipients", { Name: name, Address: "Musterweg 1" }),
    ),
  );
  const invoice = await createRow(page, "/odata/CreateInvoice", {
    recipientId: anna.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
  });

  await page.goto(`/invoices/${invoice.RootId}`);
  await expect(auditEntries(page)).toHaveCount(1);
  await expect(auditEntries(page).first()).toContainText("Rechnung erstellt");

  await page.getByRole("button", { name: "Bearbeiten", exact: true }).click();
  await waitForOverlayOpen(page);
  await pickRecipient(page, "invoiceRecipient", bernd.Name);
  await saveEditor(drawer(page));

  // the revision's entry names the recipient it was and the one it is now,
  // each with their number
  await expect(auditEntries(page)).toHaveCount(2);
  const revised = auditEntries(page).first();
  await expect(revised).toContainText("Rechnung überarbeitet");
  await expect(revised).toContainText(
    `Empfänger: ${anna.Number} ${anna.Name} → ${bernd.Number} ${bernd.Name}`,
  );
  await expect(revised).toContainText(`${invoice.InvoiceNumber} → ${invoice.InvoiceNumber}-R1`);
});

test("clearing an item's default price reads as no default price", async ({ page }) => {
  const marker = `AD-${unique()}`;
  const { graveId, itemNumber, itemId } = await seedSimpleGrave(page, marker);

  // the way the item dialog saves: the whole item, here without a default price
  const response = await page.request.patch(`/odata/InvoiceItems(${itemId})`, {
    data: {
      Number: itemNumber,
      Description: "Grabpflege",
      VatRate: 7,
      Quarter: null,
      DefaultPrice: null,
    },
  });
  expect(response.ok(), await response.text()).toBe(true);

  // the grave billed the default, so it keeps 100,00 € as its own price
  await page.goto(`/graves/${graveId}`);
  await expect(auditEntries(page).first()).toContainText(
    `Standardpreis von ${itemNumber} Grabpflege geändert (100,00 € → kein Standardpreis); ` +
      "Grab behält 100,00 € als eigenen Preis",
  );
});

test("the Verlauf loads older entries page by page", async ({ page }) => {
  const marker = `AL-${unique()}`;
  const { graveId, itemId, recipientId } = await seedSimpleGrave(page, marker);

  // 44 edits and the creation: more than two pages of 20. Each one a save of
  // the grave as the editor sends it, with the next field number.
  const edits = 44;
  const cemeteryId = await ensureCemetery(page);
  for (let n = 2; n <= edits + 1; n++) {
    await createRow(page, "/odata/SaveGrave", {
      graveId,
      graveNumber: marker,
      fieldNumber: String(n),
      siteName: "Schineller Bäcke",
      cemeteryId,
      position: "SingleInside",
      startDate: "2045-01",
      endDate: "2045-12",
      items: [{ itemId, count: 1, unitPrice: null }],
      recipientIds: [recipientId],
      isCooperative: false,
      contractNumber: null,
    });
  }

  await page.goto(`/graves/${graveId}`);
  const trail = page.getByTestId("audit-trail");
  const more = trail.getByRole("button", { name: "Weitere laden" });
  // newest first: the last edit on top, the first page holding 20
  await expect(auditEntries(page)).toHaveCount(20);
  await expect(auditEntries(page).first()).toContainText(`Feldnummer: ${edits} → ${edits + 1}`);
  await expect(auditEntries(page).filter({ hasText: "Grab angelegt" })).toHaveCount(0);

  // each click adds the next older ones below, and the ones shown stay
  await more.click();
  await expect(auditEntries(page)).toHaveCount(40);
  await expect(auditEntries(page).first()).toContainText(`Feldnummer: ${edits} → ${edits + 1}`);
  await expect(auditEntries(page).nth(20)).toContainText(
    `Feldnummer: ${edits - 20} → ${edits - 19}`,
  );

  // the last page holds the rest, down to the creation, and the button goes
  await more.click();
  await expect(auditEntries(page)).toHaveCount(edits + 1);
  await expect(auditEntries(page).last()).toContainText("Grab angelegt");
  await expect(auditEntries(page).nth(edits - 1)).toContainText("Feldnummer: 1 → 2");
  await expect(more).toHaveCount(0);
  await expect(trail).not.toContainText("nicht geladen werden");
});
