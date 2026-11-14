import AdmZip from "adm-zip";
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
 *  is left alone. */
export interface SeedGraveLine {
  itemId: number;
  count?: number;
  unitPrice?: number | null;
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
  /** A cooperative grave needs one; defaults to "V-1" for it. */
  contractNumber?: string;
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
    })),
    recipientIds: grave.recipientIds,
    isCooperative: grave.isCooperative ?? false,
    contractNumber: grave.isCooperative ? (grave.contractNumber ?? "V-1") : null,
  });
  return created.Id;
}

/**
 * Creates an ordinary grave without recipients, the state a grave is in after
 * an import that had none to link, and returns its id. SaveGrave refuses to
 * create one, so this goes the way such a grave arises: a one-grave archive
 * through ImportArchive, with one priced all-year item and no recipient link.
 * The marker names the grave and its item, so it has to be new. Its term lies
 * far ahead, where no test generates invoices, so it stays without any.
 */
export async function seedIncompleteGrave(page: Page, marker: string): Promise<number> {
  await ensureRow(page, "/odata/VatRates", { Rate: 7 });
  await ensureCemetery(page);
  const zip = new AdmZip();
  const files: Record<string, string> = {
    "recipients.csv": "RecipientNumber,Name,Address,Email\r\n",
    "items.csv":
      "ItemNumber,Description,VatRate,Price,Quarter\r\n" +
      `${marker}-item,Grabpflege,7,100.00,\r\n`,
    "graves.csv":
      "GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative,ContractNumber\r\n" +
      `${marker},1,Schineller Bäcke,${SEED_CEMETERY},SingleInside,2044-01,2045-12,,\r\n`,
    "mapping.csv":
      "Type,GraveNumber,Reference,Count,PriceIncludesVat,UnitPrice\r\n" +
      `Item,${marker},${marker}-item,1,,\r\n`,
  };
  for (const [name, content] of Object.entries(files)) {
    zip.addFile(name, Buffer.from(content, "utf8"));
  }
  const response = await page.request.post("/odata/ImportArchive", {
    multipart: {
      file: { name: "import.zip", mimeType: "application/zip", buffer: zip.toBuffer() },
    },
  });
  if (!response.ok()) {
    throw new Error(`ImportArchive failed: HTTP ${response.status()} ${await response.text()}`);
  }
  const filter = encodeURIComponent(`GraveNumber eq '${marker}'`);
  const found = (
    await (await page.request.get(`/odata/Graves?$filter=${filter}&$select=Id`)).json()
  ).value;
  if (found.length !== 1) {
    throw new Error(`grave "${marker}" not found after the import`);
  }
  return found[0].Id;
}

/** One line of a quote `seedQuote` creates; a `unitPrice` left out bills the
 *  item's default price, which the item must then have. */
export interface SeedQuoteLine {
  itemId: number;
  count: number;
  unitPrice?: number;
}

/**
 * Creates an open quote the way the editor does — one POST to CreateQuote —
 * and returns its id and number.
 */
export async function seedQuote(
  page: Page,
  input: { recipientId: number; lines: SeedQuoteLine[] },
): Promise<{ id: number; number: string }> {
  const created = await createRow(page, "/odata/CreateQuote", {
    recipientId: input.recipientId,
    lines: input.lines.map((line) => ({
      itemId: line.itemId,
      count: line.count,
      unitPrice: line.unitPrice ?? null,
      priceIncludesVat: false,
    })),
  });
  return { id: created.Id, number: created.QuoteNumber };
}
