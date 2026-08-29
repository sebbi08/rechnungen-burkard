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
