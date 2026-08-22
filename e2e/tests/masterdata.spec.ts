import { test, expect } from "@playwright/test";

test("settings: stamp word validation and VAT rate (Story 1)", async ({ page }) => {
    await page.goto("/index.html");

    await page.getByLabel("Stempelwort").fill("");
    await page.getByRole("button", { name: "Speichern" }).click();
    await expect(page.locator(".sapMDialog")).toContainText("Stempelwort");

    // the error is a blocking dialog: dismiss it before continuing
    await page.locator(".sapMDialog").getByRole("button").click();
    await expect(page.locator(".sapMDialog")).toHaveCount(0);

    await page.getByLabel("Stempelwort").fill("Kopie");
    await page.getByLabel("VAT-Satz").fill("7");
    await page.getByRole("button", { name: "VAT-Satz hinzufügen" }).click();
    // on reruns the rate may already exist: dismiss the blocking error dialog
    const errorDialog = page.locator(".sapMDialog");
    await errorDialog.waitFor({ state: "attached", timeout: 2000 }).catch(() => {});
    if (await errorDialog.count()) {
        await errorDialog.getByRole("button").click();
        await expect(errorDialog).toHaveCount(0);
    }
    await page.getByRole("button", { name: "Speichern" }).click();

    await expect(page.locator(".sapMListTbl")).toContainText("7");
});

test("items: create an item without default price (Story 2)", async ({ page }) => {
    await page.request.post("http://localhost:8080/odata/VatRates", { data: { Rate: 7 } });
    await page.goto("/index.html#/items");
    await page.getByRole("button", { name: "Leistung anlegen" }).click();
    // scope to the dialog: the page search field's placeholder also contains "Nummer"
    await page.locator(".sapMDialog").getByLabel("Nummer").fill("1001");
    await page.getByLabel("Bezeichnung").fill("Grabpflege");
    await page.getByLabel("VAT-Satz").fill("7");
    await page.getByRole("button", { name: "Speichern" }).click();

    await expect(page.locator("table")).toContainText("1001");
    await expect(page.locator("table")).toContainText("Grabpflege");
});

test("items: edit an existing item via the row dialog (Story 2)", async ({ page }) => {
    const ts = Date.now();
    const number = `E2E-ITEM-${ts}`;
    const original = `Original ${ts}`;
    const changed = `Geändert ${ts}`;
    await page.request.post("http://localhost:8080/odata/VatRates", { data: { Rate: 7 } });
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
        data: { Number: number, Description: original, VatRate: 7, DefaultPrice: null },
    });

    await page.goto("/index.html#/items");
    const itemsTable = page.locator("[id$='itemsTable-listUl']");
    await expect(itemsTable).toContainText(number);

    await itemsTable.locator("tbody tr", { hasText: number })
        .getByRole("button", { name: "Bearbeiten" }).click();
    // the dialog pre-fills the row's current values
    await expect(page.locator(".sapMDialog").getByLabel("Nummer")).toHaveValue(number);
    await page.locator(".sapMDialog").getByLabel("Bezeichnung").fill(changed);
    await page.getByRole("button", { name: "Speichern" }).click();

    // the changed description replaces the original one in the row
    await expect(itemsTable.locator("tbody tr", { hasText: number })).toContainText(changed);
    await expect(itemsTable.locator("tbody tr", { hasText: number })).not.toContainText(original);
    await expect(page.locator(".sapMDialog")).toBeHidden();
});

test("recipients: create and search a recipient (Story 3)", async ({ page }) => {
    await page.goto("/index.html#/recipients");

    const createRecipient = async (name: string) => {
        await page.getByRole("button", { name: "Empfänger anlegen" }).click();
        // scope to the dialog: the page search field's placeholder also contains "Name"
        await page.locator(".sapMDialog").getByLabel("Name").fill(name);
        await page.getByLabel("Adresse").fill("Musterstraße 1\n12345 Musterstadt");
        await page.getByRole("button", { name: "Speichern" }).click();
        await expect(page.locator("table")).toContainText(name);
        // wait until the dialog has fully closed before reopening it
        await expect(page.locator(".sapMDialog")).toBeHidden();
    };

    await createRecipient("Familie Mahr");
    await createRecipient("Zweiter Empfänger");

    // search must actually narrow the list: "Mahr" matches only the first recipient
    const search = page.getByRole("searchbox");
    await search.fill("Mahr");
    await search.press("Enter");
    await expect(page.locator("table")).toContainText("Familie Mahr");
    await expect(page.locator("table")).not.toContainText("Zweiter Empfänger");
});

test("recipients: edit an existing recipient via the row dialog (Story 3)", async ({ page }) => {
    const ts = Date.now();
    const name = `Vorher ${ts}`;
    const changed = `Geändert ${ts}`;
    await page.request.post("http://localhost:8080/odata/Recipients", {
        data: { Name: name, Address: "Alte Gasse 1" },
    });

    await page.goto("/index.html#/recipients");
    const recipientsTable = page.locator("[id$='recipientsTable-listUl']");
    await expect(recipientsTable).toContainText(name);

    await recipientsTable.locator("tbody tr", { hasText: name })
        .getByRole("button", { name: "Bearbeiten" }).click();
    // the dialog pre-fills the row's current values
    await expect(page.locator(".sapMDialog").getByLabel("Name")).toHaveValue(name);
    await page.locator(".sapMDialog").getByLabel("Name").fill(changed);
    await page.getByRole("button", { name: "Speichern" }).click();

    // the old name is gone, the new one is shown
    await expect(recipientsTable.locator("tbody tr", { hasText: changed })).toBeVisible();
    await expect(recipientsTable).not.toContainText(name);
    await expect(page.locator(".sapMDialog")).toBeHidden();
});

test("graves: search by number narrows the list and empty search shows nothing (Story 5.2)", async ({ page }) => {
    const ts = Date.now();
    const graveNumber = `SUCHE-${ts}`;
    await page.request.post("http://localhost:8080/odata/Graves", {
        data: { GraveNumber: graveNumber, StartDate: "2026-01-01", EndDate: "2040-12-31" },
    });

    await page.goto("/index.html#/graves");
    const gravesTable = page.locator("[id$='gravesTable-listUl']");
    // the newest grave is visible without search (Id desc order)
    await expect(gravesTable).toContainText(graveNumber);

    const search = page.getByRole("searchbox");
    await search.fill(graveNumber);
    await search.press("Enter");
    // the unique number matches exactly this run's grave, nothing else
    await expect(gravesTable.locator("tbody tr")).toHaveCount(1);
    await expect(gravesTable.locator("tbody tr")).toContainText(graveNumber);

    await search.fill(`gibtsnicht${ts}`);
    await search.press("Enter");
    // no match: the table renders its German empty state and this run's grave is gone
    await expect(gravesTable).toContainText("Keine Daten");
    await expect(gravesTable).not.toContainText(graveNumber);
});
