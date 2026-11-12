import { expect, type Page, test } from "@playwright/test";
import { withGenerationLock } from "../generation-lock";
import { createRow, ensureRow, seedGrave } from "../seed";
import { unique } from "../unique";

// The page sorts the graves that have ended or were deleted into four panels.
// Every test seeds graves of its own and finds them by their number's marker,
// so the graves other tests leave behind do not matter.

/** The panel with this heading. */
function panel(page: Page, title: string) {
  return page
    .locator(".p-card")
    .filter({ has: page.getByRole("heading", { level: 2, name: title, exact: true }) });
}

/** Seeds an ordinary grave with an item and a recipient. */
async function seedOrdinary(
  page: Page,
  graveNumber: string,
  rest: Partial<Parameters<typeof seedGrave>[1]> = {},
): Promise<number> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `${graveNumber}-item`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 100,
  });
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Geschäft ${graveNumber}`,
    Address: "Weg 1",
  });
  return seedGrave(page, {
    graveNumber,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
    ...rest,
  });
}

async function seedCooperative(
  page: Page,
  graveNumber: string,
  rest: Partial<Parameters<typeof seedGrave>[1]> = {},
): Promise<number> {
  return seedGrave(page, {
    graveNumber,
    items: [],
    recipientIds: [],
    isCooperative: true,
    ...rest,
  });
}

async function remove(page: Page, graveId: number): Promise<void> {
  // the server refuses a delete while a run is going, so wait for the lock
  await withGenerationLock(async () => {
    const deletion = await page.request.post("/odata/DeleteGrave", { data: { graveId } });
    expect(deletion.ok()).toBe(true);
  });
}

/** Seeds one grave of each kind and answers the marker they share. */
async function seedAll(page: Page): Promise<string> {
  const marker = `IG-${unique()}`;
  const year = new Date().getFullYear();
  const ended = { startDate: `${year - 2}-01`, endDate: `${year - 1}-12` };
  const running = { startDate: "2044-01", endDate: null };
  await seedOrdinary(page, `${marker}-ended`, ended);
  await remove(page, await seedOrdinary(page, `${marker}-deleted`, running));
  await seedCooperative(page, `${marker}-coop-ended`, ended);
  await remove(page, await seedCooperative(page, `${marker}-coop-deleted`, running));
  // deleted and ended: only the panel of deleted graves lists it
  await remove(page, await seedCooperative(page, `${marker}-coop-both`, ended));
  await seedOrdinary(page, `${marker}-running`, running);
  return marker;
}

test("the page sorts graves into its four panels", async ({ page }) => {
  const marker = await seedAll(page);

  await page.goto("/inactive-graves");
  await expect(page.getByRole("heading", { level: 1, name: "Inaktive Gräber" })).toBeVisible();

  const expected: Record<string, string[]> = {
    "Beendete Gräber": [`${marker}-ended`],
    "Gelöschte Gräber": [`${marker}-deleted`],
    "Beendete Genossenschaftsgräber": [`${marker}-coop-ended`],
    "Gelöschte Genossenschaftsgräber": [`${marker}-coop-deleted`, `${marker}-coop-both`],
  };
  for (const [title, numbers] of Object.entries(expected)) {
    const one = panel(page, title);
    await one.getByRole("textbox", { name: `${title} durchsuchen` }).fill(marker);
    const rows = one.locator("tbody tr");
    await expect(rows, title).toHaveCount(numbers.length);
    for (const number of numbers) {
      await expect(rows.filter({ hasText: number }), `${title}: ${number}`).toHaveCount(1);
    }
  }
  // the running grave is in none of them
  await expect(page.getByText(`${marker}-running`)).toHaveCount(0);
});

test("a panel searches and opens a grave", async ({ page }) => {
  const marker = await seedAll(page);

  await page.goto("/inactive-graves");
  const deleted = panel(page, "Gelöschte Genossenschaftsgräber");
  const field = deleted.getByRole("textbox", {
    name: "Gelöschte Genossenschaftsgräber durchsuchen",
  });
  await field.fill(marker);
  await expect(deleted.locator("tbody tr")).toHaveCount(2);
  await field.fill(`${marker}-coop-both`);
  await expect(deleted.locator("tbody tr")).toHaveCount(1);
  // a search narrows its own panel only
  const ended = panel(page, "Beendete Gräber");
  await ended.getByRole("textbox", { name: "Beendete Gräber durchsuchen" }).fill(marker);
  await expect(ended.locator("tbody tr")).toHaveCount(1);

  await deleted.getByRole("link", { name: `${marker}-coop-both` }).click();
  await expect(page).toHaveURL(/\/inactive-graves\/\d+$/);
  await expect(
    page
      .getByRole("navigation", { name: "Navigation" })
      .getByRole("link", { name: "Inaktive Gräber", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expect(
    page.getByRole("heading", { level: 1, name: new RegExp(`${marker}-coop-both`) }),
  ).toBeVisible();

  const crumb = page
    .getByRole("navigation", { name: "Seitenpfad" })
    .getByRole("link", { name: "Inaktive Gräber" });
  await crumb.click();
  await expect(page).toHaveURL(/\/inactive-graves(\?.*)?$/);
});

test("a panel folds from its heading and is remembered folded", async ({ page }) => {
  const marker = `IG-${unique()}`;
  const year = new Date().getFullYear();
  await seedCooperative(page, `${marker}-coop-ended`, {
    startDate: `${year - 2}-01`,
    endDate: `${year - 1}-12`,
  });

  await page.goto("/inactive-graves");
  const title = "Beendete Genossenschaftsgräber";
  const ended = panel(page, title);
  const toggle = ended.getByRole("button", { name: title, exact: true });
  const field = ended.getByRole("textbox", { name: `${title} durchsuchen` });
  await expect(toggle).toHaveAttribute("aria-expanded", "true");
  await field.fill(marker);
  await expect(ended.locator("tbody tr")).toHaveCount(1);

  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-expanded", "false");
  await expect(field).toHaveCount(0);
  await expect(ended.locator("table")).toHaveCount(0);
  // the others stay open
  await expect(
    panel(page, "Beendete Gräber").getByRole("textbox", { name: "Beendete Gräber durchsuchen" }),
  ).toBeVisible();

  // opened again, it has kept its search
  await toggle.click();
  await expect(field).toHaveValue(marker);
  await expect(ended.locator("tbody tr")).toHaveCount(1);

  // folded, it stays folded across a reload
  await toggle.click();
  await page.reload();
  await expect(toggle).toHaveAttribute("aria-expanded", "false");
  await expect(field).toHaveCount(0);
});

test("a panel shows ten graves a page, of those its search leaves", async ({ page }) => {
  const marker = `IG-${unique()}`;
  const year = new Date().getFullYear();
  await Promise.all(
    Array.from({ length: 11 }, (_, i) =>
      seedCooperative(page, `${marker}-${String(i + 1).padStart(2, "0")}`, {
        startDate: `${year - 2}-01`,
        endDate: `${year - 1}-12`,
      }),
    ),
  );

  await page.goto("/inactive-graves");
  const title = "Beendete Genossenschaftsgräber";
  const ended = panel(page, title);
  const field = ended.getByRole("textbox", { name: `${title} durchsuchen` });
  await field.fill(marker);
  const rows = ended.locator("tbody tr");
  await expect(rows).toHaveCount(10);
  await expect(ended.getByText("1–10 von 11")).toBeVisible();

  await ended.getByRole("button", { name: "Nächste Seite" }).click();
  await expect(rows).toHaveCount(1);
  await expect(ended.getByText("11–11 von 11")).toBeVisible();

  // a narrower search starts on page one again, and needs no paginator
  await field.fill(`${marker}-0`);
  await expect(rows).toHaveCount(9);
  await expect(ended.getByRole("button", { name: "Nächste Seite" })).toHaveCount(0);
});
