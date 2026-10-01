import { expect, type Page, type Request, test } from "@playwright/test";
import { createRow, ensureRow } from "../seed";
import {
  confirmation,
  dialog,
  drawer,
  fillNumber,
  pickItem,
  pickOption,
  pickRecipient,
  rowAction,
  saveEditor,
  search,
  toasts,
  waitForOverlayOpen,
} from "../vue";

// Unique per run: the parallel grave generation (graves.spec.ts) inserts
// invoices in bursts, and a stack that kept its data still holds the rows of
// earlier runs, so row-count and first-row assertions are invalid. A unique
// recipient makes this spec's assertions independent of both.
const RUN = Date.now();
const RECIPIENT_NAME = `E2E 9001 ${RUN}`;
const ITEM_NUMBER = `9001-${RUN}`;

/** The invoice editor's recipient picker. */
const RECIPIENT = "invoiceRecipient";

async function seedMasterData(page: Page): Promise<void> {
  // Direct API calls keep the test focused on the UI flow.
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureRow(page, "/odata/InvoiceItems", {
    Number: ITEM_NUMBER,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: null,
  });
  // Recipient names are not unique in the backend and the tests in this file
  // share the seeded recipient, so seed only when it does not exist yet —
  // otherwise the picker would hold duplicates and the strict locator would fail.
  const recipients = await (
    await page.request.get(
      `/odata/Recipients?$filter=Name eq '${encodeURIComponent(RECIPIENT_NAME)}'`,
    )
  ).json();
  if (!(recipients.value as unknown[]).length) {
    await createRow(page, "/odata/Recipients", {
      Name: RECIPIENT_NAME,
      Address: "Musterstraße 1\n12345 Musterstadt",
    });
  }
}

/** Opens the editor for a new invoice and waits until it can be typed into. */
async function openNewInvoice(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await waitForOverlayOpen(page);
  // the recipient list loads with the drawer; a fresh draft starts on the newest
  await expect(page.locator(`#${RECIPIENT}`)).not.toHaveText("Empfänger wählen");
}

/** One row of the invoice list. */
function invoiceRow(page: Page, text: string) {
  return page.getByRole("row").filter({ hasText: text });
}

test("create an invoice with a mandatory price and open its document (Story 4, 8)", async ({
  page,
}) => {
  await seedMasterData(page);
  await page.goto("/invoices");

  await openNewInvoice(page);
  // this run's recipient name is unique: picking it cannot collide with rows
  // created by other tests, parallel workers or previous runs
  await pickRecipient(page, RECIPIENT, RECIPIENT_NAME);
  await pickItem(page, 1, ITEM_NUMBER);
  const editor = drawer(page);
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 1" }), "2");
  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "120");
  // the running estimate follows the lines: 2 × 120,00 net, plus 7 %
  await expect(editor).toContainText("Summe ca.: 240,00 € netto, 256,80 € brutto");
  await saveEditor(editor);

  // saving closes the editor and lists a row for this run's unique recipient
  await expect(editor).toHaveCount(0);
  const row = invoiceRow(page, RECIPIENT_NAME).first();
  await expect(row).toBeVisible();

  // the normal document opens in a new tab, where the browser shows it
  // instead of downloading it
  const tabPromise = page.context().waitForEvent("page");
  const documentRequest = page
    .context()
    .waitForEvent("request", (request) =>
      /\/odata\/Documents\(\d+\)\/\$value$/.test(request.url()),
    );
  await row.getByRole("button", { name: rowAction("Rechnung herunterladen") }).click();
  const tab = await tabPromise;
  // what the tab renders is up to the browser — a headless one has no PDF
  // viewer — so the assertion is on the document it navigates to
  const documentUrl = (await documentRequest).url();
  await tab.close();

  // that document is the normal PDF, served for display instead of download
  const document = await page.request.get(documentUrl);
  expect(document.headers()["content-type"]).toBe("application/pdf");
  const disposition = document.headers()["content-disposition"] ?? "";
  expect(disposition).toContain("inline");
  expect(disposition).toContain(".pdf");
  expect(disposition).not.toContain("Kopie");
});

