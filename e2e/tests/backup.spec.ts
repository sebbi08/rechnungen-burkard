import AdmZip from "adm-zip";
import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import { createRow } from "../seed";
import { withGenerationLock } from "../generation-lock";
import { unique } from "../unique";
import { confirmation, toasts } from "../vue";

// None of these confirms a restore: it would replace the data of every other
// test running in parallel. The restore itself is covered by the backend's
// integration tests. Download and upload are refused while a generation runs,
// so each test that does either takes the generation lock.

// the backup card's file field comes after the import card's
const backupField = (page: import("@playwright/test").Page) =>
  page.locator("input[type='file']").last();

test("download a backup holding the database and its manifest", async ({ page }) => {
  await page.goto("/import-export");

  await withGenerationLock(async () => {
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Sicherung herunterladen" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(
      /^rechnungen-sicherung-\d{4}-\d{2}-\d{2}-\d{4}\.zip$/,
    );

    // entry names only: the contents are the whole database
    const names = new AdmZip(await download.path()).getEntries().map((entry) => entry.entryName);
    expect(names.sort()).toEqual(["invoicing.db", "manifest.json"]);
  });
});

test("refuse a file that is not a backup", async ({ page }) => {
  await page.goto("/import-export");

  const zip = new AdmZip();
  zip.addFile("notiz.txt", Buffer.from("keine Sicherung", "utf8"));

  await withGenerationLock(async () => {
    await backupField(page).setInputFiles({
      name: "falsch.zip",
      mimeType: "application/zip",
      buffer: zip.toBuffer(),
    });
    await expect(
      toasts(page).filter({ hasText: "Die Datei ist keine Sicherung dieser Anwendung." }),
    ).toBeVisible();
  });
  await expect(confirmation(page)).toHaveCount(0);
});

test("cancelling the confirmation changes nothing", async ({ page }) => {
  const name = `Sicherung Beispiel ${unique()}`;
  await createRow(page, "/odata/Recipients", { Name: name, Address: "Sicherungsweg 1" });
  await page.goto("/import-export");
  // a reload would drop this
  await page.evaluate(() => {
    (window as any).__noReload = true;
  });

  await withGenerationLock(async () => {
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Sicherung herunterladen" }).click();
    const download = await downloadPromise;
    await backupField(page).setInputFiles({
      name: "sicherung.zip",
      mimeType: "application/zip",
      buffer: await readFile(await download.path()),
    });

    const ask = confirmation(page);
    await expect(ask).toBeVisible();
    // the counts depend on the other tests' data: the wording and numbers only
    await expect(ask).toContainText(
      /Die Sicherung vom .+ \(Version .+\) enthält \d+ Gräber, \d+ Empfänger und \d+ Rechnungen\. Alle Daten dieser Instanz werden durch sie ersetzt\./,
    );
    await ask.getByRole("button", { name: "Abbrechen" }).click();
    await expect(ask).toHaveCount(0);
  });

  expect(await page.evaluate(() => (window as any).__noReload)).toBe(true);
  await page.goto("/recipients");
  await expect(page.getByRole("row").filter({ hasText: name })).toBeVisible();
});
