import { expect, type Locator, type Page, test } from "@playwright/test";
import { createRow, ensureRow } from "../seed";

// Unique per run: the parallel grave generation (graves.spec.ts) inserts
// invoices in bursts, and against the dev stack rows from earlier runs are
// still there, so row-count and first-row assertions are invalid. A unique
// recipient makes this spec's assertions independent of both.
// Deliberately contains the item number seeded below. Recipient and item are
// both ComboBoxes, both leave their suggestion list in the DOM after a pick,
// and both render sapMSLI rows, so a suggestion locator that named neither
// field would match the other's rows. This name keeps that collision permanent
// instead of leaving it to whether the clock happens to produce a timestamp
// containing the item number, which is how it first showed up.
const RECIPIENT_NAME = `E2E 9001 ${Date.now()}`;

/**
 * One field's suggestion rows. A ComboBox renders its list as
 * `<control id>-popup-list`, so naming the field keeps the two lists apart.
 * The item field sits in a table template and its id therefore carries a clone
 * suffix, which is why the field name is matched anywhere in the id.
 */
function suggestions(page: Page, field: string): Locator {
  return page.locator(`[id*='${field}'][id$='-popup-list'] li.sapMSLI`);
}

/** The invoice dialog's recipient input. */
function recipientField(page: Page): Locator {
  return page.locator(".sapMDialog input[id$='--recipient-inner']");
}

/** Picks a recipient the way the item field is driven: type, then click. */
async function pickRecipient(page: Page, name: string): Promise<void> {
  const input = recipientField(page);
  await input.click();
  await input.fill(name);
  await suggestions(page, "recipient").filter({ hasText: name }).click();
}

async function seedMasterData(page: Page): Promise<void> {
  // Direct API calls keep the test focused on the UI flow. VatRate 7 and
  // item 9001 may already exist from previous runs; the API then answers
  // 409, which is harmless because the existing rows are reused.
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureRow(page, "/odata/InvoiceItems", {
    Number: "9001",
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: null,
  });
  // Recipient names are not unique in the backend and both tests in this
  // file share the seeded recipient, so seed only when it does not exist
  // yet — otherwise the suggestion list would hold duplicates and the strict
  // locator would fail.
  const recipients = await (
    await page.request.get(
      `/odata/Recipients?$filter=Name eq '${encodeURIComponent(RECIPIENT_NAME)}'`,
    )
  ).json();
  if (!(recipients.value as any[]).length) {
    await createRow(page, "/odata/Recipients", {
      Name: RECIPIENT_NAME,
      Address: "Musterstraße 1\n12345 Musterstadt",
    });
  }
}

test("create an invoice with a mandatory price and download documents (Story 4, 8)", async ({
  page,
}) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");

  // the dialog's line table stays in the DOM after closing: scope to the
  // invoices table explicitly
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  // this run's recipient name is unique: picking it cannot collide with
  // rows created by other tests, parallel workers or previous runs
  await pickRecipient(page, RECIPIENT_NAME);
  // the item field is a typeahead too: type the number, then pick the suggestion
  const lineInput = page.locator(".sapMDialog input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill("9001");
  await suggestions(page, "lineItem").filter({ hasText: "9001" }).click();
  await page.getByLabel("Anzahl").fill("2");
  await page.getByLabel("Preis").fill("120");
  await page.getByRole("button", { name: "Speichern" }).click();

  // saving shows a row for this run's unique recipient. This fails if
  // CreateInvoice breaks, and is unaffected by parallel grave generation
  // (which creates rows for other recipients) or by an empty fresh DB.
  await expect(invoicesTable.locator("tbody tr", { hasText: RECIPIENT_NAME })).toBeVisible();

  // open the normal document from the row of this run's invoice, not from the
  // first row (which a parallel grave run may replace): it opens in a new tab
  // and the browser shows it instead of downloading it
  const tabPromise = page.context().waitForEvent("page");
  const documentRequest = page
    .context()
    .waitForEvent("request", (request) =>
      /\/odata\/Documents\(\d+\)\/\$value$/.test(request.url()),
    );
  await invoicesTable
    .locator("tbody tr", { hasText: RECIPIENT_NAME })
    .getByRole("button", { name: "Rechnung herunterladen", exact: true })
    .first()
    .click();
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
  await page.goto("/index.html#/invoices");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  const input = recipientField(page);
  await input.click();
  await input.fill(RECIPIENT_NAME);

  // the recipient is seeded with "Musterstraße 1\n12345 Musterstadt", so this
  // also pins that the secondary value is the first line and not the whole
  // address — an address runs over as many lines as the user typed
  const row = suggestions(page, "recipient").filter({ hasText: RECIPIENT_NAME });
  await expect(row).toContainText("Musterstraße 1");
  await expect(row).not.toContainText("12345 Musterstadt");
});