test("a recipient suggestion shows the first line of the address (Story 3)", async ({ page }) => {
  await seedMasterData(page);
  await page.goto("/invoices");
  await openNewInvoice(page);

  await page.locator(`#${RECIPIENT}`).click();
  const overlay = page.locator(".p-select-overlay");
  await overlay.locator("input.p-select-filter").fill(RECIPIENT_NAME);

  // the recipient is seeded with "Musterstraße 1\n12345 Musterstadt", so this
  // also pins that the secondary value is the first line and not the whole
  // address — an address runs over as many lines as the user typed
  const option = overlay.getByRole("option", { name: RECIPIENT_NAME });
  await expect(option).toContainText("Musterstraße 1");
  await expect(option).not.toContainText("12345 Musterstadt");
});

test("invoice without price cannot be saved (Story 4.3/edge case)", async ({ page }) => {
  await seedMasterData(page);
  await page.goto("/invoices");

  await openNewInvoice(page);
  await pickRecipient(page, RECIPIENT, RECIPIENT_NAME);
  await pickItem(page, 1, ITEM_NUMBER);
  await drawer(page).getByRole("button", { name: "Speichern" }).click();

  // the backend refuses the line, the editor stays open with the draft
  await expect(
    toasts(page).filter({ hasText: "Zu dieser Leistung fehlt ein Preis." }),
  ).toBeVisible();
  await expect(drawer(page).getByRole("combobox", { name: "Nummer 1" })).toContainText(ITEM_NUMBER);
});

test("create invoice with an on-the-fly recipient (Story 3.2)", async ({ page }) => {
  const ts = Date.now();
  const quickName = `Schnell ${ts}`;
  const itemNumber = `9004-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "E2E Schnell",
    VatRate: 7,
    DefaultPrice: null,
  });

  await page.goto("/invoices");
  await openNewInvoice(page);
  await drawer(page).getByRole("button", { name: "Neuer Empfänger" }).click();
  await waitForOverlayOpen(page);

  // the quick-create dialog sits on top of the invoice editor
  const quickDialog = dialog(page, "Neuer Empfänger");
  await quickDialog.getByLabel("Name", { exact: true }).fill(quickName);
  await quickDialog.getByLabel("Adresse").fill("Blitzweg 1\n12345 Blitzstadt");
  await quickDialog.getByRole("button", { name: "Speichern" }).click();

  // the dialog closes without leaving the invoice editor, which now shows the
  // newly created recipient as selected
  await expect(quickDialog).toHaveCount(0);
  await expect(page.locator(`#${RECIPIENT}`)).toHaveText(quickName);

  await pickItem(page, 1, itemNumber);
  await fillNumber(drawer(page).getByRole("spinbutton", { name: "Preis 1" }), "120");
  await saveEditor(drawer(page));

  await expect(invoiceRow(page, quickName)).toBeVisible();
});

test("editing an invoice creates a revision with -R1 number and badge (Story 9)", async ({
  page,
}) => {
  const ts = Date.now();
  const recipientName = `Revision ${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `9005-${ts}`,
    Description: "E2E Revision",
    VatRate: 7,
    DefaultPrice: null,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Revisionsweg 1",
  });
  const invoice = await createRow(page, "/odata/CreateInvoice", {
    recipientId: recipient.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
  });
  const number = invoice.InvoiceNumber as string;
  expect(number).toMatch(/^\d{4}-\d{4}$/); // freshly created: no -R suffix

  await page.goto("/invoices");
  const row = invoiceRow(page, number);
  await expect(row).toBeVisible();
  await expect(row.getByText("R1", { exact: true })).toHaveCount(0);

  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editor = drawer(page);
  // the editor opens on the invoice as it is
  await expect(page.locator(`#${RECIPIENT}`)).toHaveText(recipientName);
  await expect(editor.getByRole("spinbutton", { name: "Preis 1" })).toHaveValue("100,00");
  await fillNumber(editor.getByRole("spinbutton", { name: "Anzahl 1" }), "3");
  await fillNumber(editor.getByRole("spinbutton", { name: "Preis 1" }), "150");
  await saveEditor(editor);

  // The list shows the chain's number without the -R suffix and marks the
  // revision with a badge. That the revision exists under the suffixed
  // number is asserted against the API: the list does not print it.
  await expect(row.getByText("R1", { exact: true })).toBeVisible();
  const revisions = await (
    await page.request.get(`/odata/Invoices?$filter=InvoiceNumber eq '${number}-R1'`)
  ).json();
  expect(revisions.value).toHaveLength(1);
});

