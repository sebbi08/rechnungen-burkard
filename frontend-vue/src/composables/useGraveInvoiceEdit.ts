import { useQueryClient } from "@tanstack/vue-query";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { fetchGraveInvoiceGroup } from "@/api/endpoints";
import { loadItemVatRates } from "@/api/queries";
import { halfYearText } from "@/lib/halfYear";
import { type DraftLine, newLine } from "@/lib/lines";
import { priceFieldValue } from "@/lib/money";
import { useErrorToast } from "./useErrorToast";
import { showMessage } from "./useMessage";

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
}

/** Opens the grave-invoice editor -- or explains why not. One edit revises
 *  every recipient of the grave's half-year, so one paid recipient blocks all
 *  of them, and the group names who they are. */
export function useGraveInvoiceEdit() {
  const { t } = useI18n();
  const { showError } = useErrorToast();
  const queryClient = useQueryClient();
  const visible = ref(false);
  const draft = ref<GraveInvoiceDraft | null>(null);

  async function open(invoiceId: number): Promise<void> {
    try {
      const [group, rates] = await Promise.all([
        fetchGraveInvoiceGroup(invoiceId),
        loadItemVatRates(queryClient),
      ]);
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
        warning: t("graveInvoiceEditWarning", [
          halfYearText(group.PeriodYear, group.PeriodHalf),
          group.Recipients.map((recipient) => recipient.Name).join(", "),
        ]),
        // Grave-level lines: the full count at the full price. A net price is
        // not rounded -- an old line keeps three decimals on purpose -- and only
        // a VAT-included one is converted (GraveEdit.controller.ts).
        lines: group.Lines.map((line) =>
          newLine({
            itemId: line.ItemId,
            count: line.Count,
            unitPrice: priceFieldValue(
              line.UnitPrice,
              line.PriceIncludesVat,
              rates.get(line.ItemId) ?? 0,
            ),
            priceIncludesVat: line.PriceIncludesVat,
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
