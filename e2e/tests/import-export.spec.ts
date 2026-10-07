import AdmZip from "adm-zip";
import { expect, type Page, test } from "@playwright/test";
import { ensureRow } from "../seed";
import { unique } from "../unique";
import { dialog } from "../vue";

// Unique per worker, and each test puts its own prefix in front: the import is
// create-only, so a number another test or an earlier run already imported
// would turn a valid archive into a rejected one.
const RUN = unique();

const RECIPIENTS_HEADER = "RecipientNumber,Name,Address\r\n";
const ITEMS_HEADER = "ItemNumber,Description,VatRate,Price,Quarter\r\n";
const GRAVES_HEADER = "GraveNumber,FieldNumber,SiteName,Position,StartDate,EndDate\r\n";
const MAPPING_HEADER = "Type,GraveNumber,Reference,Count,PriceIncludesVat\r\n";

function archive(files: Record<string, string>): Buffer {
  const zip = new AdmZip();
  for (const [name, content] of Object.entries(files)) {
    zip.addFile(name, Buffer.from(content, "utf8"));
  }
  return zip.toBuffer();
}

function validArchive(suffix: string): Buffer {
  return archive({
    "recipients.csv":
      RECIPIENTS_HEADER +
      `IMP-R1-${suffix},Anna Import,"Hauptstraße 1\n99999 Musterdorf"\r\n` +
      `IMP-R2-${suffix},Bert Import,"Nebenweg 2\n99999 Musterdorf"\r\n`,
    "items.csv": ITEMS_HEADER + `IMP-I1-${suffix},Grabpflege importiert,7,120.00,\r\n`,
    "graves.csv":
      GRAVES_HEADER + `IMP-G1-${suffix},27,Grabstätte Import,DoubleOutside,2026-01,2040-12\r\n`,
    "mapping.csv":
      MAPPING_HEADER +
      `Item,IMP-G1-${suffix},IMP-I1-${suffix},2,\r\n` +
      `Recipient,IMP-G1-${suffix},IMP-R1-${suffix},,\r\n` +
      `Recipient,IMP-G1-${suffix},IMP-R2-${suffix},,\r\n`,
  });
}

