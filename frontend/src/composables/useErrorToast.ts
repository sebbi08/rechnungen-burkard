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
    // a text for this many args if there is one, else the code's own text:
    // an arg the backend adds later should not lose the message altogether
    const counted = list ? `error${code}${list.length}` : undefined;
    const plain = `error${code}`;
    let detail = `${t("errorGeneric")} (${code})`;
    if (counted && i18n.te(counted)) {
      detail = t(counted, list ?? []);
    } else if (i18n.te(plain)) {
      detail = list ? t(plain, list) : t(plain);
    }
    toast.add({ severity: "error", summary: t("errorTitle"), detail, life: 8000 });
  }

  return { showError };
}
