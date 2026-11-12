import { computed, type ComputedRef } from "vue";
import { useI18n } from "vue-i18n";
import { PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

/** A paginator's "1–10 von 163". PrimeVue fills in its own placeholders, so
 *  they pass through the catalogue's. */
export function usePageReport(): ComputedRef<string> {
  const { t } = useI18n();
  return computed(() =>
    t("pageReport", { first: "{first}", last: "{last}", total: "{totalRecords}" }),
  );
}

/** The paging of a table that is mostly short, for `v-bind`: PAGE_SIZE rows a
 *  page, and the paginator only once there is a second one. It stays mounted
 *  while hidden, so a table that shrinks back to one page returns to it. */
export function useShortTablePaging() {
  const report = usePageReport();
  return computed(() => ({
    paginator: true,
    alwaysShowPaginator: false,
    rows: PAGE_SIZE,
    paginatorTemplate: PAGINATOR_TEMPLATE,
    currentPageReportTemplate: report.value,
  }));
}
