import type { BillingPreviewInvoice, GraveBillingProblem, GraveLineInput } from "@/api/endpoints";
import { formatAmount, formatVatRate, type Translate } from "./format";
import { halfYearText } from "./halfYear";
import type { DraftLine } from "./lines";

/** Why a grave's prices do not split evenly across its recipients, per VAT
 *  rate, with the nearest amounts that would work. */
export function billingProblemText(
  problems: GraveBillingProblem[],
  recipients: number,
  t: Translate,
): string {
  const lines = problems.map((problem) => {
    const sentence = t(recipients === 1 ? "pricesDoNotDivideLineOne" : "pricesDoNotDivideLine", [
      formatVatRate(problem.VatRate),
      formatAmount(problem.Amount),
      recipients,
    ]);
    const options = problem.Suggestions.map((value) => formatAmount(value));
    if (options.length >= 2) {
      return `${sentence} ${t("pricesNearbyValid", [options[0], options[1]])}`;
    }
    if (options.length === 1) {
      return `${sentence} ${t("pricesNearbyValidOne", [options[0]])}`;
    }
    return `${sentence} ${t("pricesAdjustAPrice")}`;
  });
  return [t("pricesDoNotDivideIntro"), "", ...lines].join("\n");
}

/** The heading of an invoice of the preview: its kind, with the half-year
 *  where it belongs to one. */
export function billingTitle(
  invoice: Pick<BillingPreviewInvoice, "Kind" | "Year" | "Half">,
  t: Translate,
): string {
  const key = `billingKind${invoice.Kind}`;
  return invoice.Year == null ? t(key) : t(key, [halfYearText(invoice.Year, invoice.Half)]);
}

/** How many recipients a grave may split its bill across. */
export const MAX_RECIPIENTS = 4;

function isWholeCents(value: number): boolean {
  return Math.abs(value * 100 - Math.round(value * 100)) < 1e-6;
}

/** The rows a billing preview can judge: an item, a whole count above zero and
 *  a price in whole cents, its own or the item's default. A row still being
 *  typed is left out rather than sent to be refused, which would read as the
 *  check being down. `keys` maps each input back to its row. */
export function previewLines(
  lines: DraftLine[],
  defaultPrice: (itemId: number) => number | null,
): { inputs: GraveLineInput[]; keys: number[] } {
  const inputs: GraveLineInput[] = [];
  const keys: number[] = [];
  for (const line of lines) {
    if (line.itemId == null || line.count == null) {
      continue;
    }
    if (!Number.isInteger(line.count) || line.count <= 0) {
      continue;
    }
    const price = line.unitPrice ?? defaultPrice(line.itemId);
    if (price == null || !isWholeCents(price)) {
      continue;
    }
    inputs.push({
      itemId: line.itemId,
      count: line.count,
      unitPrice: line.unitPrice,
    });
    keys.push(line.key);
  }
  return { inputs, keys };
}
