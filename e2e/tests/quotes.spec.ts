import { expect, type Page, test } from "@playwright/test";
import { createRow, ensureRow, seedQuote } from "../seed";
import { unique } from "../unique";
import {
  confirmation,
  drawer,
  fillNumber,
  filterColumn,
  pickItem,
  pickRecipient,
  rowAction,
  saveEditor,
  searchColumn,
  waitForOverlayOpen,
} from "../vue";

/** The editor's recipient picker, the same in quote mode as for an invoice. */
const RECIPIENT = "invoiceRecipient";

/**
 * Seeds an item without a default price and a recipient, both the calling
 * test's own, like invoices.spec.ts does: every test runs beside the others,
 * so its rows are told apart by names nobody else uses.
 */
async function seedMasterData(page: Page): Promise<{
  recipientName: string;
  recipientId: number;
  itemNumber: string;
  itemId: number;
}> {
  const run = unique();
  const recipientName = `Angebot ${run}`;
  const itemNumber = `ANG-${run}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "Grabgestaltung",
    VatRate: 7,
    DefaultPrice: null,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Musterstraße 1\n12345 Musterstadt",
  });
  return { recipientName, recipientId: recipient.Id, itemNumber, itemId: item.Id };
}

/** A seeded quote of one line, 1 × 100,00 net, and its master data. */
async function seedOneQuote(page: Page) {
  const data = await seedMasterData(page);
  const quote = await seedQuote(page, {
    recipientId: data.recipientId,
    lines: [{ itemId: data.itemId, count: 1, unitPrice: 100 }],
  });
  return { ...data, ...quote };
}

/** One row of the quote list. */
function quoteRow(page: Page, text: string) {
  return page.getByRole("row").filter({ hasText: text });
}

/** The quote list narrowed to one quote by its number, through the URL the
 *  list keeps its searches in: independent of how many quotes other tests have
 *  created meanwhile. */
async function openListFor(page: Page, number: string) {
  await page.goto(`/quotes?number=${encodeURIComponent(number)}`);
  const row = quoteRow(page, number);
  await expect(row).toBeVisible();
  return row;
}

test("create a quote and open its PDF", async ({ page }) => {
  const { recipientName, recipientId, itemNumber } = await seedMasterData(page);
  await page.goto("/quotes");

  await page.getByRole("button", { name: "Neues Angebot" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor).toContainText("Neues Angebot");
  await expect(page.locator(`#${RECIPIENT}`)).not.toHaveText("Empfänger wählen");
  await pickRecipient(page, RECIPIENT, recipientName);
  await pickItem(page, 1, itemNumber);
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 1" }), "2");
  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "120");
  await saveEditor(editor);

  const row = quoteRow(page, recipientName).first();
  await expect(row).toBeVisible();
  await expect(row).toContainText(new RegExp(`A${new Date().getFullYear()}-\\d{4}`));
  await expect(row).toContainText("Offen");

  const created = (
    await (await page.request.get(`/odata/Quotes?$filter=RecipientId eq ${recipientId}`)).json()
  ).value as { Id: number }[];
  expect(created).toHaveLength(1);

  // the PDF opens in a new tab, served for display rather than download
  const tabPromise = page.context().waitForEvent("page");
  const pdfRequest = page
    .context()
    .waitForEvent("request", (request) => /\/odata\/Quotes\(\d+\)\/Pdf$/.test(request.url()));
  await row.getByRole("button", { name: rowAction("Angebot herunterladen") }).click();
  const tab = await tabPromise;
  const pdfUrl = (await pdfRequest).url();
  await tab.close();
  expect(pdfUrl).toMatch(new RegExp(`/odata/Quotes\\(${created[0].Id}\\)/Pdf$`));

  const pdf = await page.request.get(pdfUrl);
  expect(pdf.headers()["content-type"]).toBe("application/pdf");
  expect(pdf.headers()["content-disposition"] ?? "").toContain("inline");

  // a quote is no invoice: the invoice list has nothing for this recipient
  const invoices = page.waitForResponse((response) => {
    const url = decodeURIComponent(response.url().replace(/\+/g, " "));
    return url.includes("/odata/Invoices") && url.includes(`recipient=${recipientName}`);
  });
  await page.goto(`/invoices?recipient=${encodeURIComponent(recipientName)}`);
  await invoices;
  await expect(page.getByText("Keine Daten")).toBeVisible();
  await expect(quoteRow(page, recipientName)).toHaveCount(0);
});

