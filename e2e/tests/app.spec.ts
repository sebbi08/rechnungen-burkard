import { expect, test } from "@playwright/test";
import { createRow, ensureRow, seedGrave } from "../seed";

// A navigation bar lists the sections, the invoice list is the start page, and
// a detail page leads back through its breadcrumb.

const sections = [
  { label: "Rechnungen", path: "/invoices" },
  { label: "Gräber", path: "/graves" },
  { label: "Leistungen", path: "/items" },
  { label: "Empfänger", path: "/recipients" },
  { label: "Import/Export", path: "/import-export" },
  { label: "Einstellungen", path: "/settings" },
];

test("the start page is the invoice list and the navigation reaches every section", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/invoices$/);
  await expect(page.getByRole("heading", { level: 1, name: "Rechnungen" })).toBeVisible();

  const nav = page.getByRole("navigation", { name: "Navigation" });
  for (const section of sections) {
    const link = nav.getByRole("link", { name: section.label, exact: true });
    await link.click();
    await expect(page).toHaveURL(new RegExp(`${section.path}$`));
    await expect(page.getByRole("heading", { level: 1, name: section.label })).toBeVisible();
    // the section the page belongs to is the one marked as current
    await expect(link).toHaveAttribute("aria-current", "page");
  }
});

test.describe("a browser that prefers English", () => {
  test.use({ locale: "en-US" });

  test("still gets the German UI", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1, name: "Rechnungen" })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("lang", "de");
  });
});

test("an unknown path leads to the start page", async ({ page }) => {
  await page.goto("/gibt-es-nicht");
  await expect(page).toHaveURL(/\/invoices$/);
});

test("a detail page keeps its section marked and leads back through its breadcrumb", async ({
  page,
}) => {
  const ts = Date.now();
  const recipient = await createRow(page, "/odata/Recipients", {
    Name: `Navigation ${ts}`,
    Address: "Musterweg 1",
  });
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  const item = await createRow(page, "/odata/InvoiceItems", {
    Number: `NAV-${ts}`,
    Description: "Grabpflege",
    VatRate: 7,
    DefaultPrice: 10,
  });
  const graveId = await seedGrave(page, {
    graveNumber: `NAV-${ts}`,
    items: [{ itemId: item.Id }],
    recipientIds: [recipient.Id],
  });

  await page.goto(`/graves/${graveId}`);
  await expect(page.getByRole("heading", { level: 1, name: `NAV-${ts}` })).toBeVisible();
  // a detail route is no child of its list's route, so this is not RouterLink's own marking
  const nav = page.getByRole("navigation", { name: "Navigation" });
  await expect(nav.getByRole("link", { name: "Gräber", exact: true })).toHaveAttribute(
    "aria-current",
    "page",
  );

  await page
    .getByRole("navigation", { name: "Breadcrumb" })
    .getByRole("link", { name: "Gräber" })
    .click();
  await expect(page).toHaveURL(/\/graves$/);
  await expect(page.getByRole("heading", { level: 1, name: "Gräber" })).toBeVisible();
});
