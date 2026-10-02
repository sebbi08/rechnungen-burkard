import { useToast } from "primevue/usetoast";
import { useI18n } from "vue-i18n";
import { toApiError } from "@/api/http";
import { BlockingBusyError } from "./useBlocking";

/** Shows a failed call or a failed client check. A known code has its own
 *  text (`error<Code>`); anything else shows the generic text with the code. */
export function useErrorToast() {
  const toast = useToast();
  const i18n = useI18n();
  const { t } = i18n;

  function showError(error: unknown): void {
    if (error instanceof BlockingBusyError) {
      return;
    }
    const { code, body } = toApiError(error);
    // A refusal that names things (GraveNumberInUse) sends them as args; its
    // text is chosen by how many there are, e.g. errorGraveNumberInUse2.
    const args = (body as { error?: { args?: unknown } } | undefined)?.error?.args;
    const list = Array.isArray(args) ? args.map(String) : undefined;
    const key = list ? `error${code}${list.length}` : `error${code}`;
    toast.add({
      severity: "error",
      summary: t("errorTitle"),
      detail: i18n.te(key) ? (list ? t(key, list) : t(key)) : `${t("errorGeneric")} (${code})`,
      life: 8000,
    });
  }

  return { showError };
}
