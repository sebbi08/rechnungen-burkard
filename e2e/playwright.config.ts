import { defineConfig, devices } from "@playwright/test";
import { BASE_URL, OWN_STACK } from "./stack";

// Without E2E_BASE_URL the suite starts its own stack (docker-compose.e2e.yml)
// and runs against that, so a local run never writes into the dev database. CI
// sets E2E_BASE_URL to target the release image it has already started. The dev stack is not a target: the specs write graves,
// recipients and invoices that nothing deletes again, into the database that
// holds the user's own data.
export default defineConfig({
  testDir: "./tests",
  timeout: 60000,
  globalSetup: OWN_STACK ? "./global-setup.ts" : undefined,
  globalTeardown: OWN_STACK ? "./global-teardown.ts" : undefined,
  // The line reporter is what a terminal wants; the HTML one is what a failed
  // CI run needs, and it writes the `playwright-report` folder the workflow
  // uploads — without it that upload found nothing and a red run left only a
  // stack trace to go on. `open: never` keeps a local run from launching a
  // browser at the end.
  reporter: [["line"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    // page.goto and page.request both resolve relative paths against this, so
    // the browser and the seeding calls always hit the same deployment
    baseURL: BASE_URL,
    // The UI is German whatever the browser prefers; the locale is pinned so
    // that what the browser formats on its own does not follow the CI machine.
    locale: "de-DE",
    // Kept only for what failed. A trace carries the DOM, the network and the
    // console at every step, which is what turns "element not found" into a
    // cause — the last CI failure took a reproduction to explain because none
    // of that survived the run.
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  // The feature specs describe flows and run once, at desktop size. Only the
  // layout spec runs on the small screens, and it runs on both of them: a phone
  // (emulated, touch and all) and a tablet-sized window.
  projects: [
    {
      name: "desktop",
      testIgnore: /layout\.spec\.ts$/,
    },
    {
      name: "phone",
      testMatch: /layout\.spec\.ts$/,
      use: { ...devices["Pixel 5"] },
    },
    {
      name: "tablet",
      testMatch: /layout\.spec\.ts$/,
      use: { viewport: { width: 820, height: 1180 } },
    },
  ],
});
