import { expect, test } from "@playwright/test";

// Unique per run: the dev DB persists between runs and the parallel grave
// generation (graves.spec.ts) inserts invoices in bursts, so row-count and
// first-row assertions are invalid. A unique recipient makes this spec's
// assertions independent of counts and of pre-existing rows.
const RECIPIENT_NAME = `E2E ${Date.now()}`;

async function seedMasterData(
  page: import("@playwright/test").Page,
): Promise<void> {
  // Direct API calls keep the test focused on the UI flow. VatRate 7 and
  // item 9001 may already exist from previous runs; the API then answers
  // 409, which is harmless because the existing rows are reused.
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("http://localhost:8080/odata/InvoiceItems", {
    data: {
      Number: "9001",
      Description: "Grabpflege",
      VatRate: 7,
      DefaultPrice: null,
    },
  });
  // Recipient names are not unique in the backend and both tests in this
  // file share the seeded recipient, so seed only when it does not exist
  // yet — otherwise the select list would hold duplicates and the strict
  // locator would fail.
  const recipients = await (
    await page.request.get(
      `http://localhost:8080/odata/Recipients?$filter=Name eq '${encodeURIComponent(RECIPIENT_NAME)}'`,
    )
  ).json();
  if (!(recipients.value as any[]).length) {
    await page.request.post("http://localhost:8080/odata/Recipients", {
      data: {
        Name: RECIPIENT_NAME,
        Address: "Musterstraße 1\n12345 Musterstadt",
      },
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
  // fragment controls are prefixed with the view id; the visual select root
  // is the first .sapMSlt inside the dialog (the hidden native select is not)
  await page.locator(".sapMDialog .sapMSlt").first().click(); // recipient Select opens popover
  // this run's recipient name is unique: selecting it cannot collide with
  // rows created by other tests, parallel workers or previous runs
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: RECIPIENT_NAME,
    })
    .click();
  // the item field is a typeahead input: type the number, then pick the suggestion
  const lineInput = page.locator(
    ".sapMDialog input[id*='lineItem'][id$='-inner']",
  );
  await lineInput.click();
  await lineInput.fill("9001");
  await page.locator("div.sapMPopover li", { hasText: "9001" }).click();
  await page.getByLabel("Anzahl").fill("2");
  await page.getByLabel("Preis").fill("120");
  await page.getByRole("button", { name: "Speichern" }).click();

  // saving shows a row for this run's unique recipient. This fails if
  // CreateInvoice breaks, and is unaffected by parallel grave generation
  // (which creates rows for other recipients) or by an empty fresh DB.
  await expect(
    invoicesTable.locator("tbody tr", { hasText: RECIPIENT_NAME }),
  ).toBeVisible();

  // download the normal document from the row of this run's invoice,
  // not from the first row (which a parallel grave run may replace)
  const downloadPromise = page.waitForEvent("download");
  await invoicesTable
    .locator("tbody tr", { hasText: RECIPIENT_NAME })
    .getByRole("button", { name: "Rechnung", exact: true })
    .first()
    .click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toContain(".pdf");
  expect(download.suggestedFilename()).not.toContain("Kopie");
});

test("invoice without price cannot be saved (Story 4.3/edge case)", async ({
  page,
}) => {
  await seedMasterData(page);
  await page.goto("/index.html#/invoices");

  await page.getByRole("button", { name: "Rechnung erstellen" }).click();
  await page.locator(".sapMDialog .sapMSlt").first().click();
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: RECIPIENT_NAME,
    })
    .click();
  const lineInput = page.locator(
    ".sapMDialog input[id*='lineItem'][id$='-inner']",
  );
  await lineInput.click();
  await lineInput.fill("9001");
  await page.locator("div.sapMPopover li", { hasText: "9001" }).click();
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

test("create invoice with an on-the-fly recipient (Story 3.2)", async ({
  page,
}) => {
  const ts = Date.now();
  const quickName = `Schnell ${ts}`;
  const itemNumber = `9004-${ts}`;
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("http://localhost:8080/odata/InvoiceItems", {
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
  await expect(page.locator(".sapMDialog .sapMSlt").first()).toContainText(
    quickName,
  );

  const lineInput = page.locator(
    ".sapMDialog input[id*='lineItem'][id$='-inner']",
  );
  await lineInput.click();
  await lineInput.fill(itemNumber);
  await page.locator("div.sapMPopover li", { hasText: itemNumber }).click();
  await page.getByLabel("Anzahl").fill("1");
  await page.getByLabel("Preis").fill("120");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(
    invoicesTable.locator("tbody tr", { hasText: quickName }),
  ).toBeVisible();
});

test("editing an invoice creates a revision with -R1 number and badge (Story 9)", async ({
  page,
}) => {
  const ts = Date.now();
  const recipientName = `Revision ${ts}`;
  const itemNumber = `9005-${ts}`;
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  const item = await (
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
      data: {
        Number: itemNumber,
        Description: "E2E Revision",
        VatRate: 7,
        DefaultPrice: null,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("http://localhost:8080/odata/Recipients", {
      data: { Name: recipientName, Address: "Revisionsweg 1" },
    })
  ).json();
  const invoice = await (
    await page.request.post("http://localhost:8080/odata/CreateInvoice", {
      data: {
        recipientId: recipient.Id,
        lines: [{ itemId: item.Id, count: 1, unitPrice: 100 }],
      },
    })
  ).json();
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
