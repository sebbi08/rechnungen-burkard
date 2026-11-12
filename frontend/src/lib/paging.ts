/** How many rows a paged list shows at a time. */
export const PAGE_SIZE = 10;

/** The paginator every paged list shows: the page links, then "1–10 von 163". */
export const PAGINATOR_TEMPLATE =
  "FirstPageLink PrevPageLink PageLinks NextPageLink LastPageLink CurrentPageReport";

/** The first row of the last page that still has rows, for a list that shrank
 *  under the page it was on. */
export function lastPageStart(total: number): number {
  return total > 0 ? Math.floor((total - 1) / PAGE_SIZE) * PAGE_SIZE : 0;
}