test("invoice without price cannot be saved (Story 4.3/edge case)", async ({ page }) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await pickRecipient(page, RECIPIENT_NAME);
  const lineInput = page.locator(".sapMDialog input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill("9001");
  await suggestions(page, "lineItem").filter({ hasText: "9001" }).click();
  await page.getByRole("button", { name: "Speichern" }).click();

  // the create dialog renders "Preis" as a column header and stays in the DOM
  // after closing, so an assertion on any .sapMDialog would pass without the
  // blocking error dialog. Scope to the MessageBox (exclusive class
  // sapMMessageBox) and assert the exact German error text
  // (i18n errorPriceRequired), which only the error dialog shows.
  await expect(page.locator(".sapMDialog.sapMMessageBox")).toContainText(
    "Zu dieser Leistung fehlt ein Preis.",
  );
});

test("create invoice with an on-the-fly recipient (Story 3.2)", async ({ page }) => {
  const ts = Date.now();
  const quickName = `Schnell ${ts}`;
  const itemNumber = `9004-${ts}`;
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("/odata/InvoiceItems", {
    data: {
      Number: itemNumber,
      Description: "E2E Schnell",
      VatRate: 7,
      DefaultPrice: null,
    },
  });

  await page.goto("/index.html#/invoices");
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await page.getByRole("button", { name: "Neuer Empfänger" }).click();

  // the quick-create dialog is a second dialog on top of the invoice dialog
  const quickDialog = page.locator("[id$='quickRecipientDialog']");
  await quickDialog.getByLabel("Name").fill(quickName);
  await quickDialog.getByLabel("Adresse").fill("Blitzweg 1\n12345 Blitzstadt");
  await quickDialog.getByRole("button", { name: "Speichern" }).click();

  // the quick dialog closes without leaving the invoice dialog, which now
  // shows the newly created recipient as selected
  await expect(quickDialog).toBeHidden();
  await expect(recipientField(page)).toHaveValue(quickName);

  const lineInput = page.locator(".sapMDialog input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill(itemNumber);
  await suggestions(page, "lineItem").filter({ hasText: itemNumber }).click();
  await page.getByLabel("Anzahl").fill("1");
  await page.getByLabel("Preis").fill("120");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(invoicesTable.locator("tbody tr", { hasText: quickName })).toBeVisible();
});

test("editing an invoice creates a revision with -R1 number and badge (Story 9)", async ({
  page,
}) => {
  const ts = Date.now();
  const recipientName = `Revision ${ts}`;
  const itemNumber = `9005-${ts}`;
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
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

  await page.goto("/index.html#/invoices");
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");
  const row = invoicesTable.locator("tbody tr", { hasText: number });
  await expect(row).toBeVisible();

  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await page.getByLabel("Anzahl").fill("3");
  await page.getByLabel("Preis").fill("150");
  await page.getByRole("button", { name: "Speichern" }).click();

  // the revision row shows the suffixed number and the "R1" badge
  const revisionRow = invoicesTable.locator("tbody tr", {
    hasText: `${number}-R1`,
  });
  await expect(revisionRow).toBeVisible();
  await expect(revisionRow.getByText("R1", { exact: true })).toBeVisible();
});

/** Seeds a VAT rate, one item and one recipient, all unique to this test. */
async function seedOwnMasterData(
  page: Page,
  marker: string,
): Promise<{ itemNumber: string; recipientName: string; recipientId: number }> {
  const itemNumber = `${marker}-item`;
  const recipientName = `${marker}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureRow(page, "/odata/InvoiceItems", {
    Number: itemNumber,
    Description: "E2E",
    VatRate: 7,
    DefaultPrice: null,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: recipientName,
    Address: "Musterweg 1\n12345 Musterstadt",
  });
  return { itemNumber, recipientName, recipientId: recipient.Id };
}

/** Invoices whose snapshotted recipient name matches, by name rather than id:
 *  the caller knows what the dialog displayed, not which row it came from. */
async function countInvoicesFor(page: Page, recipientName: string): Promise<number> {
  const filter = encodeURIComponent(`RecipientName eq '${recipientName.replace(/'/g, "''")}'`);
  const response = await page.request.get(`/odata/Invoices?$filter=${filter}&$select=Id`);
  return ((await response.json()).value as unknown[]).length;
}

