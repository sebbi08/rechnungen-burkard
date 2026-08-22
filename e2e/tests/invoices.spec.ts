import { test, expect } from "@playwright/test";

async function seedMasterData(page: import("@playwright/test").Page): Promise<void> {
    // direct API calls keep the test focused on the UI flow
    await page.request.post("http://localhost:8080/odata/VatRates", { data: { Rate: 7 } });
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
        data: { Number: "9001", Description: "Grabpflege", VatRate: 7, DefaultPrice: null },
    });
    await page.request.post("http://localhost:8080/odata/Recipients", {
        data: { Name: "Familie Mahr", Address: "Musterstraße 1\n12345 Musterstadt" },
    });
}

test("create an invoice with a mandatory price and download documents (Story 4, 8)", async ({ page }) => {
    await seedMasterData(page);
    await page.goto("/index.html#/invoices");

    // the dialog's line table stays in the DOM after closing: scope to the
    // invoices table explicitly
    const invoicesTable = page.locator("[id$='invoicesTable-listUl']");

    await page.getByRole("button", { name: "Rechnung erstellen" }).click();
    // fragment controls are prefixed with the view id; the visual select root
    // is the first .sapMSlt inside the dialog (the hidden native select is not)
    await page.locator(".sapMDialog .sapMSlt").first().click(); // recipient Select opens popover
    // previous runs leave several "Familie Mahr" rows behind: take the first one
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "Familie Mahr" }).first().click();
    await page.locator(".sapMDialog .sapMSlt[id*='lineItem']").click();
    // the item select shows item numbers; 9001 is the unique number this test seeds
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "9001" }).click();
    await page.getByLabel("Anzahl").fill("2");
    await page.getByLabel("Preis").fill("120");
    // by now the initial list has fully rendered: snapshot the row count. The dev
    // DB persists between runs (already holds 2026-0001..2026-0005), so asserting
    // a fixed number would pass on pre-existing rows.
    const rowsBefore = await invoicesTable.locator("tbody tr").count();
    await page.getByRole("button", { name: "Speichern" }).click();

    // assert row-count growth: saving adds exactly one invoice row
    await expect(invoicesTable.locator("tbody tr")).toHaveCount(rowsBefore + 1);

    const downloadPromise = page.waitForEvent("download");
    // new rows are appended at the end: download the normal document from the
    // last row (getByRole instead of getByText, which would also match the
    // "Rechnungsnummer" header)
    await invoicesTable.locator("tbody tr").last()
        .getByRole("button", { name: "Rechnung", exact: true }).first().click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toContain(".pdf");
    expect(download.suggestedFilename()).not.toContain("Kopie");
});

test("invoice without price cannot be saved (Story 4.3/edge case)", async ({ page }) => {
    await seedMasterData(page);
    await page.goto("/index.html#/invoices");

    await page.getByRole("button", { name: "Rechnung erstellen" }).click();
    await page.locator(".sapMDialog .sapMSlt").first().click();
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "Familie Mahr" }).first().click();
    await page.locator(".sapMDialog .sapMSlt[id*='lineItem']").click();
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: "9001" }).click();
    await page.getByRole("button", { name: "Speichern" }).click();

    // the create dialog renders "Preis" as a column header and stays in the DOM
    // after closing, so an assertion on any .sapMDialog would pass without the
    // blocking error dialog. Scope to the MessageBox (exclusive class
    // sapMMessageBox) and assert the exact German error text
    // (i18n errorPriceRequired), which only the error dialog shows.
    await expect(page.locator(".sapMDialog.sapMMessageBox"))
        .toContainText("Zu dieser Leistung fehlt ein Preis.");
});
