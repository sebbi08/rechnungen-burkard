import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { fetchGraveInvoiceGroup } from "@/api/endpoints";
import { halfYearText } from "@/lib/halfYear";
import { type DraftLine, newLine } from "@/lib/lines";
import { useErrorToast } from "./useErrorToast";
import { showMessage } from "./useMessage";

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
  /** How many recipients share the bill: the divisor its prices must split by. */
  recipients: number;
}

/** Opens the grave-invoice editor -- or explains why not. One edit revises
 *  every recipient of the grave's half-year, so one paid recipient blocks all
 *  of them, and the group names who they are. */
export function useGraveInvoiceEdit() {
  const { t } = useI18n();
  const { showError } = useErrorToast();
  const visible = ref(false);
  const draft = ref<GraveInvoiceDraft | null>(null);

  async function open(invoiceId: number): Promise<void> {
    try {
      const group = await fetchGraveInvoiceGroup(invoiceId);
      const paid = group.Recipients.filter((recipient) => recipient.Paid);
      if (paid.length) {
        showMessage(
          t("graveInvoiceEditTitle"),
          t("errorGraveInvoicePaidNames", [paid.map((recipient) => recipient.Name).join(", ")]),
        );
        return;
      }
      draft.value = {
        invoiceId,
        recipients: group.Recipients.length,
        warning: t("graveInvoiceEditWarning", [
          halfYearText(group.PeriodYear, group.PeriodHalf),
          group.Recipients.map((recipient) => recipient.Name).join(", "),
        ]),
        // Grave-level lines: the full count at the full price, as stored. A net
        // price is not rounded -- an old line keeps three decimals on purpose.
        lines: group.Lines.map((line) =>
          newLine({
            itemId: line.ItemId,
            count: line.Count,
            unitPrice: line.UnitPrice,
          }),
        ),
      };
      visible.value = true;
    } catch (error) {
      showError(error);
    }
  }

  return { visible, draft, open };
}
