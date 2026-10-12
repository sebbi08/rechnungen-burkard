import { computed, type ComputedRef } from "vue";
import { useI18n } from "vue-i18n";

/** A paginator's "1–100 von 163". PrimeVue fills in its own placeholders, so
 *  they pass through the catalogue's. */
export function usePageReport(): ComputedRef<string> {
  const { t } = useI18n();
  return computed(() =>
    t("pageReport", { first: "{first}", last: "{last}", total: "{totalRecords}" }),
  );
}
