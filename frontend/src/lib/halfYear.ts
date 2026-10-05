export interface YearHalfOption {
  key: string;
  text: string;
}

/** The half-year today falls in: Jan-Jun is 1, Jul-Dec is 2. The graves
 *  overview, the grave detail page and the generation dialog all open on it, so
 *  they agree on which graves are still to be invoiced. */
export function currentHalfYear(): [number, number] {
  const now = new Date();
  return [now.getFullYear(), now.getMonth() < 6 ? 1 : 2];
}

// Half-year keys ("2026-1", "2026-2") for the given year range, newest first.
export function halfYearOptions(yearsBack: number, yearsAhead: number): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (let year = currentYear + yearsAhead; year >= currentYear - yearsBack; year--) {
    options.push({ key: `${year}-2`, text: `H2 ${year}` });
    options.push({ key: `${year}-1`, text: `H1 ${year}` });
  }
  return options;
}

// Year-only keys ("2026") for the given year range, newest first.
export function yearOptions(yearsBack: number, yearsAhead: number): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (let year = currentYear + yearsAhead; year >= currentYear - yearsBack; year--) {
    options.push({ key: String(year), text: String(year) });
  }
  return options;
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