test("the list shows the invoice date, and marks revisions only (Story 9)", async ({ page }) => {
  const ts = Date.now();
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `DAT-${ts}`,
    Description: "E2E Datum",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const plain = await createRow(page, "/odata/Recipients", {
    Name: `Datum Original ${ts}`,
    Address: "Musterweg 1",
  });
  const revised = await createRow(page, "/odata/Recipients", {
    Name: `Datum Revision ${ts}`,
    Address: "Musterweg 2",
  });

  await createRow(page, "/odata/CreateInvoice", {
    recipientId: plain.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
  });
  const toRevise = await createRow(page, "/odata/CreateInvoice", {
    recipientId: revised.Id,
    lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
  });
  await createRow(page, "/odata/CreateRevision", {
    invoiceId: toRevise.Id,
    recipientId: revised.Id,
    lines: [{ itemId: item.Id, count: 2, unitPrice: 100 }],
  });

  await page.goto("/invoices");

  // Cells follow the column order: invoice number (with the revision badge),
  // recipient, date, payment, actions.
  const NUMBER = 0;
  const DATE = 2;

  const original = invoiceRow(page, `Datum Original ${ts}`).first();
  await expect(original).toBeVisible();
  await expect(original.getByRole("cell").nth(DATE)).toHaveText(/^\d{1,2}\.\d{1,2}\.\d{4}$/);
  // an original carries no revision marker at all. The number cell also holds
  // the recipient's name for a phone, hidden here, so this reads what is shown.
  await expect(original.getByRole("cell").nth(NUMBER)).toHaveText(/^\d{4}-\d{4}$/, {
    useInnerText: true,
  });

  const revisionRow = invoiceRow(page, `Datum Revision ${ts}`).first();
  await expect(revisionRow).toBeVisible();
  await expect(revisionRow.getByRole("cell").nth(DATE)).toHaveText(/^\d{1,2}\.\d{1,2}\.\d{4}$/);
  await expect(revisionRow.getByRole("cell").nth(NUMBER)).toContainText("R1");
});

/** The JSON body of the request that created an invoice. */
function createInvoicePayload(request: Request): { recipientId: number } {
  return request.postDataJSON() as { recipientId: number };
}

async function seedOwnMasterData(
  page: Page,
  marker: string,
): Promise<{ itemNumber: string; recipientName: string; recipientId: number }> {
  const itemNumber = `${marker}-item`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "E2E",
    VatRate: 7,
    DefaultPrice: null,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: marker,
    Address: "Musterweg 1\n12345 Musterstadt",
  });
  return { itemNumber, recipientName: marker, recipientId: recipient.Id };
}

async function countInvoices(page: Page, recipientId: number): Promise<number> {
  const response = await page.request.get(
    `/odata/Invoices?$filter=RecipientId eq ${recipientId}&$select=Id`,
  );
  return ((await response.json()).value as unknown[]).length;
}

async function fillLine(page: Page, itemNumber: string): Promise<void> {
  await pickItem(page, 1, itemNumber);
  await fillNumber(drawer(page).getByRole("spinbutton", { name: "Preis 1" }), "120");
}

