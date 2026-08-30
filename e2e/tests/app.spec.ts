import { expect, test } from "@playwright/test";

const tileTitles = ["Einstellungen", "Leistungen", "Empfänger", "Rechnungen", "Gräber"];

test("home shows the launchpad tiles and tiles navigate to the sub-views", async ({ page }) => {
  await page.goto("/index.html");

  // the home route is the default: all five launchpad tiles are visible
  for (const title of tileTitles) {
    await expect(page.locator(".sapMGT", { hasText: title })).toBeVisible();
  }

  // clicking a tile navigates to its route
  await page.locator(".sapMGT", { hasText: "Rechnungen" }).click();
  await expect(page).toHaveURL(/#\/invoices\/?$/);

  // the page back button returns to the launchpad
  await page.locator("button[id$='-navButton']").click();
  await expect(page).toHaveURL(/#\/?$/);
  await expect(page.locator(".sapMGT", { hasText: "Rechnungen" })).toBeVisible();

  // a second tile leads to its route
  await page.locator(".sapMGT", { hasText: "Gräber" }).click();
  await expect(page).toHaveURL(/#\/graves\/?$/);
});
