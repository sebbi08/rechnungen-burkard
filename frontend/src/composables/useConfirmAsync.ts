import { useConfirm } from "primevue/useconfirm";
import { useI18n } from "vue-i18n";

/** A yes/no confirmation that can be awaited. */
export function useConfirmAsync() {
  const confirm = useConfirm();
  const { t } = useI18n();

  return (message: string): Promise<boolean> =>
    new Promise((resolve) => {
      confirm.require({
        header: t("confirmTitle"),
        message,
        icon: "pi pi-exclamation-triangle",
        rejectProps: { label: t("cancel"), severity: "secondary", outlined: true },
        acceptProps: { label: t("yes") },
        accept: () => resolve(true),
        reject: () => resolve(false),
        onHide: () => resolve(false),
      });
    });
}
