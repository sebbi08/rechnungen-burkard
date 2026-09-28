import { expect, test } from "@playwright/test";
import { VUE_BASE_URL } from "../stack";

// The release image serves the Vue evaluation frontend (D-003) on a second
// port, from the same process as the UI5 one. The frontend itself has no tests
// by decision; these cover only what the image adds for it: which files a port
// hands out, the fallback that lets a history-mode route be reloaded, and the
// API answering on that port too.

test("the Vue port starts the Vue app and leaves no request unanswered", async ({ page }) => {
  const failed = new Set<string>();
  page.on("response", (response) => {
    if (response.status() >= 400) {
      failed.add(`${response.status()} ${new URL(response.url()).pathname}`);
    }
  });

  await page.goto(`${VUE_BASE_URL}/`);
  await expect(page).toHaveURL(/\/invoices$/);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.waitForLoadState("networkidle");

  expect([...failed].sort()).toEqual([]);
});

test("a Vue route loaded directly opens that page", async ({ page }) => {
  // what a reload or a bookmark does: the server gets the route's path, which
  // is no file, and has to answer with the app for the router to pick it up
  await page.goto(`${VUE_BASE_URL}/settings`);
  await expect(page).toHaveURL(/\/settings$/);
  await expect(page.getByRole("heading", { level: 1, name: "Einstellungen" })).toBeVisible();
});

test("the API answers on the Vue port, and a wrong API path stays a 404", async ({ request }) => {
  expect((await request.get(`${VUE_BASE_URL}/odata/Settings`)).status()).toBe(200);
  expect((await request.get(`${VUE_BASE_URL}/odata/NoSuchSet`)).status()).toBe(404);
});

test("the Vue port ships the licenses of what it bundles", async ({ request }) => {
  const response = await request.get(`${VUE_BASE_URL}/THIRD-PARTY-LICENSES.md`);
  expect(response.status()).toBe(200);
  expect(await response.text()).toContain("primevue");
});
