import { useQueryClient } from "@tanstack/vue-query";
import { useI18n } from "vue-i18n";
import { useRouter } from "vue-router";
import { convertQuote, declineQuote, deleteQuote, reopenQuote } from "@/api/endpoints";
import { useBlocking } from "./useBlocking";
import { useConfirmAsync } from "./useConfirmAsync";
import { useErrorToast } from "./useErrorToast";

/** Quotes with an action in flight, shared by the list and the detail page: a
 *  double click sent the second call into a state the first had already left. */
const pending = new Set<number>();

/** What can be done to a quote, the same from the list and from its page.
 *  Every query is refreshed afterwards -- also when the server refused, since
 *  the page that offered the action may have been out of date. Errors show as
 *  a toast; none of the functions throws. */
export function useQuoteActions() {
  const { t } = useI18n();
  const ask = useConfirmAsync();
  const { showError } = useErrorToast();
  const { run } = useBlocking();
  const queryClient = useQueryClient();
  const router = useRouter();

  /** Runs `call` once per quote at a time; answers whether it succeeded. */
  async function once(id: number, call: () => Promise<void>): Promise<boolean> {
    if (pending.has(id)) {
      return false;
    }
    pending.add(id);
    try {
      await call();
      return true;
    } catch (error) {
      showError(error);
      return false;
    } finally {
      pending.delete(id);
    }
  }

  async function decline(id: number): Promise<void> {
    await once(id, () => declineQuote(id));
    await queryClient.invalidateQueries();
  }

  async function reopen(id: number): Promise<void> {
    await once(id, () => reopenQuote(id));
    await queryClient.invalidateQueries();
  }

  /** Asks, then deletes the quote with every version. Answers whether it was
   *  deleted, so the detail page knows to go back to the list. */
  async function remove(id: number, label: string): Promise<boolean> {
    if (!(await ask(t("confirmDeleteQuote", [label])))) {
      return false;
    }
    const deleted = await once(id, () => deleteQuote(id));
    if (deleted) {
      // not awaited: a detail page that is about to leave would otherwise
      // refetch the quote it just deleted and say it is not found first
      void queryClient.invalidateQueries();
    } else {
      await queryClient.invalidateQueries();
    }
    return deleted;
  }

  /** Asks, then creates the invoice behind the blocking overlay while its PDFs
   *  render, and opens it. */
  async function convert(id: number, label: string): Promise<void> {
    if (!(await ask(t("confirmConvertQuote", [label])))) {
      return;
    }
    let invoiceId: number | undefined;
    const converted = await once(id, () =>
      run(t("invoiceRunning"), async () => {
        invoiceId = await convertQuote(id);
        // under the overlay, as the editor does: the lists come back current
        await queryClient.invalidateQueries();
      }),
    );
    if (converted && invoiceId != null) {
      await router.push(`/invoices/${invoiceId}`);
    } else {
      await queryClient.invalidateQueries();
    }
  }

  return { decline, reopen, remove, convert };
}
