import AdmZip from "adm-zip";
import { expect, test } from "@playwright/test";

// Unique per run: the import is create-only, so against the dev stack
// (`npm run test:dev`) fixed numbers would collide with rows from earlier runs.
const RUN = Date.now();

const RECIPIENTS_HEADER = "RecipientNumber,Name,Address\r\n";
const ITEMS_HEADER = "ItemNumber,Description,VatRate,Price,PriceIncludesVat\r\n";
const GRAVES_HEADER = "GraveNumber,StartDate,EndDate\r\n";
const MAPPING_HEADER = "Type,GraveNumber,Reference,Count\r\n";

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
    "graves.csv": GRAVES_HEADER + `IMP-G1-${suffix},2026-01-01,2040-12-31\r\n`,
    "mapping.csv":
      MAPPING_HEADER +
      `Item,IMP-G1-${suffix},IMP-I1-${suffix},2\r\n` +
      `Recipient,IMP-G1-${suffix},IMP-R1-${suffix},\r\n` +
      `Recipient,IMP-G1-${suffix},IMP-R2-${suffix},\r\n`,
  });
}

test.beforeEach(async ({ page }) => {
  // the imported item's VAT rate has to be configured, exactly as it must be
  // when an item is created through the item section
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
});

test("download the import template and read its four CSV files (Story 10)", async ({ page }) => {
  await page.goto("/index.html#/import-export");

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

test("a defective archive is reported per file and row and imports nothing (Story 11)", async ({
  page,
}) => {
  const graveNumber = `BAD-G-${RUN}`;
  const broken = archive({
    "recipients.csv": RECIPIENTS_HEADER + `BAD-R-${RUN},,Hauptstraße 1\r\n`,
    "items.csv": ITEMS_HEADER + `BAD-I-${RUN},Grabpflege,7,120,\r\n`,
    "graves.csv": GRAVES_HEADER + `${graveNumber},2026-01-01,2040-12-31\r\n`,
    "mapping.csv": MAPPING_HEADER + `Item,${graveNumber},GIBTSNICHT,1\r\n`,
  });

  await page.goto("/index.html#/import-export");
  await page
    .locator("input[type='file']")
    .setInputFiles({ name: "import.zip", mimeType: "application/zip", buffer: broken });
  await page.getByRole("button", { name: "Importieren", exact: true }).click();

  const errorBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(errorBox).toContainText("abgelehnt");
  await errorBox.getByRole("button").first().click();

  // the report names the file and the row of every problem
  const problems = page.locator("[id$='problemsTable-listUl']");
  await expect(problems).toContainText("recipients.csv");
  await expect(problems).toContainText("mapping.csv");
  await expect(problems.locator("tbody tr").first()).toContainText("2");

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
  await page.goto("/index.html#/import-export");

  await page.locator("input[type='file']").setInputFiles({
    name: "import.zip",
    mimeType: "application/zip",
    buffer: validArchive(suffix),
  });
  await page.getByRole("button", { name: "Importieren", exact: true }).click();

  // blocking progress while the archive is processed, then the summary
  const resultBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(resultBox).toContainText("Import abgeschlossen", {
    timeout: 30000,
  });
  await expect(resultBox).toContainText("Empfänger: 2");
  await expect(resultBox).toContainText("Gräber: 1");
  await resultBox.getByRole("button").first().click();

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

  // the first import goes through the API so the test focuses on the second
  const upload = {
    name: "import.zip",
    mimeType: "application/zip",
    buffer,
  };
  await page.goto("/index.html#/import-export");
  await page.locator("input[type='file']").setInputFiles(upload);
  await page.getByRole("button", { name: "Importieren", exact: true }).click();
  const first = page.locator(".sapMDialog.sapMMessageBox");
  await expect(first).toContainText("Import abgeschlossen", { timeout: 30000 });
  await first.getByRole("button").first().click();

  await page.locator("input[type='file']").setInputFiles(upload);
  await page.getByRole("button", { name: "Importieren", exact: true }).click();
  const second = page.locator(".sapMDialog.sapMMessageBox");
  await expect(second).toContainText("abgelehnt");
  await second.getByRole("button").first().click();

  const problems = page.locator("[id$='problemsTable-listUl']");
  await expect(problems).toContainText("recipients.csv");
  await expect(problems).toContainText("graves.csv");

  // still exactly one grave with that number
  const graves = await (
    await page.request.get(`/odata/Graves?$filter=GraveNumber eq 'IMP-G1-${suffix}'`)
  ).json();
  expect(graves.value).toHaveLength(1);
});
