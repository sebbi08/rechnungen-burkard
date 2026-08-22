import { test, expect } from "@playwright/test";

test("app loads and navigation works", async ({ page }) => {
    await page.goto("/index.html");
    await expect(page.locator("body")).toContainText("Einstellungen");

    await page.locator("ul.sapTntNL > li", { hasText: "Rechnungen" }).click();
    await expect(page).toHaveURL(/invoices/);
    // wait until the page transition finished (the previous page is hidden)
    await expect(page.locator(".sapMNavItem.sapMNavItemHidden")).toHaveCount(1);

    await page.locator("ul.sapTntNL > li:visible", { hasText: "Gräber" }).click();
    await expect(page).toHaveURL(/graves/);
});
