import type { BillingQuarter } from "@/api/generated";

/** The backend's BillingQuarter members, in calendar order. */
export const QUARTERS = ["Spring", "Summer", "Autumn", "Winter"] as const;

export type QuarterName = (typeof QUARTERS)[number];

/** The i18n key naming a quarter; an item without one is billed all year. */
export function quarterLabelKey(quarter: string | null | undefined): string {
  return quarter ? `quarter${quarter}` : "quarterNone";
}

/** A quarter as the editor's select holds it; anything else is "all year". */
export function toQuarterName(value: string | null | undefined): QuarterName | null {
  return QUARTERS.find((quarter) => quarter === value) ?? null;
}

/** The generated client's enum for the same name. A string enum does not take
 *  a literal union directly, so it goes through string. */
export function toBillingQuarter(name: QuarterName | null): BillingQuarter | null {
  return name == null ? null : (name as string as BillingQuarter);
}
