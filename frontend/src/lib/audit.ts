import type { Composer } from "vue-i18n";
import { formatAmount, formatDate, formatMonth } from "./format";
import { halfYearText } from "./halfYear";

/** One field of an entry's ChangesJson, as the backend writes it (AuditChange,
 *  serialised with the web defaults, so in camelCase): values are invariant
 *  strings, formatted here by `type`. */
export interface AuditChange {
  field: string;
  label: string | null;
  old: string | null;
  new: string | null;
  type: string;
  period: string | null;
}

type Translate = Composer["t"];

/** The changes of an entry; none for a value that is not a JSON array. */
export function parseChanges(json: string): AuditChange[] {
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as AuditChange[]) : [];
  } catch {
    return [];
  }
}

/** An entry's DetailsJson: an object of strings, or nothing. */
export function parseDetails(json: string | null | undefined): Record<string, string> {
  if (!json) {
    return {};
  }
  try {
    const parsed: unknown = JSON.parse(json);
    return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, string>)
      : {};
  } catch {
    return {};
  }
}

/** "<rootId>:<invoiceNumber>", split on the first colon. */
export function parseInvoiceValue(value: string): { rootId: number; number: string } | null {
  const index = value.indexOf(":");
  const rootId = Number(value.slice(0, index));
  return index > 0 && Number.isInteger(rootId) ? { rootId, number: value.slice(index + 1) } : null;
}

function money(value: string): string {
  return formatAmount(Number(value));
}

/** A change's old or new value the way the rest of the app writes it. A missing
 *  value is "–", except a missing end, which is a grave without one. */
export function formatAuditValue(change: AuditChange, value: string | null, t: Translate): string {
  if (value == null) {
    return change.field === "end" ? t("auditNoEnd") : t("auditNone");
  }
  switch (change.type) {
    case "money":
      return money(value);
    case "month":
      return formatMonth(value);
    case "date":
      return formatDate(value);
    case "bool":
      return value === "true" ? t("yes") : t("no");
    case "position":
      return t(`gravePosition${value}`);
    case "priceSource":
      return value === "own" ? t("auditPriceOwn") : t("auditPriceDefault");
    case "invoice":
      return parseInvoiceValue(value)?.number ?? value;
    default:
      return value;
  }
}

/** "Anfang · H1 2026" for "Start:2026-1"; the year is "-" for a kind that
 *  belongs to no half-year ("FullFirstHalf:--1"): split on the first colon and
 *  the last dash. Null for a period it cannot read. */
export function auditPeriodText(period: string, t: Translate): string | null {
  const colon = period.indexOf(":");
  const dash = period.lastIndexOf("-");
  if (colon < 0 || dash < colon) {
    return null;
  }
  const kind = period.slice(0, colon);
  const year = period.slice(colon + 1, dash);
  const half = Number(period.slice(dash + 1));
  const key = `billingKind${kind}`;
  return year === "-" ? t(key) : t(key, [halfYearText(Number(year), half)]);
}

const KNOWN_ACTIONS = new Set([
  "GraveCreated",
  "GraveEdited",
  "GraveHandedOver",
  "GraveDeleted",
  "GraveRestored",
  "GraveImported",
  "ItemPriceIncreased",
  "ItemDefaultPriceChanged",
  "GraveInvoicesGenerated",
  "GraveInvoiceRevised",
  "InvoiceCreated",
  "InvoiceCreatedFromQuote",
  "InvoiceRevised",
  "InvoicePaid",
  "InvoiceUnpaid",
]);

/** The sentence of an entry's action, with its details filled in. An action
 *  this version does not know reads as the plain "Änderung". */
export function auditActionText(
  action: string,
  details: Record<string, string>,
  t: Translate,
): string {
  const from = details.from ? formatMonth(details.from) : "";
  const year = Number(details.year);
  const half = Number(details.half);
  // a default price set where there was none, or removed, is written empty
  const noPrice = action === "ItemDefaultPriceChanged" ? t("auditNoDefaultPrice") : "";
  const values = {
    from,
    item: details.item ?? "",
    old: details.old ? money(details.old) : noPrice,
    new: details.new ? money(details.new) : noPrice,
    halfYear: details.year && details.half ? halfYearText(year, half) : "",
    quote: details.quote ?? "",
  };
  if (action === "GraveCreated" && from) {
    return t("auditActionGraveCreatedFrom", values);
  }
  return t(KNOWN_ACTIONS.has(action) ? `auditAction${action}` : "auditActionUnattributed", values);
}

/** What a change is about, before its values: the item or invoice it names, the
 *  correction's block, then the field. "Grabpflege: Preis". */
export function auditChangeHead(change: AuditChange, t: Translate): string {
  const parts: string[] = [];
  const period = change.period ? auditPeriodText(change.period, t) : null;
  if (period) {
    parts.push(period);
  }
  if (change.label) {
    parts.push(change.label);
  }
  parts.push(t(`auditField${change.field[0].toUpperCase()}${change.field.slice(1)}`));
  return parts.join(": ");
}
