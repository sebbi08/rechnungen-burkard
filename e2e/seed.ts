import type { Page } from "@playwright/test";

/**
 * Creates a row and returns it.
 *
 * The whole suite writes to one SQLite file, and under the load of the parallel
 * specs a write can fail. A server failure (5xx) is retried, and a permanent
 * failure names the status — otherwise a dropped seed write surfaces much later
 * as a test timing out on a row that was never created, which reads like a race
 * in the feature under test rather than a lost insert. A refusal (4xx) is the
 * seed's own mistake and fails at once: asking again only delays the message.
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
    if (response.status() < 500) {
      break;
    }
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
    if (response.status() < 500) {
      break;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`POST ${path} failed: ${failure}`);
}

/** The cemetery the specs' graves lie in, unless a test is about cemeteries. */
export const SEED_CEMETERY = "Bergfriedhof";

/**
 * Makes sure a cemetery of this name exists and answers its id. Every grave
 * needs one, and most specs share the one cemetery rather than each creating
 * their own: a cemetery is created once and found by every later call, the
 * way ensureRow finds a VAT rate.
 */
export async function ensureCemetery(page: Page, name = SEED_CEMETERY): Promise<number> {
  await ensureRow(page, "/odata/Cemeteries", { Name: name });
  const filter = encodeURIComponent(`Name eq '${name.replaceAll("'", "''")}'`);
  const response = await page.request.get(`/odata/Cemeteries?$filter=${filter}&$select=Id`);
  const found = (await response.json()).value;
  if (!response.ok() || found.length !== 1) {
    throw new Error(`cemetery "${name}" not found after ensuring it: HTTP ${response.status()}`);
  }
  return found[0].Id;
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
  /** Defaults to SEED_CEMETERY. */
  cemeteryId?: number;
  position?: string;
  /** "yyyy-MM" */
  startDate?: string;
  /** "yyyy-MM", or null for no end */
  endDate?: string | null;
  items: SeedGraveLine[];
  recipientIds: number[];
  /** A cooperative grave skips the recipient, price and divide rules below. */
  isCooperative?: boolean;
}

/**
 * Creates a grave the way the app does — one POST to SaveGrave carrying the
 * grave, its items and its recipients — and returns the new grave's id.
 *
 * The Graves, GraveItems and GraveRecipients sets take no writes any more, so
 * a spec's grave passes the same checks as the dialog's: a cemetery, at
 * least one recipient, a price on every line (the link's own or the item's
 * default, with at most two decimals), and per VAT rate a net subtotal that
 * divides evenly across the recipients. A fixture that breaks one of these is refused as a
 * whole rather than written half-way, and createRow's retry-and-throw names
 * the code.
 */
export async function seedGrave(page: Page, grave: SeedGraveInput): Promise<number> {
  const created = await createRow(page, "/odata/SaveGrave", {
    graveId: null,
    graveNumber: grave.graveNumber,
    fieldNumber: grave.fieldNumber ?? "1",
    siteName: grave.siteName ?? "Schineller Bäcke",
    cemeteryId: grave.cemeteryId ?? (await ensureCemetery(page)),
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
    isCooperative: grave.isCooperative ?? false,
  });
  return created.Id;
}
