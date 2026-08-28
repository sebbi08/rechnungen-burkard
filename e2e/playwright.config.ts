import { defineConfig, devices } from "@playwright/test";
import { BASE_URL, OWN_STACK } from "./stack";

// Without E2E_BASE_URL the suite starts its own stack (docker-compose.e2e.yml)
// and runs against that, so a local run never writes into the dev database. Set
// E2E_BASE_URL to target something already running instead — the dev stack via
// `npm run test:dev`, or the release image in CI.
export default defineConfig({
  testDir: "./tests",
  timeout: 60000,
  globalSetup: OWN_STACK ? "./global-setup.ts" : undefined,
  globalTeardown: OWN_STACK ? "./global-teardown.ts" : undefined,
  use: {
    // page.goto and page.request both resolve relative paths against this, so
    // the browser and the seeding calls always hit the same deployment
    baseURL: BASE_URL,
    // The app ships German and English; these tests assert the German UI, so
    // the browser locale is pinned instead of following the CI machine's.
    locale: "de-DE",
  },
  // The feature specs describe flows and run once, at desktop size. Only the
  // layout spec runs on the small screens, and it runs on both of them: a phone
  // (emulated, so UI5 reports system.phone) and a tablet-sized window.
  projects: [
    {
      name: "desktop",
      testIgnore: /layout\.spec\.ts/,
    },
    {
      name: "phone",
      testMatch: /layout\.spec\.ts/,
      use: { ...devices["Pixel 5"] },
    },
    {
      name: "tablet",
      testMatch: /layout\.spec\.ts/,
      use: { viewport: { width: 820, height: 1180 } },
    },
  ],
});
