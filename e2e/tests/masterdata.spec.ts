import { expect, test } from "@playwright/test";
import { createRow, ensureCemetery, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";
import {
  confirmation,
  dialog,
  filterColumn,
  pickOption,
  searchColumn,
  toasts,
  waitForOverlayOpen,
} from "../vue";

test("settings: stamp word validation and VAT rate (Story 1)", async ({ page }) => {
  await page.goto("/settings");
  const stampWord = page.getByLabel("Stempelwort");
  // the form is filled in from the settings once they have loaded
  await expect(stampWord).not.toHaveValue("");

  await stampWord.fill("");
  await page.getByRole("button", { name: "Speichern" }).click();
  // the backend refuses, and says why
  await expect(
    toasts(page).filter({ hasText: "Das Stempelwort darf nicht leer sein." }),
  ).toBeVisible();

  await stampWord.fill("Kopie");
  await page.getByLabel("MwSt.-Satz", { exact: true }).fill("7");
  // other specs seed 7 % as well; finding it configured already is refused with
  // a toast of its own, which leaves the list just as this test wants it
  await page.getByRole("button", { name: "MwSt.-Satz hinzufügen" }).click();
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(toasts(page).filter({ hasText: "Gespeichert." })).toBeVisible();

  const rates = page.locator("table", {
    has: page.getByRole("columnheader", { name: "MwSt.-Satz" }),
  });
  await expect(rates.getByRole("cell", { name: "7 %", exact: true })).toBeVisible();

  // the ending text survives typing, save and a reload
  const endingText = `E2E Schlusstext ${Date.now()}`;
  await page.getByLabel("Schlusstext").fill(endingText);
  const saved = page.waitForResponse(
    (response) =>
      response.url().includes("/odata/Settings") && response.request().method() === "PATCH",
  );
  await page.getByRole("button", { name: "Speichern" }).click();
  expect((await saved).ok()).toBe(true);
  await page.reload();
  await expect(page.getByLabel("Schlusstext")).toHaveValue(endingText);
  await expect(page.getByLabel("Stempelwort")).toHaveValue("Kopie");
});

test("settings: a cemetery is added, renamed for its graves and kept while they lie there", async ({
  page,
}) => {
  const ts = unique();
  const name = `Südfriedhof ${ts}`;
  const renamed = `Hauptfriedhof ${ts}`;
  const unused = `Leerer Friedhof ${ts}`;
  await page.goto("/settings");
  const cemeteries = page.locator("table", {
    has: page.getByRole("columnheader", { name: "Name", exact: true }),
  });
  const field = page.getByLabel("Name", { exact: true });

  await field.fill(name);
  await page.getByRole("button", { name: "Friedhof hinzufügen" }).click();
  await expect(cemeteries.getByRole("row").filter({ hasText: name })).toBeVisible();
  await expect(field).toHaveValue("");

  // the same name in other letters is the same cemetery, umlauts included
  await field.fill(name.toUpperCase());
  await page.getByRole("button", { name: "Friedhof hinzufügen" }).click();
  await expect(
    toasts(page).filter({ hasText: "Ein Friedhof mit diesem Namen existiert bereits." }),
  ).toBeVisible();

  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `FH-${ts}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 10,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Friedhof Beispiel ${ts}`,
    Address: "Friedhofsweg 1",
  });
  const graveId = await seedGrave(page, {
    graveNumber: `FH-${ts}`,
    cemeteryId: await ensureCemetery(page, name),
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  // a rename is the grave's new cemetery name too: it refers to the cemetery
  await cemeteries
    .getByRole("row")
    .filter({ hasText: name })
    .getByRole("button", { name: `Friedhof umbenennen: ${name}` })
    .click();
  await waitForOverlayOpen(page);
  const renameDialog = dialog(page, "Friedhof umbenennen");
  await expect(renameDialog.getByLabel("Name")).toHaveValue(name);
  await renameDialog.getByLabel("Name").fill(renamed);
  await renameDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(renameDialog).toHaveCount(0);
  const row = cemeteries.getByRole("row").filter({ hasText: renamed });
  await expect(row).toBeVisible();
  await expect(cemeteries).not.toContainText(name);

  // refused while the grave lies there, and asked about first
  await row.getByRole("button", { name: `Löschen: ${renamed}` }).click();
  await confirmation(page).getByRole("button", { name: "Ja" }).click();
  await expect(
    toasts(page).filter({ hasText: "Auf diesem Friedhof liegen noch Gräber." }),
  ).toBeVisible();
  await expect(row).toBeVisible();

  // one without graves goes
  await createRow(page, "/odata/Cemeteries", { Name: unused });
  await page.reload();
  const unusedRow = cemeteries.getByRole("row").filter({ hasText: unused });
  await unusedRow.getByRole("button", { name: `Löschen: ${unused}` }).click();
  await confirmation(page).getByRole("button", { name: "Ja" }).click();
  await expect(unusedRow).toHaveCount(0);
  await expect(row).toBeVisible();

  await page.goto(`/graves/${graveId}`);
  await expect(page.getByText(renamed, { exact: true })).toBeVisible();
});

test("items: create an item without default price (Story 2)", async ({ page }) => {
  const number = `1001-${unique()}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await page.goto("/items");

  await page.getByRole("button", { name: "Leistung anlegen" }).click();
  await waitForOverlayOpen(page);
  const itemDialog = dialog(page, "Leistung anlegen");
  await itemDialog.getByLabel("Nummer").fill(number);
  await itemDialog.getByLabel("Bezeichnung").fill("Grabpflege");
  // the rate is a Select; its label is not wired to it, so it goes by its id
  await pickOption(page, itemDialog.locator("#itemVatRate"), "7 %");
  // the default price stays empty: an item without one
  await itemDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(itemDialog).toHaveCount(0);

  const row = page.getByRole("row").filter({ hasText: number });
  await expect(row).toContainText("Grabpflege");
  await expect(row).toContainText("7 %");
  await expect(row).not.toContainText("€");
});

test("items: each column filters on its own", async ({ page }) => {
  const ts = unique();
  const allYear = `SA-${ts}`;
  const summer = `SB-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureRow(page, "/odata/VatRates", { Rate: 19 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: allYear,
    Description: "Grabpflege Ganzjahr",
    VatRate: 7,
    DefaultPrice: 12.5,
  });
  await createRow(page, "/odata/InvoiceItems", {
    Number: summer,
    Description: "Bepflanzung Sommer",
    VatRate: 19,
    DefaultPrice: 40,
    Quarter: "Summer",
  });

  await page.goto("/items");
  // the marker narrows the shared catalogue to this test's two items
  await searchColumn(page, "Nummer", ts);
  const table = page.getByRole("table");
  await expect(table).toContainText(allYear);
  await expect(table).toContainText(summer);

  await filterColumn(page, "Quartal", "Sommer");
  await expect(table).toContainText(summer);
  await expect(table).not.toContainText(allYear);

  await filterColumn(page, "Quartal", "Alle");
  await filterColumn(page, "MwSt.-Satz", "7 %");
  await expect(table).toContainText(allYear);
  await expect(table).not.toContainText(summer);
  await filterColumn(page, "MwSt.-Satz", "Alle");

  // the words of a term match in any order
  await searchColumn(page, "Bezeichnung", "Sommer Bepflanzung");
  await expect(table).toContainText(summer);
  await expect(table).not.toContainText(allYear);
  await searchColumn(page, "Bezeichnung", "");

  await searchColumn(page, "Standardpreis", "12,50");
  await expect(table).toContainText(allYear);
  await expect(table).not.toContainText(summer);
});

test("items: the price is found as shown and as typed", async ({ page }) => {
  const ts = unique();
  const large = `PG-${ts}`;
  const small = `PK-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: large,
    Description: "Grabanlage",
    VatRate: 7,
    DefaultPrice: 1234.56,
  });
  await createRow(page, "/odata/InvoiceItems", {
    Number: small,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 12.5,
  });

  await page.goto("/items");
  // the marker narrows the shared catalogue to this test's two items
  await searchColumn(page, "Nummer", ts);
  const table = page.getByRole("table");
  await expect(table).toContainText(large);
  await expect(table).toContainText(small);

  // the same forms the invoice list's total takes
  for (const term of ["1.234,56 €", "1.234,56", "1234,56", "1234.56"]) {
    await searchColumn(page, "Standardpreis", term);
    await expect(table, term).toContainText(large);
    await expect(table, term).not.toContainText(small);
  }
  for (const term of ["12,50 €", "12.50", "12,5"]) {
    await searchColumn(page, "Standardpreis", term);
    await expect(table, term).toContainText(small);
    await expect(table, term).not.toContainText(large);
  }
});

test("items: edit an existing item via the row dialog (Story 2)", async ({ page }) => {
  const ts = unique();
  const number = `E2E-ITEM-${ts}`;
  const original = `Original ${ts}`;
  const changed = `Geändert ${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await createRow(page, "/odata/InvoiceItems", {
    Number: number,
    Description: original,
    VatRate: 7,
    DefaultPrice: null,
  });

  await page.goto("/items");
  const row = page.getByRole("row").filter({ hasText: number });
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);

  // the dialog pre-fills the row's current values
  const itemDialog = dialog(page, "Leistung bearbeiten");
  await expect(itemDialog.getByLabel("Nummer")).toHaveValue(number);
  await expect(itemDialog.getByLabel("Bezeichnung")).toHaveValue(original);
  await itemDialog.getByLabel("Bezeichnung").fill(changed);
  await itemDialog.getByRole("button", { name: "Speichern" }).click();

  // the changed description replaces the original one in the row
  await expect(itemDialog).toHaveCount(0);
  await expect(row).toContainText(changed);
  await expect(row).not.toContainText(original);
});

test("items: the edit dialog lists the graves billing the item under their price (Story 2)", async ({
  page,
}) => {
  const ts = unique();
  const number = `E2E-USED-${ts}`;
  const graveNumber = `E2E-USED-G-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: number,
    Description: `Verwendet ${ts}`,
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Nutzer ${ts}`,
    Address: "Musterweg 1",
  });
  // no price on the line, so the grave bills the item's default
  await seedGrave(page, {
    graveNumber,
    items: [{ itemId: item.Id, count: 1, unitPrice: null }],
    recipientIds: [recipient.Id],
  });

  await page.goto("/items");
  await page
    .getByRole("row")
    .filter({ hasText: number })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);

  // the grave bills the item's default price, so it is listed under it, and
  // the rate is fixed while a grave bills the item
  const itemDialog = dialog(page, "Leistung bearbeiten");
  await expect(itemDialog).toContainText("Standardpreis 100,00 €");
  await expect(itemDialog.getByRole("link", { name: graveNumber })).toBeVisible();
  await expect(itemDialog).toContainText("MwSt.-Satz und Quartal sind festgelegt");

  // the grave's number leads to the grave
  await itemDialog.getByRole("link", { name: graveNumber }).click();
  await expect(page.getByRole("heading", { level: 1, name: graveNumber })).toBeVisible();
});

