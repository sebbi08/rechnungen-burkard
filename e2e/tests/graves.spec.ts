import { test, expect } from "@playwright/test";

// Unique per test run: the dev DB persists between runs, so fixed numbers would
// either collide with pre-existing rows or make assertions vacuous.
const RUN = Date.now();

async function seedGraveGenerationData(page: import("@playwright/test").Page, count: number): Promise<string> {
    // VatRate 7 may already exist from previous runs; a 409 here is harmless.
    await page.request.post("http://localhost:8080/odata/VatRates", { data: { Rate: 7 } });
    const item = await (await page.request.post("http://localhost:8080/odata/InvoiceItems", {
        data: { Number: `8001-${RUN}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 },
    })).json();
    const recipient = await (await page.request.post("http://localhost:8080/odata/Recipients", {
        data: { Name: `Anna Beispiel ${RUN}`, Address: "Hauptstraße 1" },
    })).json();

    // self-heal leftovers from interrupted runs: graves without item/recipient
    // links would be skipped by the generator and keep the duplicate check
    // (409 "bereits") from firing. Only the gaps are filled, so existing
    // grave totals are not inflated.
    const itemLinks = await (await page.request.get(
        "http://localhost:8080/odata/GraveItems?$select=GraveId")).json();
    const recipientLinks = await (await page.request.get(
        "http://localhost:8080/odata/GraveRecipients?$select=GraveId")).json();
    const withItems = new Set<number>((itemLinks.value ?? []).map((link: any) => link.GraveId));
    const withRecipients = new Set<number>((recipientLinks.value ?? []).map((link: any) => link.GraveId));
    const existing = await (await page.request.get(
        "http://localhost:8080/odata/Graves?$select=Id")).json();
    for (const grave of existing.value ?? []) {
        if (!withItems.has(grave.Id)) {
            await page.request.post("http://localhost:8080/odata/GraveItems", {
                data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
            });
        }
        if (!withRecipients.has(grave.Id)) {
            await page.request.post("http://localhost:8080/odata/GraveRecipients", {
                data: { GraveId: grave.Id, RecipientId: recipient.Id },
            });
        }
    }

    const firstGraveNumber = `G-${RUN}-001`;
    for (let i = 1; i <= count; i++) {
        const grave = await (await page.request.post("http://localhost:8080/odata/Graves", {
            data: {
                GraveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
                StartDate: "2026-01-01",
                EndDate: "2040-12-31",
            },
        })).json();
        await page.request.post("http://localhost:8080/odata/GraveItems", {
            data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
        });
        await page.request.post("http://localhost:8080/odata/GraveRecipients", {
            data: { GraveId: grave.Id, RecipientId: recipient.Id },
        });
    }
    return firstGraveNumber;
}

test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({ page }) => {
    const firstGraveNumber = await seedGraveGenerationData(page, 60);
    await page.goto("/index.html#/graves");
    const gravesTable = page.locator("[id$='gravesTable-listUl']");
    await expect(gravesTable).toContainText(firstGraveNumber);

    await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
    await page.getByRole("button", { name: "Jetzt erzeugen" }).click();

    // blocking progress indicator is visible while the job runs
    await expect(page.locator(".sapMBusyDialog")).toBeVisible({ timeout: 5000 });
    await expect(page.locator(".sapMBusyDialog")).not.toBeVisible({ timeout: 90000 });

    // status column now shows graves as invoiced
    await expect(gravesTable.getByText("erstellt").first()).toBeVisible();

    // dismiss the success message, then a second generation is prevented
    await page.locator(".sapMDialog").getByRole("button", { name: "OK" }).click();
    await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
    await page.getByRole("button", { name: "Jetzt erzeugen" }).click();
    // the blocking error dialog shows the German "already invoiced" message
    await expect(page.locator(".sapMDialog.sapMMessageBox")).toContainText("bereits");
});

test("create a grave and link item and recipient (Story 5)", async ({ page }) => {
    const itemNumber = `8002-${RUN}`;
    const recipientName = `Bert Beispiel ${RUN}`;
    const graveNumber = `X-${RUN}`;

    await page.request.post("http://localhost:8080/odata/VatRates", { data: { Rate: 7 } });
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
        data: { Number: itemNumber, Description: "Pflege", VatRate: 7, DefaultPrice: 50 },
    });
    await page.request.post("http://localhost:8080/odata/Recipients", {
        data: { Name: recipientName, Address: "Nebenweg 2" },
    });

    await page.goto("/index.html#/graves");
    const gravesTable = page.locator("[id$='gravesTable-listUl']");

    await page.getByRole("button", { name: "Grab anlegen" }).click();
    await page.getByLabel("Grabnummer").fill(graveNumber);
    await page.getByLabel("Beginn").fill("2026-01-01");
    await page.getByLabel("Ende").fill("2040-12-31");
    await page.getByRole("button", { name: "Speichern" }).click();

    await expect(gravesTable).toContainText(graveNumber);

    // open the edit dialog for the newly created grave and add the item and
    // recipient links; ".first()" would edit an unrelated pre-existing row
    await gravesTable.locator("tbody tr", { hasText: graveNumber })
        .getByRole("button", { name: "Bearbeiten" }).click();
    await page.locator(".sapMDialog .sapMSlt[id$='linkItem']").click();
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: itemNumber }).click();
    await page.locator("[id$='linkCount-inner']").fill("1");
    await page.getByRole("button", { name: "Hinzufügen" }).first().click();
    // scope to the dialog's own items table: the select keeps a hidden inline
    // copy of its options in the DOM, which would make a dialog-wide assertion vacuous
    await expect(page.locator("[id$='graveItemsTable-listUl']")).toContainText(itemNumber);

    await page.locator(".sapMDialog .sapMSlt[id$='linkRecipient']").click();
    await page.locator("ul.sapMSltList-CTX li.sapMSelectListItem", { hasText: recipientName }).click();
    await page.getByRole("button", { name: "Hinzufügen" }).last().click();
    await expect(page.locator("[id$='graveRecipientsTable-listUl']")).toContainText(recipientName);

    await page.getByRole("button", { name: "Speichern" }).click();
});
