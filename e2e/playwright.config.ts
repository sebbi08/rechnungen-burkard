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
  // Every test runs on its own, in whichever worker is free, not only every
  // file: graves.spec.ts alone used to take as long as all the other files
  // together. That holds because each test seeds its own data and depends on no
  // other test; the one group that must not overlap says so itself.
  fullyParallel: true,
  // GitHub's runners for a private repository have two cores, where
  // Playwright's default of half the cores leaves a single worker. Two keep
  // both busy while a test waits on the app.
  workers: process.env.CI ? 2 : undefined,
  // Every test writes into the one SQLite database at once, and a grave
  // generation renders each grave's documents inside its write. A dialog that
  // closes once its row is saved has been seen to wait more than the default
  // five seconds behind one, so an assertion is given longer to come true.
  // Nothing that should fail passes because of it; it only fails later.
  expect: { timeout: 15_000 },
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
    // CI runs in UTC, a dev machine in Berlin; a month picked as local midnight
    // only survives a toISOString shift when the browser is not in UTC.
    timezoneId: "Europe/Berlin",
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