test("editing a quote makes a revision", async ({ page }) => {
  const { recipientName, number } = await seedOneQuote(page);
  const row = await openListFor(page, number);
  await expect(row.getByText("R1", { exact: true })).toHaveCount(0);

  await row.getByRole("button", { name: rowAction("Bearbeiten") }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  await expect(editor).toContainText("Angebot bearbeiten");
  // the editor opens on the quote as it is
  await expect(page.locator(`#${RECIPIENT}`)).toHaveText(recipientName);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("100,00");
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 1" }), "3");
  await saveEditor(editor);

  await expect(row.getByText("R1", { exact: true })).toBeVisible();
  const revisions = await (
    await page.request.get(`/odata/Quotes?$filter=QuoteNumber eq '${number}-R1'`)
  ).json();
  expect(revisions.value).toHaveLength(1);
});

test("decline and reopen a quote", async ({ page }) => {
  const { number } = await seedOneQuote(page);
  const row = await openListFor(page, number);
  await expect(row).toContainText("Offen");

  await row.getByRole("button", { name: rowAction("Ablehnen") }).click();
  await expect(row).toContainText("Abgelehnt");
  // a declined quote is neither edited nor converted
  await expect(row.getByRole("button", { name: rowAction("Bearbeiten") })).toHaveCount(0);
  await expect(row.getByRole("button", { name: rowAction("In Rechnung umwandeln") })).toHaveCount(
    0,
  );

  await row.getByRole("button", { name: rowAction("Wieder öffnen") }).click();
  await expect(row).toContainText("Offen");
  await expect(row.getByRole("button", { name: rowAction("Bearbeiten") })).toBeVisible();
});

test("delete a quote after confirming", async ({ page }) => {
  const { id, number } = await seedOneQuote(page);
  const row = await openListFor(page, number);

  await row.getByRole("button", { name: rowAction("Angebot löschen") }).click();
  await waitForOverlayOpen(page);
  // the question names the quote, so a misclick on the wrong row shows
  await expect(confirmation(page)).toContainText(number);
  await confirmation(page).getByRole("button", { name: "Ja" }).click();
  await expect(row).toHaveCount(0);
  const left = await (await page.request.get(`/odata/Quotes?$filter=RootId eq ${id}`)).json();
  expect(left.value).toHaveLength(0);
});

test("a converted quote names its invoice and offers no changes", async ({ page }) => {
  const { id, number } = await seedOneQuote(page);
  const invoice = await createRow(page, "/odata/ConvertQuote", { quoteId: id });
  const row = await openListFor(page, number);

  await expect(row).toContainText("Umgewandelt");
  const link = row.getByRole("link", { name: invoice.InvoiceNumber });
  await expect(link).toHaveAttribute("href", `/invoices/${invoice.Id}`);
  for (const name of ["Details", "Angebot herunterladen"]) {
    await expect(row.getByRole("button", { name: rowAction(name) })).toBeVisible();
  }
  for (const name of [
    "Bearbeiten",
    "In Rechnung umwandeln",
    "Ablehnen",
    "Wieder öffnen",
    "Angebot löschen",
  ]) {
    await expect(row.getByRole("button", { name: rowAction(name) })).toHaveCount(0);
  }
});

test("the list searches its columns and filters by state", async ({ page }) => {
  const { recipientName, recipientId, itemId } = await seedMasterData(page);
  const open = await seedQuote(page, {
    recipientId,
    lines: [{ itemId, count: 1, unitPrice: 100 }],
  });
  const declined = await seedQuote(page, {
    recipientId,
    lines: [{ itemId, count: 2, unitPrice: 100 }],
  });
  await createRow(page, "/odata/DeclineQuote", { quoteId: declined.id });
  await page.goto("/quotes");

  // each column's search reaches the backend as its own query option
  await searchColumn(
    page,
    "Nummer",
    open.number,
    (url) => url.includes("/odata/Quotes") && url.includes(`number=${open.number}`),
  );
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(quoteRow(page, open.number)).toBeVisible();
  await page.getByRole("textbox", { name: "Nummer durchsuchen" }).fill("");

  await searchColumn(
    page,
    "Empfänger",
    recipientName,
    (url) =>
      url.includes("/odata/Quotes") &&
      url.includes(`recipient=${recipientName}`) &&
      !url.includes("number="),
  );
  await expect(page.locator("tbody tr")).toHaveCount(2);

  // the state's select narrows the list to the declined one
  await filterColumn(page, "Status", "Abgelehnt");
  await expect(page.locator("tbody tr")).toHaveCount(1);
  await expect(quoteRow(page, declined.number)).toContainText("Abgelehnt");
  await expect(page).toHaveURL(/state=Declined/);
});

/** One version's panel of the detail page, found by its header. */
function versionPanel(page: Page, label: RegExp) {
  return page
    .locator(".p-accordionpanel")
    .filter({ has: page.getByRole("button", { name: label }) });
}

/** What an open quote's page offers to change, none of which a converted one
 *  offers. */
const CHANGES = ["Bearbeiten", "In Rechnung umwandeln", "Ablehnen", "Angebot löschen"];

test("convert a quote into an invoice", async ({ page }) => {
  const { id, number, itemNumber } = await seedOneQuote(page);
  await page.goto(`/quotes/${id}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(number);
  await expect(page.getByText("Offen", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "In Rechnung umwandeln" }).click();
  await waitForOverlayOpen(page);
  await expect(confirmation(page)).toContainText(number);
  await confirmation(page).getByRole("button", { name: "Ja" }).click();

  // the new invoice's page, with the quote's lines and the way back to it
  await expect(page).toHaveURL(/\/invoices\/\d+$/);
  const invoiceId = Number(new URL(page.url()).pathname.split("/").pop());
  await expect(page.locator(".p-accordionpanel").first()).toContainText(itemNumber);
  const back = page.getByRole("link", { name: `Aus Angebot ${number}` });
  await expect(back).toHaveText(new RegExp(`^Aus Angebot A${new Date().getFullYear()}-\\d{4}$`));
  const invoiceNumber = (await page.getByRole("heading", { level: 1 }).textContent())!.trim();

  await back.click();
  await expect(page).toHaveURL(new RegExp(`/quotes/${id}$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(number);
  await expect(page.getByText("Umgewandelt", { exact: true })).toBeVisible();
  const forward = page.getByRole("link", { name: `Umgewandelt in Rechnung ${invoiceNumber}` });
  await expect(forward).toHaveAttribute("href", `/invoices/${invoiceId}`);
  for (const name of CHANGES) {
    await expect(page.getByRole("button", { name })).toHaveCount(0);
  }
});

test("cancelling the confirmation converts nothing", async ({ page }) => {
  const { id, number, recipientId } = await seedOneQuote(page);
  await page.goto(`/quotes/${id}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(number);

  await page.getByRole("button", { name: "In Rechnung umwandeln" }).click();
  await waitForOverlayOpen(page);
  await expect(confirmation(page)).toContainText(number);
  await confirmation(page).getByRole("button", { name: "Abbrechen" }).click();
  await expect(confirmation(page)).toHaveCount(0);

  await expect(page).toHaveURL(new RegExp(`/quotes/${id}$`));
  await expect(page.getByText("Offen", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "In Rechnung umwandeln" })).toBeVisible();
  const invoices = await (
    await page.request.get(`/odata/Invoices?$filter=RecipientId eq ${recipientId}`)
  ).json();
  expect(invoices.value).toHaveLength(0);
});

test("the detail page lists every version", async ({ page }) => {
  const { id, number, recipientId, itemId } = await seedOneQuote(page);
  await createRow(page, "/odata/CreateQuoteRevision", {
    quoteId: id,
    recipientId,
    lines: [{ itemId, count: 3, unitPrice: 100, priceIncludesVat: false }],
  });
  await page.goto(`/quotes/${id}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(number);
  // the breadcrumb leads back to the list
  await expect(
    page.getByRole("navigation", { name: "Seitenpfad" }).getByRole("link", { name: "Angebote" }),
  ).toHaveAttribute("href", "/quotes");

  // newest first, the revision open and the original collapsed
  const headers = page.locator(".p-accordionheader");
  await expect(headers).toHaveCount(2);
  await expect(headers.nth(0)).toContainText("Revision 1");
  await expect(headers.nth(0)).toContainText(`${number}-R1`);
  await expect(headers.nth(1)).toContainText("Original");

  const revision = versionPanel(page, /^Revision 1/);
  // 3 × 100,00 net + 7 % = 321,00
  await expect(revision.locator("dl")).toContainText("Gesamt321,00 €");
  const original = versionPanel(page, /^Original/);
  await original.getByRole("button", { name: /^Original/ }).click();
  await expect(original.locator("dl")).toContainText("Gesamt107,00 €");

  // one document per version, and no copy
  for (const panel of [revision, original]) {
    await expect(panel.getByRole("button", { name: "Angebot herunterladen" })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Rechnung herunterladen" })).toHaveCount(0);
    await expect(panel.getByRole("button", { name: "Kopie herunterladen" })).toHaveCount(0);
  }

  // each version downloads its own PDF
  const tabPromise = page.context().waitForEvent("page");
  const pdfRequest = page
    .context()
    .waitForEvent("request", (request) => /\/odata\/Quotes\(\d+\)\/Pdf$/.test(request.url()));
  await original.getByRole("button", { name: "Angebot herunterladen" }).click();
  const tab = await tabPromise;
  const pdfUrl = (await pdfRequest).url();
  await tab.close();
  expect(pdfUrl).toMatch(new RegExp(`/odata/Quotes\\(${id}\\)/Pdf$`));
});

test("deleting from the detail page returns to the list", async ({ page }) => {
  const { id, number } = await seedOneQuote(page);
  await page.goto(`/quotes/${id}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(number);

  await page.getByRole("button", { name: "Angebot löschen" }).click();
  await waitForOverlayOpen(page);
  await expect(confirmation(page)).toContainText(number);
  await confirmation(page).getByRole("button", { name: "Ja" }).click();

  await expect(page).toHaveURL(/\/quotes$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Angebote");
  const left = await (await page.request.get(`/odata/Quotes?$filter=RootId eq ${id}`)).json();
  expect(left.value).toHaveLength(0);
});
