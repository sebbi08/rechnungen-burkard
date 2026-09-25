/** vue-i18n's `t`, narrowed to what the lib helpers need. */
export type Translate = (key: string, list?: unknown[]) => string;

export function formatDate(value: Date | string | null | undefined): string {
  if (value == null || value === "") {
    return "";
  }
  const date = value instanceof Date ? value : new Date(value);
  return date.toLocaleDateString("de-DE");
}

export function formatAmount(value: number | null | undefined): string {
  if (value == null) {
    return "";
  }
  return `${value.toLocaleString("de-DE", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} €`;
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

/** "33,3 %": a recipient's share, one decimal, as the UI5 detail page shows it. */
export function formatShare(value: number | null | undefined): string {
  return value == null ? "" : `${value.toFixed(1).replace(".", ",")} %`;
}

/** yyyy-MM-dd from the local calendar fields. Not toISOString(): a DatePicker
 *  value is local midnight, which is the previous day in UTC here. */
export function toIsoDate(date: Date | null | undefined): string {
  if (!date) {
    return "";
  }
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

/** An Edm.Date ("2026-01-31") as local midnight, for a DatePicker. */
export function parseIsoDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  return new Date(year, month - 1, day);
}
