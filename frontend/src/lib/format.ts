/** vue-i18n's `t`, narrowed to what the lib helpers need. */
export type Translate = (key: string, list?: unknown[]) => string;

export function formatDate(value: Date | string | null | undefined): string {
  if (value == null || value === "") {
    return "";
  }
  // An Edm.Date ("2026-01-01") is a calendar day. new Date() reads it as UTC
  // midnight, which is the day before anywhere west of UTC; a timestamp
  // (GeneratedAt) is an instant and converts as one.
  const date =
    value instanceof Date
      ? value
      : /^\d{4}-\d{2}-\d{2}$/.test(value)
        ? parseIsoDate(value)!
        : new Date(value);
  // two-digit day and month, like the date pickers and like the invoice
  // search, which matches "01.10.2026" but not "1.10.2026"
  return date.toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit", year: "numeric" });
}

/** An instant (UTC ISO) as German local date and time, "01.10.2026, 14:30". */
export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("de-DE", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function formatAmount(value: number | null | undefined): string {
  if (value == null) {
    return "";
  }
  return `${value.toLocaleString("de-DE", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`;
}

/** A count the German way, with a thousands separator: "1.234". */
export function formatCount(value: number): string {
  return value.toLocaleString("de-DE", { maximumFractionDigits: 0 });
}

/** "19", or "7,7" where a rate has a decimal -- never "19.00". */
export function formatVatRate(value: number | null | undefined): string {
  return value == null ? "" : value.toLocaleString("de-DE", { maximumFractionDigits: 2 });
}

/** "2026-0042-R1" -> "2026-0042": the chain, without the revision marker. */
export function formatInvoiceNumber(value: string | null | undefined): string {
  if (value == null) {
    return "";
  }
  const index = value.lastIndexOf("-R");
  return index >= 0 ? value.slice(0, index) : value;
}

export function formatRevisionBadge(value: number | null | undefined): string {
  return value != null && value > 0 ? `R${value}` : "";
}

/** The first line of a multi-line address. */
export function formatAddressLine(value: string | null | undefined): string {
  return (value ?? "").split("\n")[0].trim();
}

/** "33,33 %", "50 %": a recipient's share as the invoice prints it, with up
 *  to two decimals and none that say nothing. */
export function formatShare(value: number | null | undefined): string {
  return value == null ? "" : `${value.toLocaleString("de-DE", { maximumFractionDigits: 2 })} %`;
}

/** A UTC timestamp the API serialises without a zone ("2026-11-11T23:30:00") as
 *  the local calendar date, formatted: the first ten characters alone would show
 *  the UTC day, which near midnight is the previous or next local one. */
export function formatDeletedDate(value: string | null | undefined): string {
  if (value == null) {
    return "";
  }
  const hasZone = /(Z|[+-]\d\d:?\d\d)$/i.test(value);
  return formatDate(new Date(hasZone ? value : `${value}Z`));
}

/** An Edm.Date ("2026-01-31") as local midnight, for a DatePicker. */
export function parseIsoDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  return new Date(year, month - 1, day);
}

/** "2026-03" from a month picker's value, by its local fields (never
 *  toISOString, which is the previous day -- and at the turn of a month the
 *  previous month -- here). Null for no value. */
export function toIsoMonth(date: Date | null | undefined): string | null {
  if (!date) {
    return null;
  }
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}

/** "03/2026" from an Edm.Date ("2026-03-01"); empty for none. */
export function formatMonth(value: string | null | undefined): string {
  if (!value) {
    return "";
  }
  const [year, month] = value.split("-");
  return `${month}/${year}`;
}
