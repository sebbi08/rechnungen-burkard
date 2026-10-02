import { expect, test } from "@playwright/test";

// The release image serves the frontend's build from the API's own process.
// These cover what that takes: the app starting without a request left
// unanswered, the fallback that lets a history-mode route be reloaded, a wrong
// API path staying an error, and the licenses of what the build bundles.

test("the app starts and leaves no request unanswered", async ({ page }) => {
  const failed = new Set<string>();
  page.on("response", (response) => {
    if (response.status() >= 400) {
      failed.add(`${response.status()} ${new URL(response.url()).pathname}`);
    }
  });

  await page.goto("/");
  await expect(page).toHaveURL(/\/invoices$/);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.waitForLoadState("networkidle");

  expect([...failed].sort()).toEqual([]);
});

test("a route loaded directly opens that page", async ({ page }) => {
  // what a reload or a bookmark does: the server gets the route's path, which
  // is no file, and has to answer with the app for the router to pick it up
  await page.goto("/settings");
  await expect(page).toHaveURL(/\/settings$/);
  await expect(page.getByRole("heading", { level: 1, name: "Einstellungen" })).toBeVisible();
});

test("the API answers next to the app, and a wrong API path stays a 404", async ({ request }) => {
  expect((await request.get("/odata/Settings")).status()).toBe(200);
  expect((await request.get("/odata/NoSuchSet")).status()).toBe(404);
});

test("the image ships the licenses of what the build bundles", async ({ request }) => {
  const response = await request.get("/THIRD-PARTY-LICENSES.md");
  expect(response.status()).toBe(200);
  expect(await response.text()).toContain("primevue");
});
