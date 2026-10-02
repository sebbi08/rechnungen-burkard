import type { Page } from "@playwright/test";

/**
 * Creates a row and returns it.
 *
 * The whole suite writes to one SQLite file, and under the load of the parallel
 * specs a write can fail. A failed attempt is retried, and a permanent failure
 * names the status — otherwise a dropped seed write surfaces much later as a
 * test timing out on a row that was never created, which reads like a race in
 * the feature under test rather than a lost insert.
 */
export async function createRow(
  page: Page,
  path: string,
  data: Record<string, unknown>,
): Promise<any> {
  let failure = "no attempt";
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await page.request.post(path, { data });
    const body = await response.text();
    if (response.ok() && body) {
      return JSON.parse(body);
    }
    failure = `HTTP ${response.status()} ${body}`;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`POST ${path} failed: ${failure}`);
}

/**
 * Creates a row that several specs share, such as a VAT rate or a catalogue item.
 * Finding it already there is success; anything else is retried like createRow.
 *
 * The API reports an existing row as a 400 carrying an error code ending in
 * "Exists" (VatRateExists, ItemNumberExists, GraveRecipientLinkExists, ...), not
 * as a 409 — so the code is what this reads, not the status alone.
 */
export async function ensureRow(
  page: Page,
  path: string,
  data: Record<string, unknown>,
): Promise<void> {
  let failure = "no attempt";
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await page.request.post(path, { data });
    if (response.ok() || response.status() === 409) {
      return;
    }

    const body = await response.text();
    if (/"code"\s*:\s*"[A-Za-z]*Exists"/.test(body)) {
      return;
    }

    failure = `HTTP ${response.status()} ${body}`;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`POST ${path} failed: ${failure}`);
}

/** One item line of a grave `seedGrave` saves. A `unitPrice` left out (or null)
 *  bills the item's default price, as the dialog does when the pre-filled price
 *  is left alone; `priceIncludesVat` says the price is a gross one. */
export interface SeedGraveLine {
  itemId: number;
  count?: number;
  unitPrice?: number | null;
  priceIncludesVat?: boolean;
}

export interface SeedGraveInput {
  graveNumber: string;
  fieldNumber?: string;
  siteName?: string;
  position?: string;
  /** "yyyy-MM" */
  startDate?: string;
  /** "yyyy-MM", or null for no end */
  endDate?: string | null;
  items: SeedGraveLine[];
  recipientIds: number[];
}

/**
 * Creates a grave the way the app does — one POST to SaveGrave carrying the
 * grave, its items and its recipients — and returns the new grave's id.
 *
 * The Graves, GraveItems and GraveRecipients sets take no writes any more, so
 * a spec's grave passes the same checks as the dialog's: at least one
 * recipient, a price on every line (the link's own or the item's default, with
 * at most two decimals), and per VAT rate a net subtotal that divides evenly
 * across the recipients. A fixture that breaks one of these is refused as a
 * whole rather than written half-way, and createRow's retry-and-throw names
 * the code.
 */
export async function seedGrave(page: Page, grave: SeedGraveInput): Promise<number> {
  const created = await createRow(page, "/odata/SaveGrave", {
    graveId: null,
    graveNumber: grave.graveNumber,
    fieldNumber: grave.fieldNumber ?? "1",
    siteName: grave.siteName ?? "Schineller Bäcke",
    position: grave.position ?? "SingleInside",
    startDate: grave.startDate ?? "2026-01",
    endDate: grave.endDate === undefined ? "2040-12" : grave.endDate,
    items: grave.items.map((line) => ({
      itemId: line.itemId,
      count: line.count ?? 1,
      unitPrice: line.unitPrice ?? null,
      priceIncludesVat: line.priceIncludesVat ?? false,
    })),
    recipientIds: grave.recipientIds,
  });
  return created.Id;
}
