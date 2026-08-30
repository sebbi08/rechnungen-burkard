import { expect, test } from "@playwright/test";

// The release image serves the UI5 build from the API's wwwroot, and the dev
// stack serves the same files through `ui5 serve`. The two do not agree on what
// they will hand out: `ui5 serve` returns any file it finds, while ASP.NET
// Core's static file middleware only answers for extensions it has a content
// type for and hides dot-prefixed names outright. Every resource type the
// runtime ships therefore works in dev and can still 404 in the deployment.
//
// A missing resource fails silently. When a message bundle 404s, UI5 keeps no
// texts of its own and renders the raw message key instead, so a dialog button
// reads "MSGBOX_TITLE_ERROR". The app's own labels are unaffected — they travel
// inside Component-preload.js — which is why nothing else in this suite notices.

// One per file type the build puts under dist/resources that is not a plain
// .js, .json or .css. The message bundles carry the framework's texts; the
// remaining four are library and theme metadata, and all of them are named with
// a leading dot.
const resourceTypes = [
  "/resources/sap/ui/core/messagebundle_de.properties",
  "/resources/sap/m/messagebundle_de.properties",
  "/resources/sap/ui/thirdparty/hyphenopoly/patterns/de.hpb",
  "/resources/sap/ui/core/.library",
  "/resources/sap/ui/core/.supportrc",
  "/resources/sap/ui/core/themes/sap_horizon/.theme",
  "/resources/sap/ui/core/themes/sap_horizon/.theming",
];

test("the deployment serves every resource type the UI5 build ships", async ({
  request,
}) => {
  for (const path of resourceTypes) {
    const response = await request.get(path);
    expect(response.status(), path).toBe(200);
  }
});

test("starting the app leaves no request unanswered", async ({ page }) => {
  const failed = new Set<string>();
  page.on("response", (response) => {
    if (response.status() >= 400) {
      failed.add(`${response.status()} ${new URL(response.url()).pathname}`);
    }
  });

  await page.goto("/index.html");
  // the launchpad tiles are the last thing the boot renders; waiting for the
  // network to settle afterwards also covers what UI5 fetches lazily, such as
  // the version info it reads once the core is up
  await expect(page.locator(".sapMGT").first()).toBeVisible();
  await page.waitForLoadState("networkidle");

  expect([...failed].sort()).toEqual([]);
});