test("the second invoice goes to the recipient the reopened editor shows", async ({ page }) => {
  const { itemNumber, recipientName } = await seedOwnMasterData(page, `Wieder ${Date.now()}`);
  await page.goto("/invoices");

  // first invoice: the recipient is picked from the list, as a user would
  await openNewInvoice(page);
  await pickRecipient(page, RECIPIENT, recipientName);
  await fillLine(page, itemNumber);
  await saveEditor(drawer(page));
  await expect(invoiceRow(page, recipientName)).toBeVisible();

  // second invoice: the editor is reopened and saved without touching the
  // recipient field. *Which* recipient it shows is not this test's business —
  // a fresh draft starts on the newest recipient, and a spec running beside
  // this one may have created one since. What must hold is that the save
  // invoices exactly the recipient on screen.
  await openNewInvoice(page);
  const shown = (await page.locator(`#${RECIPIENT}`).innerText()).trim();
  expect(shown).not.toBe("");

  await fillLine(page, itemNumber);
  const created = page.waitForRequest((request) => request.url().endsWith("/odata/CreateInvoice"));
  await saveEditor(drawer(page));
  const { recipientId } = createInvoicePayload(await created);

  // the first frontend once sent recipient 0 here, because the fresh draft
  // never picked up the selection it showed
  expect(recipientId).toBeGreaterThan(0);
  const sent = await (await page.request.get(`/odata/Recipients(${recipientId})`)).json();
  expect(sent.Name).toBe(shown);
  await expect.poll(() => countInvoices(page, recipientId)).toBeGreaterThan(0);
});

test("a double click on save creates one invoice, not two", async ({ page }) => {
  const { itemNumber, recipientName, recipientId } = await seedOwnMasterData(
    page,
    `Doppel ${Date.now()}`,
  );
  await page.goto("/invoices");

  await openNewInvoice(page);
  await pickRecipient(page, RECIPIENT, recipientName);
  await fillLine(page, itemNumber);

  // Counting the requests, not only the rows: the second request is sent
  // while the first one is still running, so a row count taken right after
  // the list refreshes can miss the invoice it is about to create.
  let created = 0;
  page.on("request", (request) => {
    if (request.url().endsWith("/odata/CreateInvoice")) {
      created++;
    }
  });

  // creating an invoice renders a PDF and takes a moment; an impatient second
  // click must not produce a second invoice (constitution VIII)
  await drawer(page).getByRole("button", { name: "Speichern" }).dblclick();
  await expect(drawer(page)).toHaveCount(0, { timeout: 30_000 });

  await expect(invoiceRow(page, recipientName)).toBeVisible();
  expect(created).toBe(1);
  await expect.poll(() => countInvoices(page, recipientId)).toBe(1);
});

test("the list shows non-grave invoices only and searches them (Story 9)", async ({ page }) => {
  const marker = `Suche ${Date.now()}`;
  const { itemNumber, recipientId, recipientName } = await seedOwnMasterData(page, marker);
  const item = await (
    await page.request.get(`/odata/InvoiceItems?$filter=Number eq '${itemNumber}'`)
  ).json();
  const invoice = await createRow(page, "/odata/CreateInvoice", {
    recipientId,
    lines: [{ itemId: item.value[0].Id, count: 1, unitPrice: 120 }],
  });

  // The list asks the server for non-grave invoices, latest revision only.
  // Asserting the request rather than counting rows keeps this independent of
  // the graves other specs invoice in parallel.
  const listRequest = page.waitForRequest((request) => {
    const url = decodeURIComponent(request.url());
    return (
      url.includes("/odata/Invoices") &&
      url.includes("GraveId eq null") &&
      url.includes("IsLatestRevision eq true")
    );
  });
  await page.goto("/invoices");
  await listRequest;
  await expect(invoiceRow(page, recipientName)).toBeVisible();

  // the search reaches the backend as its own query option
  const field = page.getByRole("textbox", { name: "Rechnung suchen" });
  const searched = (term: string) => (url: string) =>
    url.includes("/odata/Invoices") && url.includes(`search=${term}`);
  await search(page, field, recipientName, searched(recipientName));
  await expect(invoiceRow(page, recipientName)).toBeVisible();
  await expect(page.locator("tbody tr")).toHaveCount(1);

  // clearing the field restores the list
  const all = page.waitForResponse((response) => {
    const url = decodeURIComponent(response.url());
    return url.includes("/odata/Invoices") && !url.includes("search=");
  });
  await field.fill("");
  await all;
  await expect(invoiceRow(page, recipientName)).toBeVisible();

  // narrow it again for the row assertions below
  await search(page, field, recipientName, searched(recipientName));
  await expect(page.locator("tbody tr")).toHaveCount(1);

  // the row actions are icon-only, named by their labels
  const row = invoiceRow(page, recipientName);
  for (const name of [
    "Details",
    "Rechnung herunterladen",
    "Kopie herunterladen",
    "Bearbeiten",
    "Als bezahlt markieren",
  ]) {
    await expect(row.getByRole("button", { name: rowAction(name) })).toBeVisible();
  }

  // details opens the revision page for that invoice
  await row.getByRole("button", { name: rowAction("Details") }).click();
  await expect(page).toHaveURL(new RegExp(`/invoices/${invoice.RootId ?? invoice.Id}$`));
  await expect(page.getByRole("button", { name: /Original/ })).toBeVisible();
});

