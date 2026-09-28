import { useToast } from "primevue/usetoast";
import { useI18n } from "vue-i18n";
import { toApiError } from "@/api/http";
import { BlockingBusyError } from "./useBlocking";

/** Shows a failed call or a failed client check. A known code has its own
 *  text (`error<Code>`, the keys the UI5 app's CODE_TO_I18N maps to); anything
 *  else shows the generic text with the code. */
export function useErrorToast() {
  const toast = useToast();
  const i18n = useI18n();
  const { t } = i18n;

  function showError(error: unknown): void {
    if (error instanceof BlockingBusyError) {
      return;
    }
    const { code } = toApiError(error);
    const key = `error${code}`;
    toast.add({
      severity: "error",
      summary: t("errorTitle"),
      detail: i18n.te(key) ? t(key) : `${t("errorGeneric")} (${code})`,
      life: 8000,
    });
  }

  return { showError };
}
