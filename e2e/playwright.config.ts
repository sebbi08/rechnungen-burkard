import { defineConfig, devices } from "@playwright/test";
import { BASE_URL, OWN_STACK, VUE_BASE_URL } from "./stack";

// Without E2E_BASE_URL the suite starts its own stack (docker-compose.e2e.yml)
// and runs against that, so a local run never writes into the dev database. CI
// sets E2E_BASE_URL and E2E_VUE_BASE_URL to target the release images it has
// already started. The dev stack is not a target: the specs write graves,
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
    // The app ships German and English; these tests assert the German UI, so
    // the browser locale is pinned instead of following the CI machine's.
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
  // (emulated, so UI5 reports system.phone) and a tablet-sized window.
  //
  // Each frontend has its own three projects. The Vue ones (tests/vue, D-003)
  // run against a deployment of their own — a second container of the same
  // image — and seed through its API: the backend runs one invoice generation
  // at a time and a generation sweeps every grave of its half-year, so two
  // suites generating in one database would refuse and invoice each other's
  // graves. Apart they behave as each would on its own, and the UI5 projects
  // can be deleted with the UI5 frontend without touching the Vue ones.
  projects: [
    {
      name: "desktop",
      testIgnore: [/layout\.spec\.ts$/, /tests[\\/]vue[\\/]/],
    },
    {
      name: "phone",
      testMatch: /tests[\\/]layout\.spec\.ts$/,
      use: { ...devices["Pixel 5"] },
    },
    {
      name: "tablet",
      testMatch: /tests[\\/]layout\.spec\.ts$/,
      use: { viewport: { width: 820, height: 1180 } },
    },
    {
      name: "vue-desktop",
      testDir: "./tests/vue",
      testIgnore: /layout\.spec\.ts$/,
      use: { baseURL: VUE_BASE_URL },
    },
    {
      name: "vue-phone",
      testDir: "./tests/vue",
      testMatch: /layout\.spec\.ts$/,
      use: { ...devices["Pixel 5"], baseURL: VUE_BASE_URL },
    },
    {
      name: "vue-tablet",
      testDir: "./tests/vue",
      testMatch: /layout\.spec\.ts$/,
      use: { viewport: { width: 820, height: 1180 }, baseURL: VUE_BASE_URL },
    },
  ],
});
