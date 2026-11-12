import { expect, type Page, test } from "@playwright/test";
import { createRow, ensureRow } from "../seed";
import { unique } from "../unique";
import {
  drawer,
  fillNumber,
  pickItem,
  pickRecipient,
  rowAction,
  saveEditor,
  waitForOverlayOpen,
} from "../vue";

/** The editor's recipient picker, the same for an invoice and a quote. */
const RECIPIENT = "invoiceRecipient";

/** An item and a recipient of the calling test's own. */
async function seedMasterData(page: Page) {
  const run = unique();
  const recipientName = `Kommentar ${run}`;
  const itemNumber = `KOM-${run}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Heckenschnitt",
    VatRate: 19,
    DefaultPrice: 80,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  return { recipientName, recipientId: recipient.Id as number, itemNumber, itemId: item.Id };
}

test("an invoice takes a free text above its lines and a comment on each line", async ({
  page,
}) => {
  const { recipientName, recipientId, itemNumber } = await seedMasterData(page);
  await page.goto("/invoices");
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(page.locator(`#${RECIPIENT}`)).not.toHaveText("Empfänger wählen");
  await pickRecipient(page, RECIPIENT, recipientName);

  await editor
    .getByRole("textbox", { name: "Text über den Positionen" })
    .fill("Arbeiten im Garten\nvom 3. bis 5. März");
  await pickItem(page, 1, itemNumber);
  await editor
    .getByRole("textbox", { name: "Kommentar 1" })
    .fill("Hecke an der Straßenseite\nSchnittgut entsorgt");
  await editor.getByRole("button", { name: "Zeile hinzufügen" }).click();
  await pickItem(page, 2, itemNumber);
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 2" }), "2");
  await saveEditor(editor);

  const invoices = (
    await (
      await page.request.get(
        `/odata/Invoices?$filter=RecipientId eq ${recipientId}&$expand=Lines($orderby=LineNumber)`,
      )
    ).json()
  ).value as { IntroText: string; Lines: { Comment: string }[] }[];
  expect(invoices).toHaveLength(1);
  expect(invoices[0].IntroText).toBe("Arbeiten im Garten\nvom 3. bis 5. März");
  expect(invoices[0].Lines.map((line) => line.Comment)).toEqual([
    "Hecke an der Straßenseite\nSchnittgut entsorgt",
    "",
  ]);
});

test("editing a quote opens on its texts and keeps the changed ones", async ({ page }) => {
  const { recipientId, itemId } = await seedMasterData(page);
  const quote = await createRow(page, "/odata/CreateQuote", {
    recipientId,
    introText: "Angebot für den Vorgarten",
    lines: [{ itemId, count: 1, unitPrice: null, priceIncludesVat: false, comment: "Erster" }],
  });
  await page.goto(`/quotes?number=${encodeURIComponent(quote.QuoteNumber)}`);
  const row = page.getByRole("row").filter({ hasText: quote.QuoteNumber });
  await expect(row).toBeVisible();

  await row.getByRole("button", { name: rowAction("Bearbeiten") }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  const intro = editor.getByRole("textbox", { name: "Text über den Positionen" });
  const comment = editor.getByRole("textbox", { name: "Kommentar 1" });
  await expect(intro).toHaveValue("Angebot für den Vorgarten");
  await expect(comment).toHaveValue("Erster");
  await comment.fill("Geänderter Kommentar\nzweite Zeile");
  await saveEditor(editor);

  await expect(row.getByText("R1", { exact: true })).toBeVisible();
  const revisions = (
    await (
      await page.request.get(
        `/odata/Quotes?$filter=QuoteNumber eq '${quote.QuoteNumber}-R1'&$expand=Lines`,
      )
    ).json()
  ).value as { IntroText: string; Lines: { Comment: string }[] }[];
  expect(revisions).toHaveLength(1);
  expect(revisions[0].IntroText).toBe("Angebot für den Vorgarten");
  expect(revisions[0].Lines[0].Comment).toBe("Geänderter Kommentar\nzweite Zeile");
});