test("items: the quarter is chosen in the dialog and fixed while a grave bills the item", async ({
  page,
}) => {
  const ts = unique();
  const number = `E2E-Q-${ts}`;
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await page.goto("/items");

  await page.getByRole("button", { name: "Leistung anlegen" }).click();
  await waitForOverlayOpen(page);
  const createDialog = dialog(page, "Leistung anlegen");
  await createDialog.getByLabel("Nummer").fill(number);
  await createDialog.getByLabel("Bezeichnung").fill("Herbstbepflanzung");
  await pickOption(page, createDialog.locator("#itemVatRate"), "7 %");
  await pickOption(page, createDialog.locator("#itemQuarter"), "Herbst");
  await createDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(createDialog).toHaveCount(0);

  const row = page.getByRole("row").filter({ hasText: number });
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editDialog = dialog(page, "Leistung bearbeiten");
  await expect(editDialog.locator("#itemQuarter")).toContainText("Herbst");
  // nothing bills the item yet
  await expect(editDialog.locator("#itemQuarter")).not.toHaveAttribute("aria-disabled", "true");
  await editDialog.getByRole("button", { name: "Abbrechen" }).click();
  await expect(editDialog).toHaveCount(0);

  // a grave that bills an item fixes its quarter
  const itemRow = await createRow(page, "/odata/InvoiceItems", {
    Number: `${number}-used`,
    Description: `Verwendet ${ts}`,
    VatRate: 7,
    DefaultPrice: 10,
    Quarter: "Winter",
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Quartal ${ts}`,
    Address: "Musterweg 1",
  });
  // a grave needs an item billed all year as well
  const allYear = await createRow(page, "/odata/InvoiceItems", {
    Number: `${number}-year`,
    Description: `Ganzjährig ${ts}`,
    VatRate: 7,
    DefaultPrice: 10,
  });
  await seedGrave(page, {
    graveNumber: `E2E-Q-G-${ts}`,
    items: [
      { itemId: itemRow.Id, count: 1, unitPrice: null },
      { itemId: allYear.Id, count: 1, unitPrice: null },
    ],
    recipientIds: [recipient.Id],
  });

  await page.reload();
  await page
    .getByRole("row")
    .filter({ hasText: `${number}-used` })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);
  const usedDialog = dialog(page, "Leistung bearbeiten");
  await expect(usedDialog.locator("#itemQuarter")).toContainText("Winter");
  await expect(usedDialog.locator("#itemQuarter")).toHaveAttribute("aria-disabled", "true");
});

test("recipients: create and search a recipient (Story 3)", async ({ page }) => {
  const ts = unique();
  const first = `Familie Mahr ${ts}`;
  const second = `Zweiter Empfänger ${ts}`;
  await page.goto("/recipients");

  const createRecipient = async (name: string) => {
    await page.getByRole("button", { name: "Empfänger anlegen" }).click();
    await waitForOverlayOpen(page);
    const recipientDialog = dialog(page, "Empfänger anlegen");
    await recipientDialog.getByLabel("Name", { exact: true }).fill(name);
    await recipientDialog.getByLabel("Adresse").fill("Musterstraße 1\n12345 Musterstadt");
    await recipientDialog.getByRole("button", { name: "Speichern" }).click();
    // wait until the dialog has fully closed before reopening it
    await expect(recipientDialog).toHaveCount(0);
    await expect(page.getByRole("row").filter({ hasText: name })).toBeVisible();
  };

  await createRecipient(first);
  await createRecipient(second);

  // the number was left empty, so the backend assigned one
  await expect(page.getByRole("row").filter({ hasText: first })).toContainText(/E-\d+/);

  // search must actually narrow the list: this term matches only the first one
  await searchColumn(
    page,
    "Name",
    `mahr ${ts}`,
    // the search ignores case: the term is lower-cased, the names are not
    (url) => url.includes("/odata/Recipients") && url.includes(`name=mahr ${ts}`),
  );
  const table = page.getByRole("table");
  await expect(table).toContainText(first);
  await expect(table).not.toContainText(second);
});

test("recipients: the search finds the words of a term in any order", async ({ page }) => {
  const ts = unique();
  const wanted = `Bauer Hanna ${ts}`;
  const other = `Bauer Fritz ${ts}`;
  for (const name of [wanted, other]) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" });
  }

  await page.goto("/recipients");
  await searchColumn(
    page,
    "Name",
    `Hanna Bauer ${ts}`,
    (url) => url.includes("/odata/Recipients") && url.includes(`name=Hanna Bauer ${ts}`),
  );
  const table = page.getByRole("table");
  await expect(table).toContainText(wanted);
  await expect(table).not.toContainText(other);
});

test("recipients: the search looks in the address as well as the name", async ({ page }) => {
  const ts = unique();
  const wanted = `Lina Lindenweg-Test ${ts}`;
  const other = `Anton Amselweg-Test ${ts}`;
  await createRow(page, "/odata/Recipients", {
    Name: wanted,
    Address: "Lindenallee 5\n97070 Würzburg",
  });
  await createRow(page, "/odata/Recipients", {
    Name: other,
    Address: "Amselgasse 1\n97070 Würzburg",
  });

  await page.goto("/recipients");
  // one word from the address, one from the name
  await searchColumn(
    page,
    "Adresse",
    "lindenallee",
    (url) => url.includes("/odata/Recipients") && url.includes("address=lindenallee"),
  );
  await searchColumn(
    page,
    "Name",
    `Lindenweg-Test ${ts}`,
    (url) => url.includes("address=lindenallee") && url.includes(`name=Lindenweg-Test ${ts}`),
  );
  const table = page.getByRole("table");
  await expect(table).toContainText(wanted);
  await expect(table).not.toContainText(other);
});

test("recipients: each column searches its own field", async ({ page }) => {
  const ts = unique();
  const inAddress = `Erna ‹m› ${ts}`;
  const inName = `Ahorn ‹m› ${ts}`;
  await createRow(page, "/odata/Recipients", { Name: inAddress, Address: `Ahornweg ‹m› ${ts}` });
  await createRow(page, "/odata/Recipients", { Name: inName, Address: `Birkenweg ‹m› ${ts}` });

  await page.goto("/recipients");
  const table = page.getByRole("table");

  // "ahorn" is in the first one's address and the second one's name
  await searchColumn(
    page,
    "Name",
    `ahorn ‹m› ${ts}`,
    (url) => url.includes("/odata/Recipients") && url.includes(`name=ahorn ‹m› ${ts}`),
  );
  await expect(table).toContainText(inName);
  await expect(table).not.toContainText(inAddress);
  await expect(page).toHaveURL(/[?&]name=/);

  // a reload shows the same filtered list
  await page.reload();
  await expect(page.getByRole("textbox", { name: "Name durchsuchen" })).toHaveValue(
    `ahorn ‹m› ${ts}`,
  );
  await expect(table).toContainText(inName);
  await expect(table).not.toContainText(inAddress);

  // the other column, the name filter cleared
  await searchColumn(page, "Name", "");
  await searchColumn(
    page,
    "Adresse",
    `ahornweg ‹m› ${ts}`,
    (url) => url.includes(`address=ahornweg ‹m› ${ts}`) && !url.includes("name="),
  );
  await expect(table).toContainText(inAddress);
  await expect(table).not.toContainText(inName);
});

test("recipients: a filter with special characters", async ({ page }) => {
  const ts = unique();
  // ' & % and + each have a meaning in a URL; the decoy differs from the
  // wanted name only in the +, which a wrongly encoded query would read as a space
  const wanted = `O'Brien & Söhne 100% a+b ‹m› ${ts}`;
  const decoy = `O'Brien & Söhne 100% a b ‹m› ${ts}`;
  for (const name of [wanted, decoy]) {
    await createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" });
  }

  await page.goto("/recipients");
  const term = `o'brien & söhne 100% a+b ‹m› ${ts}`;
  await searchColumn(
    page,
    "Name",
    term,
    // the helper reads a literal "+" on the wire as a space; a "+" that reaches
    // the backend as itself was sent as %2B
    (url) => url.includes("/odata/Recipients") && url.includes(`name=${term}`),
  );
  const table = page.getByRole("table");
  await expect(table).toContainText(wanted);
  await expect(table).not.toContainText(decoy);
});

test("recipients: the list shows a hundred a page, of those the search leaves", async ({
  page,
}) => {
  const ts = unique();
  const names = Array.from(
    { length: 101 },
    (_, i) => `Seite ${ts} ${String(i + 1).padStart(3, "0")}`,
  );
  for (let start = 0; start < names.length; start += 10) {
    await Promise.all(
      names
        .slice(start, start + 10)
        .map((name) => createRow(page, "/odata/Recipients", { Name: name, Address: "Weg 1" })),
    );
  }

  await page.goto("/recipients");
  await searchColumn(
    page,
    "Name",
    `Seite ${ts}`,
    (url) => url.includes("/odata/Recipients") && url.includes(`name=Seite ${ts}`),
  );
  const rows = page.getByRole("row").filter({ hasText: `Seite ${ts}` });
  await expect(rows).toHaveCount(100);
  await expect(page.getByText("1–100 von 101")).toBeVisible();

  // a hundred rows are taller than the window, and the paginator stays in it
  const next = page.getByRole("button", { name: "Nächste Seite" });
  await expect(next).toBeInViewport();
  await next.click();
  await expect(rows).toHaveCount(1);
  await expect(page.getByText("101–101 von 101")).toBeVisible();
});

test("recipients: an e-mail address is optional and checked", async ({ page }) => {
  const ts = unique();
  const name = `Mit E-Mail ${ts}`;
  const email = `post.${ts}@example.de`;
  await page.goto("/recipients");

  await page.getByRole("button", { name: "Empfänger anlegen" }).click();
  await waitForOverlayOpen(page);
  const createDialog = dialog(page, "Empfänger anlegen");
  await createDialog.getByLabel("Name", { exact: true }).fill(name);
  await createDialog.getByLabel("Adresse").fill("Musterstraße 1");
  await createDialog.getByLabel("E-Mail").fill(` ${email} `);
  await createDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(createDialog).toHaveCount(0);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toContainText(email);

  // a malformed one is refused and the dialog stays open with it
  await row.getByRole("button", { name: "Bearbeiten" }).click();
  await waitForOverlayOpen(page);
  const editDialog = dialog(page, "Empfänger bearbeiten");
  await expect(editDialog.getByLabel("E-Mail")).toHaveValue(email);
  await editDialog.getByLabel("E-Mail").fill("post at example.de");
  await editDialog.getByRole("button", { name: "Speichern" }).click();
  await expect(toasts(page).filter({ hasText: "Die E-Mail-Adresse ist ungültig." })).toBeVisible();
  await expect(editDialog).toBeVisible();
  await expect(row).toContainText(email);
});

test("recipients: edit an existing recipient via the row dialog (Story 3)", async ({ page }) => {
  const ts = unique();
  const name = `Vorher ${ts}`;
  const changed = `Geändert ${ts}`;
  await createRow(page, "/odata/Recipients", { Name: name, Address: "Alte Gasse 1" });
  // A fresh invoice draft starts on the newest recipient, and an invoice keeps
  // the name its recipient had when it was saved. Renaming the newest
  // recipient while invoices.spec.ts reopens its draft would hand that spec a
  // name that changed under it; one more recipient keeps this one from ever
  // being the newest once the rename can happen.
  await createRow(page, "/odata/Recipients", { Name: `Danach ${ts}`, Address: "Neue Gasse 1" });

  await page.goto("/recipients");
  await page
    .getByRole("row")
    .filter({ hasText: name })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await waitForOverlayOpen(page);

  // the dialog pre-fills the row's current values
  const recipientDialog = dialog(page, "Empfänger bearbeiten");
  await expect(recipientDialog.getByLabel("Name", { exact: true })).toHaveValue(name);
  await expect(recipientDialog.getByLabel("Adresse")).toHaveValue("Alte Gasse 1");
  await recipientDialog.getByLabel("Name", { exact: true }).fill(changed);
  await recipientDialog.getByRole("button", { name: "Speichern" }).click();

  // the old name is gone, the new one is shown
  await expect(recipientDialog).toHaveCount(0);
  await expect(page.getByRole("row").filter({ hasText: changed })).toBeVisible();
  await expect(page.getByRole("table")).not.toContainText(name);
});

test("graves: search by number narrows the list and empty search shows nothing (Story 5.2)", async ({
  page,
}) => {
  const ts = unique();
  const graveNumber = `SUCHE-${ts}`;
  // a grave cannot be saved without a recipient, and needs an item billed all
  // year; a generation running alongside may invoice the grave, which leaves
  // it in the list all the same
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `SUCHE-${ts}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 10,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Erika Beispiel ${ts}`,
    Address: "Musterweg 1",
  });
  await seedGrave(page, {
    graveNumber,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  // wide enough for the overview's filter row, which comes from Tailwind's 2xl on
  await page.setViewportSize({ width: 1536, height: 720 });
  await page.goto("/graves");
  const table = page.getByRole("table");
  // graves still open for the selected half-year are listed first, so a
  // freshly created one is in the table right away
  await expect(table).toContainText(graveNumber);

  await searchColumn(page, "Grabnummer", graveNumber);
  // the unique number matches exactly this run's grave, nothing else
  await expect(table.locator("tbody tr")).toHaveCount(1);
  await expect(table.locator("tbody tr")).toContainText(graveNumber);

  await searchColumn(page, "Grabnummer", `gibtsnicht${ts}`);
  // no match: the table renders its German empty state and this run's grave is gone
  await expect(table).toContainText("Keine Daten");
  await expect(table).not.toContainText(graveNumber);
});