/** Chooses the archive and starts the import. */
async function importArchive(page: Page, buffer: Buffer): Promise<void> {
  // the field itself is hidden behind a button of the same name
  await page
    .locator("input[type='file']")
    .setInputFiles({ name: "import.zip", mimeType: "application/zip", buffer });
  // the chosen file is named beside the button
  await expect(page.getByText("import.zip", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Importieren", exact: true }).click();
}

/** The table of problems a rejected archive is reported with. */
function problems(page: Page) {
  return page
    .locator(".p-card")
    .filter({ has: page.locator(".p-card-title", { hasText: "Gefundene Probleme" }) });
}

test.beforeEach(async ({ page }) => {
  // the imported item's VAT rate has to be configured, exactly as it must be
  // when an item is created through the item section
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
});

test("download the import template and read its four CSV files (Story 10)", async ({ page }) => {
  await page.goto("/import-export");

  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "Vorlage herunterladen" }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toContain(".zip");

  const zip = new AdmZip(await download.path());
  const names = zip
    .getEntries()
    .map((entry) => entry.entryName)
    .sort();
  expect(names).toEqual(["graves.csv", "items.csv", "mapping.csv", "recipients.csv"]);
  // each file carries nothing but its header row
  expect(zip.readAsText("recipients.csv").trim()).toContain("RecipientNumber");
  expect(zip.readAsText("mapping.csv").trim()).toContain("Type,GraveNumber");
});

test("importing without a file asks for one first", async ({ page }) => {
  await page.goto("/import-export");
  await expect(page.getByText("Keine Datei ausgewählt")).toBeVisible();
  await page.getByRole("button", { name: "Importieren", exact: true }).click();
  await expect(
    page.locator(".p-toast-message", { hasText: "Bitte wählen Sie zuerst eine ZIP-Datei aus." }),
  ).toBeVisible();
});

test("a defective archive is reported per file and row and imports nothing (Story 11)", async ({
  page,
}) => {
  const graveNumber = `BAD-G-${RUN}`;
  const broken = archive({
    "recipients.csv": RECIPIENTS_HEADER + `BAD-R-${RUN},,Hauptstraße 1\r\n`,
    "items.csv": ITEMS_HEADER + `BAD-I-${RUN},Grabpflege,7,120,\r\n`,
    "graves.csv":
      GRAVES_HEADER + `${graveNumber},27,Grabstätte Import,DoubleOutside,2026-01,2040-12\r\n`,
    "mapping.csv": MAPPING_HEADER + `Item,${graveNumber},GIBTSNICHT,1,\r\n`,
  });

  await page.goto("/import-export");
  await importArchive(page, broken);

  const refusal = dialog(page, "Archiv");
  await expect(refusal).toContainText("abgelehnt");
  await refusal.getByRole("button", { name: "OK" }).click();
  await expect(refusal).toHaveCount(0);

  // the report names the file and the row of every problem
  await expect(problems(page)).toContainText("recipients.csv");
  await expect(problems(page)).toContainText("mapping.csv");
  await expect(problems(page).locator("tbody tr").first()).toContainText("2");

  // and nothing reached the database
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq '${graveNumber}'`)
  ).json();
  expect(graves.value).toHaveLength(0);
});

test("a valid archive imports recipients, items, graves and their links (Story 12)", async ({
  page,
}) => {
  const suffix = `${RUN}`;
  await page.goto("/import-export");
  await importArchive(page, validArchive(suffix));

  // blocking progress while the archive is processed, then the summary
  const result = dialog(page, "Import abgeschlossen");
  await expect(result).toContainText("Empfänger: 2", { timeout: 30000 });
  await expect(result).toContainText("Gräber: 1");
  await result.getByRole("button", { name: "OK" }).click();
  // the form is ready for the next archive
  await expect(page.getByText("Keine Datei ausgewählt")).toBeVisible();

  // every reference resolves to an entry from the archive
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq 'IMP-G1-${suffix}'&$select=Id`)
  ).json();
  expect(graves.value).toHaveLength(1);
  const graveId = graves.value[0].Id;

  const itemLinks = await (
    await page.request.get(`/odata/GraveItems?$filter=GraveId eq ${graveId}`)
  ).json();
  expect(itemLinks.value).toHaveLength(1);
  expect(Number(itemLinks.value[0].Count)).toBe(2);

  const recipientLinks = await (
    await page.request.get(`/odata/GraveRecipients?$filter=GraveId eq ${graveId}`)
  ).json();
  expect(recipientLinks.value).toHaveLength(2);

  const recipients = await (
    await page.request.get(`/odata/Recipients?$filter=Number eq 'IMP-R1-${suffix}'`)
  ).json();
  expect(recipients.value[0].Name).toBe("Anna Import");
  // the quoted CSV field kept its line break
  expect(recipients.value[0].Address).toContain("\n");
});

test("importing the same archive twice is rejected as already existing (Story 11)", async ({
  page,
}) => {
  const suffix = `TWICE-${RUN}`;
  const buffer = validArchive(suffix);

  await page.goto("/import-export");
  await importArchive(page, buffer);
  const first = dialog(page, "Import abgeschlossen");
  await expect(first).toContainText("Empfänger: 2", { timeout: 30000 });
  await first.getByRole("button", { name: "OK" }).click();
  await expect(first).toHaveCount(0);

  await importArchive(page, buffer);
  const second = dialog(page, "Archiv");
  await expect(second).toContainText("abgelehnt");
  await second.getByRole("button", { name: "OK" }).click();

  await expect(problems(page)).toContainText("recipients.csv");
  await expect(problems(page)).toContainText("graves.csv");

  // still exactly one grave with that number
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq 'IMP-G1-${suffix}'`)
  ).json();
  expect(graves.value).toHaveLength(1);
});
