import { test, expect } from "@playwright/test";

test("settings: stamp word validation and VAT rate (Story 1)", async ({ page }) => {
    await page.goto("/index.html");

    await page.getByLabel("Stempelwort").fill("");
    await page.getByRole("button", { name: "Speichern" }).click();
    await expect(page.locator(".sapMMessageToast, .sapMDialog")).toContainText("Stempelwort");

    await page.getByLabel("Stempelwort").fill("Kopie");
    await page.getByLabel("VAT-Satz").fill("7");
    await page.getByRole("button", { name: "VAT-Satz hinzufügen" }).click();
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

test("recipients: create and search a recipient (Story 3)", async ({ page }) => {
    await page.goto("/index.html#/recipients");
    await page.getByRole("button", { name: "Empfänger anlegen" }).click();
    // scope to the dialog: the page search field's placeholder also contains "Name"
    await page.locator(".sapMDialog").getByLabel("Name").fill("Familie Mahr");
    await page.getByLabel("Adresse").fill("Musterstraße 1\n12345 Musterstadt");
    await page.getByRole("button", { name: "Speichern" }).click();

    await page.getByRole("searchbox").fill("Mahr");
    await expect(page.locator("table")).toContainText("Familie Mahr");
});
