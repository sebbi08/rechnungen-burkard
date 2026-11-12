import { useQueryClient } from "@tanstack/vue-query";
import { reactive } from "vue";
import { useI18n } from "vue-i18n";
import { markInvoicesPaid } from "@/api/endpoints";
import type { Invoice } from "@/api/generated";
import { fetchOpenGraveInvoices } from "@/api/queries";
import { formatAmount, formatInvoiceNumber } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";
import { useConfirmAsync } from "./useConfirmAsync";
import { useErrorToast } from "./useErrorToast";

/** The grave list's "mark paid": every open invoice of the grave, whatever its
 *  half-year. A single one is marked after a yes; several open a dialog in
 *  which the user ticks what has been paid (GraveMarkPaidDialog). */
export function useGraveMarkPaid() {
  const ask = useConfirmAsync();
  const { showError } = useErrorToast();
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const dialog = reactive({
    visible: false,
    graveNumber: "",
    invoices: [] as Invoice[],
    busy: false,
  });
  // the grave whose invoices are being read, so a double click reads them once
  let loading: number | null = null;

  async function mark(invoiceIds: number[]): Promise<void> {
    try {
      await markInvoicesPaid(invoiceIds);
    } catch (error) {
      showError(error);
    } finally {
      // on a refusal, too: the list may have been out of date
      await queryClient.invalidateQueries();
    }
  }

  async function start(graveId: number, graveNumber: string): Promise<void> {
    if (loading === graveId) {
      return;
    }
    loading = graveId;
    let invoices: Invoice[];
    try {
      invoices = await fetchOpenGraveInvoices(graveId);
    } catch (error) {
      showError(error);
      return;
    } finally {
      loading = null;
    }

    if (invoices.length === 0) {
      // someone else marked them meanwhile: show the list as it is now
      await queryClient.invalidateQueries();
      return;
    }
    if (invoices.length === 1) {
      const [invoice] = invoices;
      const question = t("confirmMarkPaidOne", [
        formatInvoiceNumber(invoice.InvoiceNumber),
        halfYearText(invoice.PeriodYear ?? 0, invoice.PeriodHalf ?? 0),
        formatAmount(invoice.GrossTotal),
      ]);
      if (await ask(question)) {
        await mark([invoice.Id]);
      }
      return;
    }
    Object.assign(dialog, { visible: true, graveNumber, invoices });
  }

  // A refusal closes the dialog as well: what it listed was out of date, and
  // opening it again reads the invoices as they are now.
  async function confirm(invoiceIds: number[]): Promise<void> {
    dialog.busy = true;
    try {
      await mark(invoiceIds);
    } finally {
      dialog.busy = false;
      dialog.visible = false;
    }
  }

  return { dialog, start, confirm };
}
