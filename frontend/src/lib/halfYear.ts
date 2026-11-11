import { BillingQuarter } from "@/api/generated/burkard-invoicing-domain-entities";

/** The half-year today falls in: Jan-Jun is 1, Jul-Dec is 2. The graves
 *  overview, the grave detail page and the generation dialog all open on it, so
 *  they agree on which graves are still to be invoiced. */
export function currentHalfYear(): [number, number] {
  const now = new Date();
  return [now.getFullYear(), now.getMonth() < 6 ? 1 : 2];
}

/** The first month of the current half-year, "yyyy-01" or "yyyy-07": the
 *  boundary between the current graves and the ended ones, so the lists on
 *  either side of it stay exact complements. */
export function currentHalfYearStartMonth(): string {
  const [year, half] = currentHalfYear();
  return `${year}-${half === 1 ? "01" : "07"}`;
}

/** How many half-years back a generation run may reach; the backend refuses older ones. */
export const GENERATION_LOOK_BACK = 2;

/** The earliest half-year a run may generate: the current one, two half-years back. */
export function earliestGenerationHalfYear(): [number, number] {
  const [year, half] = currentHalfYear();
  const index = year * 2 + (half - 1) - GENERATION_LOOK_BACK;
  return [Math.floor(index / 2), (index % 2) + 1];
}

/** Whether the first half-year lies before the second. */
export function halfYearBefore(
  year: number,
  half: number,
  otherYear: number,
  otherHalf: number,
): boolean {
  return year < otherYear || (year === otherYear && half < otherHalf);
}

/** "H1 2026" — the label the half-year selects already use. */
export function halfYearText(year: number, half: number): string {
  return `H${half} ${year}`;
}

/** "2026-1": the key the half-year selects use. */
export function halfYearKey(year: number, half: number): string {
  return `${year}-${half}`;
}

export function parseHalfYearKey(key: string): [number, number] {
  const [year, half] = key.split("-").map(Number);
  return [year, half];
}

/** Whether a grave's term ("yyyy-MM…" strings, end optional) has a month in
 *  the half-year. Strings of the same shape compare like their months. */
export function halfYearInTerm(
  start: string,
  end: string | null,
  year: number,
  half: number,
): boolean {
  const first = `${year}-${half === 1 ? "01" : "07"}`;
  const last = `${year}-${half === 1 ? "06" : "12"}`;
  return start.slice(0, 7) <= last && (end == null || end.slice(0, 7) >= first);
}

const QUARTER_NUMBER: Record<BillingQuarter, number> = {
  [BillingQuarter.Spring]: 1,
  [BillingQuarter.Summer]: 2,
  [BillingQuarter.Autumn]: 3,
  [BillingQuarter.Winter]: 4,
};

/** Whether a grave with these items bills at least one line in the half-year:
 *  an item without a quarter whenever the term has a month in the half-year,
 *  one with a quarter only when the term's months inside the half-year touch
 *  that quarter. Mirrors `HalfYearBilling` in the backend, which decides which
 *  graves a generation run invoices. */
export function halfYearHasBilling(
  start: string,
  end: string | null,
  year: number,
  half: number,
  quarters: (`${BillingQuarter}` | null)[],
): boolean {
  if (!halfYearInTerm(start, end, year, half)) {
    return false;
  }
  // the months of the half-year inside the term, as 1-12
  const halfFirst = half === 1 ? 1 : 7;
  const startsIn = Number(start.slice(0, 4)) === year ? Number(start.slice(5, 7)) : 0;
  const endsIn = end != null && Number(end.slice(0, 4)) === year ? Number(end.slice(5, 7)) : 13;
  const first = Math.max(halfFirst, startsIn);
  const last = Math.min(halfFirst + 5, endsIn);
  return quarters.some((quarter) => {
    if (quarter == null) {
      return true;
    }
    const quarterFirst = (QUARTER_NUMBER[quarter] - 1) * 3 + 1;
    return quarterFirst <= last && first <= quarterFirst + 2;
  });
}