test("mark an invoice paid, filter by payment, and take it back (Story 4)", async ({ page }) => {
  // its own recipient and item, unique to this run: the unpaid-filter
  // assertion below would otherwise catch the invoice the first test in this
  // file leaves behind for the shared RECIPIENT_NAME, which stays unpaid
  const ts = Date.now();
  const recipientName = `Zahlung ${ts}`;
  const itemNumber = `9006-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "E2E Zahlung",
    VatRate: 7,
    DefaultPrice: null,
  });
  await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Zahlungsweg 1\n12345 Zahlungsstadt",
  });

  await page.goto("/invoices");

  // a row of this run's own, so the assertions below cannot pick up an invoice
  // another spec or an earlier run created
  await openNewInvoice(page);
  await pickRecipient(page, RECIPIENT, recipientName);
  await pickItem(page, 1, itemNumber);
  await fillNumber(drawer(page).getByRole("spinbutton", { name: "Anzahl 1" }), "1");
  await fillNumber(drawer(page).getByRole("spinbutton", { name: "Preis 1" }), "50");
  await saveEditor(drawer(page));

  const row = invoiceRow(page, recipientName).first();
  await expect(row).toContainText("unbezahlt");

  const marked = page.waitForResponse((response) => response.url().endsWith("/SetInvoicePaid"));
  await row.getByRole("button", { name: rowAction("Als bezahlt markieren") }).click();
  expect((await marked).ok()).toBe(true);
  await expect(row).toContainText("bezahlt");
  await expect(row).not.toContainText("unbezahlt");
  // a paid invoice cannot be revised, so its edit button is dead — and its
  // tooltip names the reason instead of staying "Bearbeiten"
  const edit = row.getByRole("button", { name: rowAction("Bearbeiten") });
  await expect(edit).toBeDisabled();
  await edit.hover();
  await expect(page.getByRole("tooltip")).toHaveText(
    "Eine bezahlte Rechnung kann nicht bearbeitet werden.",
  );

  // the unpaid filter drops it, the paid filter keeps it
  const paymentFilter = page.getByRole("combobox", { name: "Zahlung" });
  await pickOption(page, paymentFilter, "Nur unbezahlte");
  await expect(invoiceRow(page, recipientName)).toHaveCount(0);
  await pickOption(page, paymentFilter, "Nur bezahlte");
  await expect(invoiceRow(page, recipientName)).toHaveCount(1);
  await pickOption(page, paymentFilter, "Alle");

  // taking the payment back asks first
  await row.getByRole("button", { name: rowAction("Zahlung zurücknehmen") }).click();
  await waitForOverlayOpen(page);
  const confirm = confirmation(page);
  await expect(confirm).toContainText("Soll die Zahlung wirklich zurückgenommen werden?");
  const unmarked = page.waitForResponse((response) => response.url().endsWith("/SetInvoicePaid"));
  await confirm.getByRole("button", { name: "Ja" }).click();
  expect((await unmarked).ok()).toBe(true);
  await expect(row).toContainText("unbezahlt");
  await expect(row.getByRole("button", { name: rowAction("Bearbeiten") })).toBeEnabled();
});