async function countInvoices(page: Page, recipientId: number): Promise<number> {
  const response = await page.request.get(
    `/odata/Invoices?$filter=RecipientId eq ${recipientId}&$select=Id`,
  );
  return ((await response.json()).value as unknown[]).length;
}

async function fillLine(page: Page, itemNumber: string): Promise<void> {
  const lineInput = page.locator(".sapMDialog input[id*='lineItem'][id$='-inner']");
  await lineInput.click();
  await lineInput.fill(itemNumber);
  await suggestions(page, "lineItem").filter({ hasText: itemNumber }).click();
  await page.getByLabel("Preis").fill("120");
}

test("the second invoice goes to the recipient the reopened dialog shows", async ({ page }) => {
  const { itemNumber, recipientName } = await seedOwnMasterData(page, `Wieder ${Date.now()}`);
  await page.goto("/index.html#/invoices");
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

  // first invoice: the recipient is picked from the list, as a user would
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await pickRecipient(page, recipientName);
  await fillLine(page, itemNumber);
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();

  // second invoice: the dialog is reopened and saved without touching the
  // recipient field. *Which* recipient it shows is not this test's business — a
  // reopened dialog carries no draft selection, so the dialog starts on the
  // newest recipient, and a spec running beside this one may have created one
  // since. What must hold is that the save invoices exactly the recipient on
  // screen.
  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  const shown = (await recipientField(page).inputValue()).trim();
  expect(shown).not.toBe("");
  const before = await countInvoicesFor(page, shown);

  await fillLine(page, itemNumber);
  await page.getByRole("button", { name: "Speichern" }).click();

  // before the fix the second save sent recipient 0 and the backend answered
  // "Der Empfänger wurde nicht gefunden", because the fresh draft never picked
  // up the shown selection
  await expect.poll(() => countInvoicesFor(page, shown)).toBe(before + 1);
});

test("a double click on save creates one invoice, not two", async ({ page }) => {
  const { itemNumber, recipientName, recipientId } = await seedOwnMasterData(
    page,
    `Doppel ${Date.now()}`,
  );
  await page.goto("/index.html#/invoices");
  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await pickRecipient(page, recipientName);
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
  await page.getByRole("button", { name: "Speichern" }).dblclick();

  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();
  expect(created).toBe(1);
  await expect.poll(() => countInvoices(page, recipientId)).toBe(1);
});

test("the list shows non-grave invoices only and searches them (Story 9)", async ({ page }) => {
  const marker = `Suche ${Date.now()}`;
  const { itemNumber, recipientName } = await seedOwnMasterData(page, marker);
  const item = await (
    await page.request.get(`/odata/InvoiceItems?$filter=Number eq '${itemNumber}'`)
  ).json();
  const recipients = await (
    await page.request.get(`/odata/Recipients?$filter=Name eq '${recipientName}'`)
  ).json();
  await page.request.post("/odata/CreateInvoice", {
    data: {
      recipientId: recipients.value[0].Id,
      lines: [{ itemId: item.value[0].Id, count: 1, unitPrice: 120 }],
    },
  });

  // The list asks the server for non-grave invoices, latest revision only.
  // Asserting the request rather than counting rows keeps this independent of
  // the graves other specs invoice in parallel.
  const listRequest = page.waitForRequest(
    (request) =>
      request.url().includes("/odata/Invoices") &&
      decodeURIComponent(request.url()).includes("GraveId eq null and IsLatestRevision eq true"),
  );
  await page.goto("/index.html#/invoices");
  await listRequest;

  const invoicesTable = page.locator("[id$='invoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();

  // the search reaches the backend as the custom query option
  const search = page.locator("[id$='invoiceSearch'] input");
  const searchRequest = page.waitForRequest(
    (request) => request.url().includes("/odata/Invoices") && request.url().includes("search="),
  );
  await search.fill(recipientName);
  await search.press("Enter");
  await searchRequest;

  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(1);

  // clearing the field restores the list
  await search.fill("");
  await search.press("Enter");
  await expect(invoicesTable.locator("tbody tr", { hasText: recipientName })).toBeVisible();

  // narrow it again for the row assertions below
  await search.fill(recipientName);
  await search.press("Enter");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(1);

  // the four row actions are icon-only, named by their tooltips
  const row = invoicesTable.locator("tbody tr", { hasText: recipientName });
  for (const name of ["Details", "Rechnung herunterladen", "Kopie herunterladen", "Bearbeiten"]) {
    await expect(row.getByRole("button", { name, exact: true })).toBeVisible();
  }

  // details opens the revision page for that invoice
  await row.getByRole("button", { name: "Details", exact: true }).click();
  await expect(page.getByText("Original", { exact: true })).toBeVisible();
});
