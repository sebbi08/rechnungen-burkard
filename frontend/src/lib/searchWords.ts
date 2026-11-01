/** The words of a search term, lowered the way German text is lowered, so
 *  "ö" and "Ö" are the same word -- as the backend's OrdinalIgnoreCase has
 *  them. A term of only whitespace has no words. */
function searchWords(term: string): string[] {
  return term
    .split(/\s+/)
    .filter((word) => word !== "")
    .map((word) => word.toLocaleLowerCase("de"));
}

/** Whether every word of `term` is found in at least one of `values`, in any
 *  order and ignoring case: "Hanna Bauer" finds "Bauer Hanna". The words need
 *  not share a value, and a missing value holds nothing. A term without words
 *  matches everything. The browser's twin of the backend's
 *  `SearchWords.InAny`. */
export function matchesWords(term: string, ...values: (string | null | undefined)[]): boolean {
  const words = searchWords(term);
  if (words.length === 0) {
    return true;
  }
  const lowered = values
    .filter((value): value is string => value != null)
    .map((value) => value.toLocaleLowerCase("de"));
  return words.every((word) => lowered.some((value) => value.includes(word)));
}
