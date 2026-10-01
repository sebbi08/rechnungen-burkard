import { useQueryClient } from "@tanstack/vue-query";
import { useI18n } from "vue-i18n";
import { setInvoicePaid } from "@/api/endpoints";
import { useConfirmAsync } from "./useConfirmAsync";
import { useErrorToast } from "./useErrorToast";

/** Invoices with a toggle in flight, shared by every list and page. */
const pending = new Set<number>();

/** Marks an unpaid invoice paid straight away, or asks before taking a mark
 *  back -- unmarking discards the recorded day, which no screen shows.
 *  Answers whether anything changed. */
export function usePaidToggle() {
  const ask = useConfirmAsync();
  const { showError } = useErrorToast();
  const { t } = useI18n();
  const queryClient = useQueryClient();

  return async (invoiceId: number, paidOn: string | null): Promise<boolean> => {
    // a double click sent the call twice; harmless while it is idempotent
    if (pending.has(invoiceId)) {
      return false;
    }
    pending.add(invoiceId);
    try {
      if (paidOn && !(await ask(t("confirmMarkUnpaid")))) {
        return false;
      }
      await setInvoicePaid(invoiceId, !paidOn);
      await queryClient.invalidateQueries();
      return true;
    } catch (error) {
      showError(error);
      return false;
    } finally {
      pending.delete(invoiceId);
    }
  };
}
